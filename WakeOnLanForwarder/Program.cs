using System.Net;
using System.Text;
using System.Net.Sockets;

const string PREFIX = "http://+:8080/"; // listen on all interfaces port 8080

if (!HttpListener.IsSupported)
{
    Console.Error.WriteLine("HttpListener not supported on this platform.");
    return;
}

using var listener = new HttpListener();
listener.Prefixes.Add(PREFIX);

try
{
    listener.Start();
}
catch (HttpListenerException e)
{
    Console.Error.WriteLine($"Failed to start listener: {e.Message}");
    return;
}

Console.WriteLine($"Listening at {PREFIX} (Ctrl+C to stop). GET /wake triggers WOL.");

while (true)
{
    var context = await listener.GetContextAsync();
    _ = Task.Run(() => HandleRequest(context));
}

static void HandleRequest(HttpListenerContext ctx)
{
    try
    {
        var req = ctx.Request;
        var resp = ctx.Response;

        if (!string.Equals(req.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(req.Url?.AbsolutePath, "/wake", StringComparison.OrdinalIgnoreCase))
        {
            resp.StatusCode = 404;
            resp.Close();
            return;
        }

        string? macAddress = req.QueryString["mac"];
        string? ipAddress = req.QueryString["ip"];

        if (macAddress is null || ipAddress is null)
        {
            resp.StatusCode = 404;
            resp.Close();
            return;
        }

        bool ok = SendWakeOnLan(macAddress, ipAddress, out string sendResult);

        string body = $"sent: {macAddress}\nresult: {ok}\n{sendResult}";
        byte[] data = Encoding.UTF8.GetBytes(body);
        resp.ContentType = "text/plain";
        resp.ContentLength64 = data.Length;
        resp.OutputStream.Write(data, 0, data.Length);
        resp.Close();
    }
    catch (Exception ex)
    {
        try
        {
            var resp = ctx.Response;
            resp.StatusCode = 500;
            byte[] b = Encoding.UTF8.GetBytes("error: " + ex.Message);
            resp.OutputStream.Write(b, 0, b.Length);
            resp.Close();
        }
        catch
        {
            // ignored
        }
    }
}

static bool SendWakeOnLan(string macAddress, string ipAddress, out string result)
{
    try
    {
        string[] parts = macAddress.Split([':', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6)
        {
            result = "Invalid MAC format";
            return false;
        }

        byte[] macBytes = new byte[6];
        for (int i = 0; i < 6; i++)
        {
            macBytes[i] = Convert.ToByte(parts[i], 16);
        }

        byte[] packet = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++) packet[i] = 0xFF;
        for (int i = 6; i < packet.Length; i += 6)
            Buffer.BlockCopy(macBytes, 0, packet, i, 6);

        using var udp = new UdpClient();
        udp.EnableBroadcast = true;

        // Try broadcast to 255.255.255.255 and port 9
        udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 9));

        // Also attempt to send to common subnet broadcast if IP_ADDRESS provided
        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            try
            {
                var ip = IPAddress.Parse(ipAddress);
                var broadcast = GetSubnetBroadcast(ip);
                udp.Send(packet, packet.Length, new IPEndPoint(broadcast!, 9));
            }
            catch
            {
                /* ignore */
            }
        }

        result = "magic packet sent";
        return true;
    }
    catch (Exception ex)
    {
        result = ex.ToString();
        return false;
    }
}

static IPAddress? GetSubnetBroadcast(IPAddress ip)
{
    // naive /24 broadcast calculation
    if (ip.AddressFamily != AddressFamily.InterNetwork) 
        return null;
    byte[] bytes = ip.GetAddressBytes();
    bytes[3] = 255;
    return new IPAddress(bytes);
}