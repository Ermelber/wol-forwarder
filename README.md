# Wake-on-LAN HTTP Trigger — README

> [!CAUTION]
> Disclaimer: this was created by GPT 5.0 (created in ~5 minutes); not formally approved — NOT FOR PRODUCTION USE.

Simple .NET console app that listens on port 8080 and triggers Wake-on-LAN by running the system `wakeonlan` command.

## Features
- Listens on all interfaces (http://+:8080/)
- GET /wake?mac=<MAC>&ip=<IP> sends a WOL magic packet for the given MAC address.
- Runs `wakeonlan` (expected at /opt/homebrew/bin/wakeonlan or available via /usr/bin/env) and returns command output.

## Tested platforms
- Only tested on **macOS**.

## Build & run
1. Clone the project
2. Build:
   - dotnet build
3. Run:
   - dotnet run  
   The app prints: Listening at http://+:8080/ (Ctrl+C to stop).

## Usage
Request:
- Method: GET
- URL: http://<host>:8080/wake?mac=AA:BB:CC:DD:EE:FF
- Optional: &ip=192.168.1.255 (use to specify target IP/broadcast)

Behavior:
- Missing or invalid MAC → 404
- Wrong path or method → 404
- On success, response body (text/plain) includes the MAC, boolean result, and command output:
  sent: <MAC>  
  result: True|False  
  exit=...  
  stdout:...  
  stderr:...

## Implementation notes
- The listener uses HttpListener and handles each request on a background Task.
- MAC is validated by splitting on ':' or '-' and expecting 6 parts.
- The code spawns an external process via /usr/bin/env with the wakeonlan path and arguments; stdout/stderr and exit code are captured and returned.
- If an IP is provided, the code appends `-i <ip>` to the wakeonlan command.

## Security and networking considerations
- The server listens on all interfaces and accepts unauthenticated GET requests; restrict access (firewall, bind to localhost, or add auth) before exposing to untrusted networks.
- Ensure the targeted network allows broadcast WOL packets and the `wakeonlan` command supports the provided IP option on your platform.
- Validate MAC/IP values further if exposing publicly.

## Troubleshooting
- "HttpListener not supported" → run on a platform/build that supports HttpListener.
- Failure to start listener → likely permission or port already in use.
- wakeonlan not found → install wakeonlan or update the path in code.
- WOL not working → check target machine BIOS/OS WOL settings and correct broadcast IP.

## Example curl
curl "http://localhost:8080/wake?mac=AA:BB:CC:DD:EE:FF&ip=192.168.1.255"

## License
Use as needed; no license specified in source.