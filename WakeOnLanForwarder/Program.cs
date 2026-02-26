using System.Diagnostics;
using System.Net;
using System.Text;

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

        if (macAddress is null)
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

static bool SendWakeOnLan(string macAddress, string? ipAddress, out string result)
{
    try
    {
        // Validate MAC quickly
        string[] parts = macAddress.Split([':', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6)
        {
            result = "Invalid MAC format";
            return false;
        }

        // Build command: wakeonlan [options] MAC
        // macOS wakeonlan accepts MAC and optional -i IP (target IP/broadcast)
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/env",
            ArgumentList = { "/opt/homebrew/bin/wakeonlan", macAddress },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // If an IP/broadcast provided, append -i <ip>
        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(ipAddress);
        }

        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        result = $"exit={p.ExitCode}\nstdout:{stdout}\nstderr:{stderr}";
        return p.ExitCode == 0;
    }
    catch (Exception ex)
    {
        result = ex.ToString();
        return false;
    }
}