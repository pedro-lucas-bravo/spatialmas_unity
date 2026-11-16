using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
        private const string MetadataPrefix = "session_metadata_";
        private const string SessionRoute = "/sessions/";
        private const string FolderSessionPrefix = "folder-";
        private static readonly string[] CsvPrefixes =
        {
            "osc_traffic_", "xr_embodiment_", "object_state_", "calibration_", "session_events_"
        };

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
                        string path = parts.Length >= 2 ? parts[1] : "/";

                        int queryIdx = path.IndexOf('?');
                        if (queryIdx >= 0) path = path.Substring(0, queryIdx);
                        path = Uri.UnescapeDataString(path);

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
                        else if (path.StartsWith(SessionRoute, StringComparison.Ordinal) && path.EndsWith(".zip", StringComparison.Ordinal))
                        {
                            string session = path.Substring(SessionRoute.Length, path.Length - SessionRoute.Length - 4);
                            await WriteSessionZipAsync(stream, session, token).ConfigureAwait(false);
                        }
                        else if (path.StartsWith("/logs/", StringComparison.OrdinalIgnoreCase))
                        {
                            string relativePath = path.Substring("/logs/".Length);
                            FileInfo file = _recordingService.GetRecordedLogFiles()
                                .FirstOrDefault(candidate => RelativeLogPath(candidate) == relativePath);
                            if (file != null)
                            {
                                bool isMetadata = file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase);
                                byte[] fileBytes = ReadFileShared(file.FullName);
                                await WriteHttpResponseAsync(stream, "200 OK", isMetadata ? "application/json; charset=utf-8" : "text/csv; charset=utf-8", fileBytes, file.Name).ConfigureAwait(false);
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

        private string RelativeLogPath(FileInfo file)
            => Path.GetRelativePath(_recordingService.LogsDirectory, file.FullName)
                .Replace(Path.DirectorySeparatorChar, '/');

        private string LogUrl(FileInfo file)
            => "/logs/" + string.Join("/", RelativeLogPath(file).Split('/').Select(Uri.EscapeDataString));

        private string SessionName(FileInfo file)
        {
            string relativePath = RelativeLogPath(file);
            int slash = relativePath.IndexOf('/');
            if (slash >= 0) return FolderSessionPrefix + relativePath.Substring(0, slash);
            string name = Path.GetFileNameWithoutExtension(file.Name);
            if (name.Contains("\\")) return "";
            if (file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                return name.StartsWith(MetadataPrefix, StringComparison.Ordinal) ? name.Substring(MetadataPrefix.Length) : "";
            foreach (string prefix in CsvPrefixes)
                if (name.StartsWith(prefix, StringComparison.Ordinal)) return name.Substring(prefix.Length);
            return "";
        }

        private static string SessionLabel(string session, string format)
        {
            bool folderSession = session.StartsWith(FolderSessionPrefix, StringComparison.Ordinal);
            string name = folderSession ? session.Substring(FolderSessionPrefix.Length) : session;
            if (!DataRecordingService.TryParseSessionTimestamp(name, out DateTime timestamp)) return name;
            string suffix = folderSession ? name.Substring(DataRecordingService.SessionTimestampFormat.Length) : "";
            return timestamp.ToString(format, CultureInfo.InvariantCulture) + suffix;
        }

        private async Task WriteSessionZipAsync(Stream stream, string session, CancellationToken token)
        {
            FileInfo[] files = _recordingService.GetRecordedLogFiles(out string activeMetadataPath);
            FileInfo[] members = string.IsNullOrEmpty(session) ? Array.Empty<FileInfo>() :
                files.Where(file => SessionName(file) == session).ToArray();
            if (members.Length == 0)
            {
                await WriteHttpResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes("Session not found."), null).ConfigureAwait(false);
                return;
            }
            if (members.Any(file => file.FullName == activeMetadataPath))
            {
                await WriteHttpResponseAsync(stream, "409 Conflict", "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes("Stop recording before downloading the session ZIP."), null).ConfigureAwait(false);
                return;
            }

            // Connection-close framing lets the archive stream without buffering an entire session.
            string folderName = SessionLabel(session, DataRecordingService.SessionTimestampFormat);
            await WriteHttpHeadersAsync(stream, "200 OK", "application/zip", null, folderName + ".zip").ConfigureAwait(false);
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                foreach (FileInfo member in members)
                {
                    token.ThrowIfCancellationRequested();
                    using (var input = new FileStream(member.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (Stream entry = zip.CreateEntry(folderName + "/" + member.Name, System.IO.Compression.CompressionLevel.Fastest).Open())
                        input.CopyTo(entry);
                }
            }
            await stream.FlushAsync().ConfigureAwait(false);
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
            await WriteHttpHeadersAsync(stream, status, contentType, body.Length, attachmentFileName).ConfigureAwait(false);
            if (body.Length > 0)
                await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        private static async Task WriteHttpHeadersAsync(Stream stream, string status, string contentType,
            int? contentLength, string attachmentFileName)
        {
            var sb = new StringBuilder(256);
            sb.Append("HTTP/1.1 ").Append(status).Append("\r\n");
            sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            if (contentLength.HasValue) sb.Append("Content-Length: ").Append(contentLength.Value).Append("\r\n");
            sb.Append("Access-Control-Allow-Origin: *\r\n");
            if (!string.IsNullOrEmpty(attachmentFileName))
            {
                sb.Append("Content-Disposition: attachment; filename*=UTF-8''").Append(Uri.EscapeDataString(attachmentFileName)).Append("\r\n");
            }
            sb.Append("Connection: close\r\n\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
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
                  .Append("\",\"url\":\"").Append(LogUrl(f)).Append("\"}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        private string BuildHtmlDashboard()
        {
            FileInfo[] files = _recordingService.GetRecordedLogFiles(out string activeMetadataPath);
            var sb = new StringBuilder(2048);
            sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'><title>SpatialMAS Researcher Telemetry Portal</title>");
            sb.Append("<style>");
            sb.Append("body{font-family:system-ui,-apple-system,sans-serif;background:#0d111a;color:#e6edf3;margin:0;padding:32px;}");
            sb.Append(".card{max-width:920px;margin:0 auto;background:#161b26;border:1px solid #2a3245;border-radius:12px;padding:24px;}");
            sb.Append("h1{margin-top:0;color:#38bdf8;font-size:24px;}");
            sb.Append("section{padding:16px 0;border-top:1px solid #2a3245;}summary{cursor:pointer;overflow-wrap:anywhere;}");
            sb.Append("li{margin:8px 0;overflow-wrap:anywhere;}a{color:#7dd3fc;}small{color:#b6c4d3;}");
            sb.Append("a.btn{background:#0284c7;color:#fff;padding:8px 14px;border-radius:6px;text-decoration:none;font-weight:600;display:inline-block;}");
            sb.Append("a.btn:hover{background:#0369a1;}");
            sb.Append("</style></head><body><div class='card'>");
            sb.Append("<h1>SpatialMAS — Wi-Fi Telemetry &amp; OSC Log Export</h1>");
            sb.Append("<p>Download synchronized world poses, object dimensions, calibration history, session events, OSC traffic, and coordinate metadata over Wi-Fi.</p>");
            if (files.Length == 0)
                sb.Append("<p>No session logs recorded yet.</p>");

            foreach (var session in files.GroupBy(SessionName))
            {
                bool active = session.Any(file => file.FullName == activeMetadataPath);
                sb.Append("<section><details><summary><strong>")
                  .Append(session.Key.Length == 0 ? "Other logs" : WebUtility.HtmlEncode(
                      SessionLabel(session.Key, "yyyy-MM-dd HH:mm:ss.fff 'UTC'")))
                  .Append("</strong> <small>").Append(session.Count()).Append(" files");
                if (active) sb.Append(" · Recording");
                sb.Append("</small></summary><ul>");
                foreach (FileInfo file in session)
                {
                    sb.Append("<li><a href='").Append(LogUrl(file)).Append("'>")
                      .Append(WebUtility.HtmlEncode(file.Name)).Append("</a> <small>")
                      .Append((file.Length / 1024.0).ToString("F1", CultureInfo.InvariantCulture))
                      .Append(" KB</small></li>");
                }
                sb.Append("</ul></details>");
                if (active)
                    sb.Append("<small>Stop recording and refresh to download the ZIP.</small>");
                else if (session.Key.Length > 0)
                    sb.Append("<a class='btn' href='").Append(SessionRoute).Append(Uri.EscapeDataString(session.Key))
                      .Append(".zip'>Download session ZIP</a>");
                sb.Append("</section>");
            }

            sb.Append("</div></body></html>");
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
