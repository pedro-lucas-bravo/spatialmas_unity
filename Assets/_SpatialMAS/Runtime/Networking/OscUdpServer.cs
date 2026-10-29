using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SpatialMAS.Networking
{
    /// <summary>
    /// High-throughput, IL2CPP-safe asynchronous UDP OSC Server.
    /// Eliminates the legacy Thread.Sleep(5) bottleneck and Thread.Abort() crash from human_swarm_ims,
    /// using a 1 MB socket receive buffer and a lock-free ConcurrentQueue.
    /// </summary>
    public class OscUdpServer : IDisposable
    {
        private readonly ConcurrentQueue<OscMessage> _incomingQueue = new ConcurrentQueue<OscMessage>();
        private Socket _socket;
        private CancellationTokenSource _cts;
        private Task _receiveTask;
        private int _listenPort;
        private int _receiveBufferSize;
        private bool _isRunning;
        private long _totalMessagesReceived;

        public int ListenPort => _listenPort;
        public bool IsRunning => _isRunning;
        public long TotalMessagesReceived => Interlocked.Read(ref _totalMessagesReceived);

        public bool StartServer(int port, int receiveBufferSize = 1048576)
        {
            StopServer();

            _listenPort = port;
            _receiveBufferSize = Mathf.Max(65536, receiveBufferSize);

            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _socket.ReceiveBufferSize = _receiveBufferSize;
                try
                {
                    _socket.IOControl((IOControlCode)(-1744830452), new byte[] { 0, 0, 0, 0 }, null);
                }
                catch { }
                _socket.Bind(new IPEndPoint(IPAddress.Any, _listenPort));

                _cts = new CancellationTokenSource();
                _isRunning = true;
                _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token));
                Debug.Log($"[OscUdpServer] Listening for OSC packets on UDP port {_listenPort} (buffer: {_receiveBufferSize / 1024} KB).");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OscUdpServer] Failed to bind UDP port {_listenPort}: {ex.Message}");
                StopServer();
                return false;
            }
        }

        private void ReceiveLoop(CancellationToken token)
        {
            byte[] buffer = new byte[65536];
            var decodedBatch = new List<OscMessage>(32);
            EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

            while (!token.IsCancellationRequested && _socket != null)
            {
                try
                {
                    int bytesRead = _socket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref remoteEp);
                    if (bytesRead > 0)
                    {
                        decodedBatch.Clear();
                        OscPacketCodec.DecodePacket(buffer, 0, bytesRead, decodedBatch);
                        for (int i = 0; i < decodedBatch.Count; i++)
                        {
                            _incomingQueue.Enqueue(decodedBatch[i]);
                            Interlocked.Increment(ref _totalMessagesReceived);
                        }
                    }
                }
                catch (SocketException)
                {
                    if (token.IsCancellationRequested) break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        Debug.LogWarning($"[OscUdpServer] Packet decode error: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Drains up to maxMessages from the lock-free queue on the Unity Main Thread.
        /// </summary>
        public int DrainMessages(List<OscMessage> destination, int maxMessages = 4096)
        {
            int count = 0;
            while (count < maxMessages && _incomingQueue.TryDequeue(out OscMessage msg))
            {
                destination.Add(msg);
                count++;
            }
            return count;
        }

        /// <summary>
        /// Injects a message directly (useful for PlayMode unit tests without socket overhead).
        /// </summary>
        public void InjectTestMessage(OscMessage msg)
        {
            _incomingQueue.Enqueue(msg);
            Interlocked.Increment(ref _totalMessagesReceived);
        }

        public void StopServer()
        {
            _isRunning = false;
            try
            {
                _cts?.Cancel();
            }
            catch { }

            try
            {
                _socket?.Close();
                _socket?.Dispose();
            }
            catch { }

            _socket = null;
            _cts?.Dispose();
            _cts = null;
        }

        public void Dispose()
        {
            StopServer();
        }
    }
}
