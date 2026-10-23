using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace SpatialMAS.Networking
{
    /// <summary>
    /// Thread-safe UDP OSC Client that sends outgoing OSC messages to the active external controller IP:Port.
    /// Automatically reconfigures target endpoint when /test/alive or /agents/instantiate is received.
    /// </summary>
    public class OscUdpClient : IDisposable
    {
        private Socket _socket;
        private IPEndPoint _remoteEndPoint;
        private string _remoteIp = "127.0.0.1";
        private int _remotePort = 6012;
        private long _totalMessagesSent;
        private readonly object _lock = new object();

        public string RemoteIp => _remoteIp;
        public int RemotePort => _remotePort;
        public long TotalMessagesSent => Interlocked.Read(ref _totalMessagesSent);

        public OscUdpClient(string defaultIp = "127.0.0.1", int defaultPort = 6012)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SendBufferSize = 524288;
            SetDestination(defaultIp, defaultPort);
        }

        public bool SetDestination(string ipAddress, int port)
        {
            if (string.IsNullOrWhiteSpace(ipAddress) || port <= 0 || port > 65535)
                return false;

            string trimmedIp = ipAddress.Trim();
            if (trimmedIp.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                trimmedIp = "127.0.0.1";
            }

            if (!IPAddress.TryParse(trimmedIp, out IPAddress parsedIp))
            {
                Debug.LogWarning($"[OscUdpClient] Invalid remote IP address '{ipAddress}'. Keeping '{_remoteIp}:{_remotePort}'.");
                return false;
            }

            lock (_lock)
            {
                _remoteIp = trimmedIp;
                _remotePort = port;
                _remoteEndPoint = new IPEndPoint(parsedIp, port);
            }
            return true;
        }

        public void Send(OscMessage message)
        {
            IPEndPoint targetEp;
            Socket sock;
            lock (_lock)
            {
                targetEp = _remoteEndPoint;
                sock = _socket;
            }

            if (sock == null || targetEp == null) return;

            try
            {
                byte[] payload = OscPacketCodec.EncodeMessage(message);
                sock.SendTo(payload, 0, payload.Length, SocketFlags.None, targetEp);
                Interlocked.Increment(ref _totalMessagesSent);
            }
            catch (Exception ex)
            {
                // Suppress transient unreachable errors on localhost when external controller is closed
                if (!(ex is SocketException))
                {
                    Debug.LogWarning($"[OscUdpClient] Send error: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                try
                {
                    _socket?.Close();
                    _socket?.Dispose();
                }
                catch { }
                _socket = null;
            }
        }
    }
}
