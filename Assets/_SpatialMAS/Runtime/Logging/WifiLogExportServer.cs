using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SpatialMAS.Logging
{
    /// <summary>
    /// Embedded Wi-Fi HTTP/1.1 Log Export Server (http://&lt;headset-ip&gt;:8080/).
    /// Built on raw TcpListener (instead of HttpListener) for 100% unprivileged cross-platform compatibility
    /// across Meta Quest 3 (Android), Apple Vision Pro (visionOS), Windows, macOS, and Linux.
    /// Allows researchers to browse and download OSC &amp; XR Embodiment CSV logs directly from any laptop browser
    /// over Wi-Fi without needing an 'adb pull' USB cable or Xcode container export.
    /// </summary>
    public class WifiLogExportServer : IDisposable
    {
        private readonly DataRecordingService _recordingService;
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private int _port;
        private bool _isRunning;

        public int Port => _port;
        public bool IsRunning => _isRunning;
        public string DashboardUrl => $"http://{GetLocalWifiIpAddress()}:{_port}/";

        public WifiLogExportServer(DataRecordingService recordingService)
        {
            _recordingService = recordingService ?? throw new ArgumentNullException(nameof(recordingService));
        }

        public bool StartServer(int port = 8080)
        {
            StopServer();
            _port = port;

            try
            {
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();
                // If port 0 was passed (ephemeral port for unit tests), capture the assigned port
                _port = ((IPEndPoint)_listener.LocalEndpoint).Port;

                _cts = new CancellationTokenSource();
                _isRunning = true;
                Task.Run(() => AcceptLoopAsync(_cts.Token));
                Debug.Log($"[WifiLogExportServer] Researcher Wi-Fi CSV Dashboard live at {DashboardUrl}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WifiLogExportServer] Could not bind port {_port}: {ex.Message}");
                StopServer();
                return false;
            }
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleClientAsync(client, token), token);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    if (token.IsCancellationRequested) break;
                }
                catch (Exception)
                {
                    if (token.IsCancellationRequested) break;
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 10000;
                    using (NetworkStream stream = client.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true))
                    {
                        string requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (string.IsNullOrEmpty(requestLine)) return;

                        // Drain remaining HTTP headers
                        string headerLine;
                        while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync().ConfigureAwait(false)))
                        {
                        }

                        string[] parts = requestLine.Split(' ');
                        string path = parts.Length >= 2 ? Uri.UnescapeDataString(parts[1]) : "/";

                        int queryIdx = path.IndexOf('?');
                        if (queryIdx >= 0) path = path.Substring(0, queryIdx);

                        if (path == "/" || path == "/index.html")
                        {
                            byte[] htmlBytes = Encoding.UTF8.GetBytes(BuildHtmlDashboard());
                            await WriteHttpResponseAsync(stream, "200 OK", "text/html; charset=utf-8", htmlBytes, null).ConfigureAwait(false);
                        }
                        else if (path == "/api/logs")
                        {
                            byte[] jsonBytes = Encoding.UTF8.GetBytes(BuildJsonLogsList());
                            await WriteHttpResponseAsync(stream, "200 OK", "application/json; charset=utf-8", jsonBytes, null).ConfigureAwait(false);
                        }
                        else if (path.StartsWith("/logs/", StringComparison.OrdinalIgnoreCase))
                        {
                            string fileName = Path.GetFileName(path.Substring("/logs/".Length));
                            string fullPath = Path.Combine(_recordingService.LogsDirectory, fileName);

                            _recordingService.Flush();
                            bool isMetadata = fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                            if (File.Exists(fullPath) && (isMetadata || fullPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
                            {
                                byte[] fileBytes = ReadFileShared(fullPath);
                                await WriteHttpResponseAsync(stream, "200 OK", isMetadata ? "application/json; charset=utf-8" : "text/csv; charset=utf-8", fileBytes, fileName).ConfigureAwait(false);
                            }
                            else
                            {
                                byte[] notFound = Encoding.UTF8.GetBytes("404 Session Log Not Found");
                                await WriteHttpResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", notFound, null).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            byte[] notFound = Encoding.UTF8.GetBytes("404 Not Found");
                            await WriteHttpResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", notFound, null).ConfigureAwait(false);
                        }
                    }
                }
                catch { }
            }
        }

        private static byte[] ReadFileShared(string fullPath)
        {
            using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var ms = new MemoryStream((int)fs.Length))
            {
                fs.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static async Task WriteHttpResponseAsync(
            Stream stream,
            string status,
            string contentType,
            byte[] body,
            string attachmentFileName)
        {
            var sb = new StringBuilder(256);
            sb.Append("HTTP/1.1 ").Append(status).Append("\r\n");
            sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            sb.Append("Access-Control-Allow-Origin: *\r\n");
            if (!string.IsNullOrEmpty(attachmentFileName))
            {
                sb.Append("Content-Disposition: attachment; filename=\"").Append(attachmentFileName).Append("\"\r\n");
            }
            sb.Append("Connection: close\r\n\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
            if (body.Length > 0)
            {
                await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
            }
            await stream.FlushAsync().ConfigureAwait(false);
        }

        private string BuildJsonLogsList()
        {
            FileInfo[] files = _recordingService.GetRecordedLogFiles();
            var sb = new StringBuilder(512);
            sb.Append('[');
            for (int i = 0; i < files.Length; i++)
            {
                if (i > 0) sb.Append(',');
                FileInfo f = files[i];
                sb.Append("{\"name\":\"").Append(f.Name)
                  .Append("\",\"size\":").Append(f.Length)
                  .Append(",\"lastModifiedUtc\":\"").Append(f.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture))
                  .Append("\",\"url\":\"/logs/").Append(Uri.EscapeDataString(f.Name)).Append("\"}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        private string BuildHtmlDashboard()
        {
            FileInfo[] files = _recordingService.GetRecordedLogFiles();
            var sb = new StringBuilder(2048);
            sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'><title>SpatialMAS Researcher Telemetry Portal</title>");
            sb.Append("<style>");
            sb.Append("body{font-family:system-ui,-apple-system,sans-serif;background:#0d111a;color:#e6edf3;margin:0;padding:32px;}");
            sb.Append(".card{max-width:920px;margin:0 auto;background:#161b26;border:1px solid #2a3245;border-radius:12px;padding:24px;}");
            sb.Append("h1{margin-top:0;color:#38bdf8;font-size:24px;}");
            sb.Append("table{width:100%;border-collapse:collapse;margin-top:16px;}");
            sb.Append("th,td{text-align:left;padding:12px;border-bottom:1px solid #262e40;}");
            sb.Append("a.btn{background:#0284c7;color:#fff;padding:8px 14px;border-radius:6px;text-decoration:none;font-weight:600;display:inline-block;}");
            sb.Append("a.btn:hover{background:#0369a1;}");
            sb.Append("</style></head><body><div class='card'>");
            sb.Append("<h1>SpatialMAS — Wi-Fi Telemetry &amp; OSC Log Export</h1>");
            sb.Append("<p>Download synchronized world poses, object dimensions, calibration history, session events, OSC traffic, and coordinate metadata over Wi-Fi.</p>");
            sb.Append("<table><thead><tr><th>Session File</th><th>Size (KB)</th><th>Last Updated (UTC)</th><th>Action</th></tr></thead><tbody>");

            if (files.Length == 0)
            {
                sb.Append("<tr><td colspan='4'>No session logs recorded yet.</td></tr>");
            }
            else
            {
                for (int i = 0; i < files.Length; i++)
                {
                    FileInfo f = files[i];
                    double kb = f.Length / 1024.0;
                    string url = "/logs/" + Uri.EscapeDataString(f.Name);
                    sb.Append("<tr><td><code>").Append(f.Name).Append("</code></td>")
                      .Append("<td>").Append(kb.ToString("F1", CultureInfo.InvariantCulture)).Append(" KB</td>")
                      .Append("<td>").Append(f.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append("</td>")
                      .Append("<td><a class='btn' href='").Append(url).Append("'>Download</a></td></tr>");
                }
            }

            sb.Append("</tbody></table></div></body></html>");
            return sb.ToString();
        }

        public static string GetLocalWifiIpAddress()
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip.Address))
                        {
                            return ip.Address.ToString();
                        }
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        public void StopServer()
        {
            _isRunning = false;
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            _listener = null;
            _cts?.Dispose();
            _cts = null;
        }

        public void Dispose()
        {
            StopServer();
        }
    }
}
