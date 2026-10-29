using System;
using System.Collections.Generic;
using System.Globalization;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Networking
{
    /// <summary>
    /// Translates all incoming OSC v1 (100% HS-ims / human_swarm_examples backward-compatible)
    /// and v2 (SpatialMAS XR & Scenario) messages into SimulationCommandBus commands,
    /// and forwards outgoing world events back over UDP.
    /// </summary>
    public class OscProtocolRouter : IDisposable
    {
        private readonly SimulationCommandBus _bus;
        private readonly SpatialCoordinateMapper _mapper;
        private readonly OscUdpServer _server;
        private readonly OscUdpClient _client;
        private readonly List<OscMessage> _drainBuffer = new List<OscMessage>(512);

        public bool StreamUserPoseEnabled { get; set; } = true;

        public OscProtocolRouter(
            SimulationCommandBus bus,
            SpatialCoordinateMapper mapper,
            OscUdpServer server,
            OscUdpClient client)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _client = client ?? throw new ArgumentNullException(nameof(client));

            // Subscribe to outgoing feedback events from the simulation world
            _bus.OnAgentMovedByUserUnity += HandleAgentMovedByUser;
            _bus.OnAgentTouchEvent += HandleAgentTouch;
            _bus.OnAgentControlClickEvent += HandleAgentControlClick;
            _bus.OnLocalCommDeliveredUnity += HandleLocalCommDelivered;
            _bus.OnBoundaryTouchedEvent += HandleBoundaryTouched;
            _bus.OnBoundaryTransitionEvent += HandleBoundaryTransition;
            _bus.OnUserHeadPoseSampledUnity += HandleUserHeadPoseSampled;
            _bus.OnUserHandPoseSampledUnity += HandleUserHandPoseSampled;
        }

        /// <summary>
        /// Called every Update() on the Unity Main Thread to drain and dispatch queued UDP packets.
        /// </summary>
        public void ProcessIncomingMessages()
        {
            _drainBuffer.Clear();
            int count = _server.DrainMessages(_drainBuffer, 4096);
            for (int i = 0; i < count; i++)
            {
                DispatchMessage(_drainBuffer[i]);
            }
        }

        public void DispatchMessage(OscMessage msg)
        {
            string addr = msg.Address;
            if (string.IsNullOrEmpty(addr)) return;

            // Log incoming OSC message for CSV recorder
            _bus.LogOscTraffic("IN", addr, msg.FormatArgumentsCsv());

            switch (addr)
            {
                // =====================================================================
                // 1. Handshake & Alive Endpoints (HS-ims /connect & /test/alive)
                // =====================================================================
                case "/connect":
                case "/test/alive":
                case "/test/alive/":
                {
                    string ip = msg.GetString(0, _client.RemoteIp);
                    int port = msg.GetInt(1, _client.RemotePort);
                    float testVal = msg.GetFloat(2, 1f);
                    _client.SetDestination(ip, port);
                    _bus.NotifyTestAlive(ip, port, testVal);

                    // Reply with HS-ims expected handshake acknowledgment (/test/alive/ <ip> <port> "OK")
                    SendAndLog(new OscMessage("/test/alive/", ip, port, "OK"));
                    break;
                }

                // =====================================================================
                // 2. Agent Instantiation, Addition & Removal (v1 HS-ims + v2 SpatialMAS)
                // =====================================================================
                case "/agents/instantiate":
                {
                    if (msg.ArgCount == 0) break;

                    // Dual signature support:
                    // - v2 6-arg format: (shape, movMode, count, scale, ip, port)
                    // - v1 HS-ims 1..3-arg format: (N, [shape=0], [movMode=0])
                    if (msg.ArgCount >= 6 && msg.Arguments[4] is string)
                    {
                        var shape = (AgentShapeType)Mathf.Clamp(msg.GetInt(0), 0, 3);
                        var movMode = (AgentMovementMode)Mathf.Clamp(msg.GetInt(1), 0, 2);
                        int nAgents = Mathf.Clamp(msg.GetInt(2), 0, 512);
                        float rawScale = Mathf.Max(0.005f, msg.GetFloat(3, 0.25f));
                        string remoteIp = msg.GetString(4, _client.RemoteIp);
                        int remotePort = msg.GetInt(5, _client.RemotePort);

                        _client.SetDestination(remoteIp, remotePort);
                        float scaledRadius = _mapper.ScaleAgentMeterRadius(rawScale);
                        _bus.InstantiateAgents(shape, movMode, nAgents, scaledRadius, remoteIp, remotePort);
                    }
                    else
                    {
                        int nAgents = Mathf.Clamp(msg.GetInt(0), 0, 512);
                        var shape = (AgentShapeType)Mathf.Clamp(msg.GetInt(1, 0), 0, 3);
                        var movMode = (AgentMovementMode)Mathf.Clamp(msg.GetInt(2, 0), 0, 2);
                        float scaledRadius = _mapper.ScaleAgentMeterRadius(0.28f);
                        _bus.InstantiateAgents(shape, movMode, nAgents, scaledRadius, _client.RemoteIp, _client.RemotePort);
                    }
                    break;
                }

                case "/agents/instantiate/id":
                {
                    // HS-ims signature: <shape> <movMode> <id0> <id1> ...
                    if (msg.ArgCount < 3) break;
                    var shape = (AgentShapeType)Mathf.Clamp(msg.GetInt(0), 0, 3);
                    var movMode = (AgentMovementMode)Mathf.Clamp(msg.GetInt(1), 0, 2);
                    int idCount = Mathf.Min(msg.ArgCount - 2, 512);
                    int[] ids = new int[idCount];
                    for (int i = 0; i < idCount; i++)
                    {
                        ids[i] = msg.GetInt(2 + i);
                    }

                    // In HS-ims scripts (e.g. swarmalators/main.py), /agents/instantiate/id is called per shape group
                    // (first spheres, then cubes). Add or reconfigure the specified IDs without wiping previous groups!
                    float scaledRadius = _mapper.ScaleAgentMeterRadius(shape == AgentShapeType.Cube ? 0.35f : 0.28f);
                    _bus.AddAgents(shape, movMode, scaledRadius, _client.RemoteIp, _client.RemotePort, ids);
                    break;
                }

                case "/agents/add":
                case "/agents/add/id":
                {
                    if (msg.ArgCount == 0) break;

                    // v2 6+ arg format: (shape, movMode, scale, ip, port, id0, id1, ...)
                    if (msg.ArgCount >= 6 && msg.Arguments[3] is string)
                    {
                        var shape = (AgentShapeType)Mathf.Clamp(msg.GetInt(0), 0, 3);
                        var movMode = (AgentMovementMode)Mathf.Clamp(msg.GetInt(1), 0, 2);
                        float rawScale = Mathf.Max(0.005f, msg.GetFloat(2, 0.28f));
                        string remoteIp = msg.GetString(3, _client.RemoteIp);
                        int remotePort = msg.GetInt(4, _client.RemotePort);

                        _client.SetDestination(remoteIp, remotePort);
                        int idCount = Mathf.Min(msg.ArgCount - 5, 512);
                        int[] ids = new int[idCount];
                        for (int i = 0; i < idCount; i++)
                        {
                            ids[i] = msg.GetInt(5 + i);
                        }
                        float scaledRadius = _mapper.ScaleAgentMeterRadius(rawScale);
                        _bus.AddAgents(shape, movMode, scaledRadius, remoteIp, remotePort, ids);
                    }
                    else if (addr == "/agents/add/id" && msg.ArgCount >= 3)
                    {
                        var shape = (AgentShapeType)Mathf.Clamp(msg.GetInt(0), 0, 3);
                        var movMode = (AgentMovementMode)Mathf.Clamp(msg.GetInt(1), 0, 2);
                        int idCount = Mathf.Min(msg.ArgCount - 2, 512);
                        int[] ids = new int[idCount];
                        for (int i = 0; i < idCount; i++) ids[i] = msg.GetInt(2 + i);
                        float scaledRadius = _mapper.ScaleAgentMeterRadius(0.28f);
                        _bus.AddAgents(shape, movMode, scaledRadius, _client.RemoteIp, _client.RemotePort, ids);
                    }
                    else
                    {
                        // HS-ims /agents/add: N [shape=0] [movMode=0] [initPosMode=0] ...
                        int n = Mathf.Clamp(msg.GetInt(0), 0, 512);
                        var shape = (AgentShapeType)Mathf.Clamp(msg.GetInt(1, 0), 0, 3);
                        var movMode = (AgentMovementMode)Mathf.Clamp(msg.GetInt(2, 0), 0, 2);
                        int[] ids = new int[n];
                        int baseId = (int)(Time.frameCount % 1000) * 100;
                        for (int i = 0; i < n; i++) ids[i] = baseId + i;
                        float scaledRadius = _mapper.ScaleAgentMeterRadius(0.28f);
                        _bus.AddAgents(shape, movMode, scaledRadius, _client.RemoteIp, _client.RemotePort, ids);
                    }
                    break;
                }

                case "/agents/remove":
                case "/agents/remove/id":
                {
                    if (msg.ArgCount == 0)
                    {
                        _bus.RemoveAgents(null);
                    }
                    else if (addr == "/agents/remove" && msg.ArgCount == 3 && msg.GetInt(1) < 0)
                    {
                        // HS-ims /agents/remove <N> -1 -1
                        _bus.RemoveAgents(null);
                    }
                    else
                    {
                        int[] ids = new int[msg.ArgCount];
                        for (int i = 0; i < msg.ArgCount; i++) ids[i] = msg.GetInt(i);
                        _bus.RemoveAgents(ids);
                    }
                    break;
                }

                // =====================================================================
                // 3. Agent Transform, Velocity, Color & Radius Endpoints
                // =====================================================================
                case "/agents/position/id":
                {
                    // Supports both single (id, x, y, z) and HS-ims packed tuples (id0, x0, y0, z0, id1, x1, y1, z1, ...)
                    for (int i = 0; i + 3 < msg.ArgCount; i += 4)
                    {
                        int id = msg.GetInt(i);
                        float xMm = msg.GetFloat(i + 1);
                        float yMm = msg.GetFloat(i + 2);
                        float zMm = msg.GetFloat(i + 3);
                        Vector3 posUnity = _mapper.OscMillimetersToUnityMeters(xMm, yMm, zMm);
                        _bus.SetAgentPositionUnity(id, posUnity);
                    }
                    break;
                }

                case "/agents/position":
                {
                    int startOffset = (msg.ArgCount > 3 && msg.ArgCount % 3 == 2) ? 2 : 0;
                    int n = (msg.ArgCount - startOffset) / 3;
                    if (n <= 0) break;
                    Vector3[] positions = new Vector3[n];
                    for (int i = 0; i < n; i++)
                    {
                        int baseIdx = startOffset + i * 3;
                        positions[i] = _mapper.OscMillimetersToUnityMeters(
                            msg.GetFloat(baseIdx),
                            msg.GetFloat(baseIdx + 1),
                            msg.GetFloat(baseIdx + 2));
                    }
                    _bus.SetBatchAgentPositionsUnity(positions);
                    break;
                }

                case "/agents/speed/id":
                {
                    for (int i = 0; i + 1 < msg.ArgCount; i += 2)
                    {
                        int id = msg.GetInt(i);
                        float speedMmPerSec = msg.GetFloat(i + 1);
                        float speedMetersPerSec = _mapper.OscMillimeterLengthToUnityMeters(speedMmPerSec);
                        _bus.SetAgentSpeedMetersPerSec(id, speedMetersPerSec);
                    }
                    break;
                }

                case "/agents/direction/id":
                {
                    for (int i = 0; i + 3 < msg.ArgCount; i += 4)
                    {
                        int id = msg.GetInt(i);
                        float dx = msg.GetFloat(i + 1);
                        float dy = msg.GetFloat(i + 2);
                        float dz = msg.GetFloat(i + 3);
                        // Swap Y and Z to match Unity left-handed axes
                        Vector3 dirUnity = new Vector3(dx, dz, dy);
                        _bus.SetAgentDirectionUnity(id, dirUnity);
                    }
                    break;
                }

                case "/agents/velocity/id":
                {
                    for (int i = 0; i + 3 < msg.ArgCount; i += 4)
                    {
                        int id = msg.GetInt(i);
                        float vxMm = msg.GetFloat(i + 1);
                        float vyMm = msg.GetFloat(i + 2);
                        float vzMm = msg.GetFloat(i + 3);
                        Vector3 velUnity = new Vector3(
                            _mapper.OscMillimeterLengthToUnityMeters(vxMm),
                            _mapper.OscMillimeterLengthToUnityMeters(vzMm),
                            _mapper.OscMillimeterLengthToUnityMeters(vyMm));
                        _bus.SetAgentVelocityUnity(id, velUnity);
                    }
                    break;
                }

                case "/agents/color/id":
                {
                    if (msg.ArgCount < 2) break;

                    // Dual signature support:
                    // 1. HS-ims hex string pairs: (id0, "00e5ff", id1, "ff0000", ...)
                    // 2. SpatialMAS float RGBA: (id, r, g, b, [a])
                    if (msg.Arguments[1] is string)
                    {
                        for (int i = 0; i + 1 < msg.ArgCount; i += 2)
                        {
                            int id = msg.GetInt(i);
                            string hex = msg.GetString(i + 1);
                            if (TryParseHexColor(hex, out Color parsed))
                            {
                                _bus.SetAgentColor(id, parsed);
                            }
                        }
                    }
                    else if (msg.ArgCount >= 4)
                    {
                        int id = msg.GetInt(0);
                        Color c = new Color(
                            Mathf.Clamp01(msg.GetFloat(1)),
                            Mathf.Clamp01(msg.GetFloat(2)),
                            Mathf.Clamp01(msg.GetFloat(3)),
                            Mathf.Clamp01(msg.GetFloat(4, 1f)));
                        _bus.SetAgentColor(id, c);
                    }
                    break;
                }

                case "/agents/color":
                {
                    if (msg.ArgCount >= 4 && msg.Arguments[3] is string)
                    {
                        // HS-ims format: N, shape, colorAction, hex0, hex1, ...
                        int hexCount = msg.ArgCount - 3;
                        Color[] colors = new Color[hexCount];
                        for (int i = 0; i < hexCount; i++)
                        {
                            if (!TryParseHexColor(msg.GetString(3 + i), out colors[i]))
                            {
                                colors[i] = Color.cyan;
                            }
                        }
                        _bus.SetBatchAgentColors(colors);
                    }
                    else
                    {
                        int n = msg.ArgCount / 4;
                        if (n <= 0) break;
                        Color[] colors = new Color[n];
                        for (int i = 0; i < n; i++)
                        {
                            int baseIdx = i * 4;
                            colors[i] = new Color(
                                Mathf.Clamp01(msg.GetFloat(baseIdx)),
                                Mathf.Clamp01(msg.GetFloat(baseIdx + 1)),
                                Mathf.Clamp01(msg.GetFloat(baseIdx + 2)),
                                Mathf.Clamp01(msg.GetFloat(baseIdx + 3, 1f)));
                        }
                        _bus.SetBatchAgentColors(colors);
                    }
                    break;
                }

                case "/agents/radius/id":
                {
                    // HS-ims signature: id0 radius_mm0 alpha0 color_hex0 ...
                    for (int i = 0; i + 3 < msg.ArgCount; i += 4)
                    {
                        int id = msg.GetInt(i);
                        float radiusMm = msg.GetFloat(i + 1);
                        float alpha = Mathf.Clamp01(msg.GetFloat(i + 2, 0.2f));
                        string hex = msg.GetString(i + 3, "ffffff");
                        if (!TryParseHexColor(hex, out Color col))
                        {
                            col = Color.white;
                        }
                        float radiusMeters = _mapper.OscMillimeterLengthToUnityMeters(radiusMm);
                        _bus.SetAgentRadiusVisual(id, radiusMeters, alpha, col);
                    }
                    break;
                }

                case "/agents/Interactable":
                case "/agents/interactable":
                {
                    if (msg.ArgCount < 2) break;
                    int id = msg.GetInt(0);
                    bool interactable = msg.GetInt(1) != 0;
                    _bus.SetAgentInteractable(id, interactable);
                    break;
                }

                // =====================================================================
                // 4. Local Communication (/agents/localcomm & /agents/localcomm/send)
                // =====================================================================
                case "/agents/localcomm":
                {
                    if (msg.ArgCount == 3 && !(msg.Arguments[0] is string))
                    {
                        // HS-ims global configuration: /agents/localcomm <maxRange_mm> <latency_ms> <showFeedback>
                        float maxRangeMeters = _mapper.OscMillimeterLengthToUnityMeters(msg.GetFloat(0, 1000f));
                        float latencySec = Mathf.Max(0.005f, msg.GetFloat(1, 200f) * 0.001f);
                        bool showFeedback = msg.GetInt(2, 1) != 0;
                        _bus.ConfigureLocalComm(maxRangeMeters, latencySec, showFeedback);
                    }
                    else if (msg.ArgCount >= 4)
                    {
                        // SpatialMAS v2 per-broadcast wavefront: (msgType, senderId, rangeMm, latencySec, ...extra)
                        string msgType = msg.GetString(0, "msg");
                        int senderId = msg.GetInt(1);
                        float rangeMm = msg.GetFloat(2, 100f);
                        float latencySec = Mathf.Max(0.005f, msg.GetFloat(3, 0.1f));

                        int extraCount = msg.ArgCount - 4;
                        object[] extraArgs = extraCount > 0 ? new object[extraCount] : Array.Empty<object>();
                        for (int i = 0; i < extraCount; i++)
                        {
                            extraArgs[i] = msg.Arguments[4 + i];
                        }

                        float rangeMeters = _mapper.OscMillimeterLengthToUnityMeters(rangeMm);
                        _bus.TriggerLocalComm(msgType, senderId, rangeMeters, latencySec, extraArgs);
                    }
                    break;
                }

                case "/agents/localcomm/send":
                {
                    // HS-ims broadcast: /agents/localcomm/send <id0> <msg0> <id1> <msg1> ...
                    for (int i = 0; i + 1 < msg.ArgCount; i += 2)
                    {
                        int senderId = msg.GetInt(i);
                        string payloadStr = msg.GetString(i + 1);
                        _bus.TriggerLocalComm("hsims_send", senderId, -1f, -1f, new object[] { payloadStr });
                    }
                    break;
                }

                // =====================================================================
                // 5. Audio Synthesizer & Soundbank Endpoints
                // =====================================================================
                case "/agents/audio/mute":
                {
                    if (msg.ArgCount == 1)
                    {
                        bool muteAll = msg.GetInt(0) != 0;
                        _bus.SetAgentMute(-1, muteAll);
                    }
                    else if (msg.ArgCount >= 2)
                    {
                        int id = msg.GetInt(0);
                        bool mute = msg.GetInt(1) != 0;
                        _bus.SetAgentMute(id, mute);
                    }
                    break;
                }

                case "/agents/audio/synth/add":
                case "/agents/audio/synth/add/id":
                {
                    // NativeDspSynthesizer is already pre-attached to every pooled AgentEntity; ensure unmuted
                    _bus.SetAgentMute(-1, false);
                    break;
                }

                case "/agents/audio/synth/remove":
                case "/agents/audio/synth/remove/id":
                case "/agents/audio/synth/flush":
                {
                    _bus.FlushAgentSynth(-1);
                    break;
                }

                case "/agents/audio/synth/note":
                {
                    if (msg.ArgCount < 3) break;
                    int id = msg.GetInt(0);
                    int midiNote;
                    int velocity;
                    int noteAction;

                    if (msg.ArgCount >= 4)
                    {
                        // Supports both HS-ims (<id> <bank=0> <note> <velocity>) and v2 (<id> <action=1|0> <note> <velocity>)
                        midiNote = msg.GetInt(2, 60);
                        velocity = msg.GetInt(3, 100);
                        noteAction = velocity > 0 ? 1 : 0;
                    }
                    else
                    {
                        midiNote = msg.GetInt(1, 60);
                        velocity = msg.GetInt(2, 100);
                        noteAction = velocity > 0 ? 1 : 0;
                    }
                    _bus.SetAgentSynthNote(id, noteAction, midiNote, velocity);
                    break;
                }

                case "/agents/audio/synth/param_val":
                {
                    if (msg.ArgCount < 3) break;
                    int id = msg.GetInt(0);
                    int ccId;
                    float val;

                    if (msg.ArgCount >= 4)
                    {
                        // HS-ims 4-arg format: <agent_id> <bank> <cc_id> <value>
                        ccId = msg.GetInt(2);
                        val = msg.GetFloat(3);
                    }
                    else
                    {
                        // 3-arg format: <agent_id> <cc_id> <value>
                        ccId = msg.GetInt(1);
                        val = msg.GetFloat(2);
                    }
                    _bus.SetAgentSynthParam(id, ccId, val);
                    break;
                }

                case "/agents/audio/synth/param_midi":
                {
                    if (msg.ArgCount < 3) break;
                    int id = msg.GetInt(0);
                    int ccId = msg.ArgCount >= 4 ? msg.GetInt(2) : msg.GetInt(1);
                    float midi01 = Mathf.Clamp01((msg.ArgCount >= 4 ? msg.GetFloat(3) : msg.GetFloat(2)) / 127.0f);

                    float mappedVal = midi01;
                    if (ccId == 0 || ccId == 1) mappedVal = Mathf.Round(midi01 * 3f);
                    else if (ccId == 2) mappedVal = Mathf.Lerp(55f, 4000f, midi01);
                    else if (ccId == 93) mappedVal = Mathf.Lerp(100f, 16000f, midi01);
                    else if (ccId == 18) mappedVal = Mathf.Lerp(0.5f, 10.0f, midi01);

                    _bus.SetAgentSynthParam(id, ccId, mappedVal);
                    break;
                }

                case "/agents/audio/bank":
                {
                    if (msg.ArgCount < 4) break;
                    int id = msg.GetInt(0);
                    int clipIdx = msg.GetInt(1);
                    int playAction = msg.GetInt(2);
                    float gain = msg.GetFloat(3, 0.5f);
                    _bus.SetAgentAudioBank(id, clipIdx, playAction, gain);
                    break;
                }

                // =====================================================================
                // 6. Boundaries, Listener, Arrows & Recorder (v1 HS-ims + v2 SpatialMAS)
                // =====================================================================
                case "/boundaries/add":
                case "/boundary/add/id":
                {
                    if (msg.ArgCount < 2) break;
                    int id = msg.GetInt(0);
                    var shape = (AgentShapeType)Mathf.Clamp(msg.GetInt(1), 0, 1);
                    Vector3 posUnity = msg.ArgCount >= 5
                        ? _mapper.OscMillimetersToUnityMeters(msg.GetFloat(2), msg.GetFloat(3), msg.GetFloat(4))
                        : _mapper.OriginOffset;
                    float sizeMm = msg.ArgCount >= 6 ? msg.GetFloat(5, 2500f) : 2500f;
                    float sizeMeters = _mapper.OscMillimeterLengthToUnityMeters(sizeMm);
                    _bus.AddBoundaryUnity(id, shape, posUnity, sizeMeters);
                    break;
                }

                case "/boundaries/update":
                {
                    if (msg.ArgCount < 5) break;
                    int id = msg.GetInt(0);
                    Vector3 posUnity = _mapper.OscMillimetersToUnityMeters(msg.GetFloat(1), msg.GetFloat(2), msg.GetFloat(3));
                    float sizeMeters = _mapper.OscMillimeterLengthToUnityMeters(msg.GetFloat(4, 1000f));
                    _bus.UpdateBoundaryUnity(id, posUnity, sizeMeters);
                    break;
                }

                case "/boundary/position/id":
                {
                    if (msg.ArgCount < 4) break;
                    int id = msg.GetInt(0);
                    Vector3 posUnity = _mapper.OscMillimetersToUnityMeters(msg.GetFloat(1), msg.GetFloat(2), msg.GetFloat(3));
                    _bus.UpdateBoundaryUnity(id, posUnity, -1f);
                    break;
                }

                case "/boundary/scale/id":
                {
                    if (msg.ArgCount < 2) break;
                    int id = msg.GetInt(0);
                    float sizeMeters = _mapper.OscMillimeterLengthToUnityMeters(msg.GetFloat(1, 2500f) * 0.5f);
                    _bus.UpdateBoundaryUnity(id, new Vector3(float.NaN, float.NaN, float.NaN), sizeMeters);
                    break;
                }

                case "/boundary/alpha/id":
                {
                    if (msg.ArgCount < 2) break;
                    int id = msg.GetInt(0);
                    float alpha = Mathf.Clamp01(msg.GetFloat(1, 0.14f));
                    _bus.SetBoundaryVisuals(id, new Color(-1f, -1f, -1f, alpha), alpha);
                    break;
                }

                case "/boundary/color/id":
                {
                    if (msg.ArgCount < 2) break;
                    int id = msg.GetInt(0);
                    if (TryParseHexColor(msg.GetString(1), out Color col))
                    {
                        _bus.SetBoundaryVisuals(id, col, -1f);
                    }
                    break;
                }

                case "/boundaries/remove":
                case "/boundary/remove/id":
                {
                    if (msg.ArgCount == 0) _bus.RemoveBoundary(-1);
                    else _bus.RemoveBoundary(msg.GetInt(0));
                    break;
                }

                case "/listener/position":
                case "/audio/listener/position":
                {
                    if (msg.ArgCount < 3) break;
                    Vector3 posUnity = _mapper.OscMillimetersToUnityMeters(msg.GetFloat(0), msg.GetFloat(1), msg.GetFloat(2));
                    _bus.SetListenerPositionUnity(posUnity);
                    break;
                }

                case "/listener/rotation":
                {
                    if (msg.ArgCount < 3) break;
                    Quaternion rotUnity = _mapper.OscEulerToUnityRotation(msg.GetFloat(0), msg.GetFloat(1), msg.GetFloat(2));
                    _bus.SetListenerRotationUnity(rotUnity);
                    break;
                }

                case "/environment/set_arrow":
                {
                    if (msg.ArgCount < 8) break;
                    int id = msg.GetInt(0);
                    Vector3 startUnity = _mapper.OscMillimetersToUnityMeters(msg.GetFloat(1), msg.GetFloat(2), msg.GetFloat(3));
                    Vector3 endUnity = _mapper.OscMillimetersToUnityMeters(msg.GetFloat(4), msg.GetFloat(5), msg.GetFloat(6));
                    float widthMeters = _mapper.OscMillimeterLengthToUnityMeters(msg.GetFloat(7, 20f));
                    _bus.SetArrowUnity(id, startUnity, endUnity, widthMeters);
                    break;
                }

                case "/arrow":
                {
                    // HS-ims signature: <x> <y> <z> <dx> <dy> <dz> <scale>
                    if (msg.ArgCount < 6) break;
                    Vector3 startUnity = _mapper.OscMillimetersToUnityMeters(msg.GetFloat(0), msg.GetFloat(1), msg.GetFloat(2));
                    Vector3 dirOsc = new Vector3(msg.GetFloat(3), msg.GetFloat(4), msg.GetFloat(5)).normalized;
                    float scaleMm = msg.ArgCount >= 7 ? msg.GetFloat(6, 500f) : 500f;
                    Vector3 endUnity = _mapper.OscMillimetersToUnityMeters(
                        msg.GetFloat(0) + dirOsc.x * scaleMm,
                        msg.GetFloat(1) + dirOsc.y * scaleMm,
                        msg.GetFloat(2) + dirOsc.z * scaleMm);
                    float widthMeters = _mapper.OscMillimeterLengthToUnityMeters(scaleMm * 0.08f);
                    _bus.SetArrowUnity(0, startUnity, endUnity, widthMeters);
                    break;
                }

                case "/environment/remove_arrow":
                case "/arrow/remove":
                {
                    if (msg.ArgCount == 0) _bus.RemoveArrow(-1);
                    else _bus.RemoveArrow(msg.GetInt(0));
                    break;
                }

                case "/recorder/start":
                {
                    _bus.SetRecordingState(true);
                    break;
                }

                case "/recorder/stop":
                {
                    _bus.SetRecordingState(false);
                    break;
                }

                // =====================================================================
                // 7. v2 SpatialMAS XR & Scenario Control Endpoints
                // =====================================================================
                case "/xr/mode":
                {
                    if (msg.ArgCount < 1) break;
                    var mode = msg.GetInt(0) == 0 ? XRDisplayMode.MRPassthrough : XRDisplayMode.VRImmersive;
                    _bus.SetXRDisplayMode(mode);
                    break;
                }

                case "/xr/scale":
                {
                    if (msg.ArgCount < 1) break;
                    float scale = Mathf.Clamp(msg.GetFloat(0, 1.0f), 0.05f, 5.0f);
                    _bus.SetWorldScale(scale);
                    break;
                }

                case "/xr/origin":
                {
                    if (msg.ArgCount < 3) break;
                    Vector3 offsetMeters = new Vector3(msg.GetFloat(0), msg.GetFloat(1), msg.GetFloat(2));
                    _bus.SetOriginOffset(offsetMeters);
                    break;
                }

                case "/xr/listener/lock_to_head":
                {
                    if (msg.ArgCount < 1) break;
                    _bus.SetListenerLockedToHead(msg.GetInt(0) != 0);
                    break;
                }

                case "/xr/user/repeller":
                {
                    if (msg.ArgCount < 1) break;
                    bool enabled = msg.GetInt(0) != 0;
                    float radius = msg.ArgCount >= 2 ? msg.GetFloat(1, 0.65f) : 0.65f;
                    float force = msg.ArgCount >= 3 ? msg.GetFloat(2, 1.5f) : 1.5f;
                    _bus.ConfigureUserRepeller(enabled, radius, force);
                    break;
                }

                case "/scenario/load":
                {
                    if (msg.ArgCount < 1) break;
                    _bus.LoadScenario(msg.GetInt(0));
                    break;
                }

                case "/scenario/param":
                {
                    if (msg.ArgCount < 2) break;
                    string paramName = msg.GetString(0);
                    float val = msg.GetFloat(1);
                    _bus.SetScenarioParam(paramName, val);
                    break;
                }

                case "/logging/state":
                {
                    if (msg.ArgCount < 1) break;
                    _bus.SetRecordingState(msg.GetInt(0) != 0);
                    break;
                }
            }
        }

        public static bool TryParseHexColor(string hex, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrWhiteSpace(hex)) return false;

            string clean = hex.Trim();
            if (clean.StartsWith("#")) clean = clean.Substring(1);
            if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(2);

            if (clean.Length == 6 &&
                uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            {
                float r = ((rgb >> 16) & 0xFF) / 255f;
                float g = ((rgb >> 8) & 0xFF) / 255f;
                float b = (rgb & 0xFF) / 255f;
                color = new Color(r, g, b, 1.0f);
                return true;
            }
            if (clean.Length == 8 &&
                uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgba))
            {
                float r = ((rgba >> 24) & 0xFF) / 255f;
                float g = ((rgba >> 16) & 0xFF) / 255f;
                float b = ((rgba >> 8) & 0xFF) / 255f;
                float a = (rgba & 0xFF) / 255f;
                color = new Color(r, g, b, a);
                return true;
            }
            return false;
        }

        // =====================================================================
        // Outgoing OSC Feedback Handlers (SpatialMAS -> External Controller)
        // =====================================================================
        private void HandleAgentMovedByUser(int id, Vector3 posUnity)
        {
            Vector3 oscMm = _mapper.UnityMetersToOscMillimeters(posUnity);
            SendAndLog(new OscMessage("/agents/position/id", id, oscMm.x, oscMm.y, oscMm.z));
        }

        private void HandleAgentTouch(int id, int touchState, int otherAgentId)
        {
            SendAndLog(new OscMessage("/agents/touch/id", id, touchState, otherAgentId));
        }

        private void HandleAgentControlClick(int id, int controlType, int controlValue)
        {
            // HS-ims mouse/hand click feedback (/agents/control <id> <0=left,1=right> <1=press,0=release>)
            SendAndLog(new OscMessage("/agents/control", id, controlType, controlValue));
        }

        private void HandleLocalCommDelivered(string msgType, int senderId, int receiverId, float distanceMeters, object[] extraArgs)
        {
            float distMm = _mapper.UnityMeterLengthToOscMillimeters(distanceMeters);
            int extraLen = extraArgs != null ? extraArgs.Length : 0;
            object[] payload = new object[4 + extraLen];
            payload[0] = msgType;
            payload[1] = senderId;
            payload[2] = receiverId;
            payload[3] = distMm;
            for (int i = 0; i < extraLen; i++) payload[4 + i] = extraArgs[i];

            SendAndLog(new OscMessage("/agents/localcomm/id", payload));

            // Also send HS-ims /agents/localcomm/recv <receiverId> <senderId> <msg> when string payload is present
            if (extraLen > 0)
            {
                SendAndLog(new OscMessage("/agents/localcomm/recv", receiverId, senderId, Convert.ToString(extraArgs[0], CultureInfo.InvariantCulture)));
            }
        }

        private void HandleBoundaryTouched(int agentId, int boundaryId)
        {
            SendAndLog(new OscMessage("/boundaries/Touched", agentId, boundaryId));
        }

        private void HandleBoundaryTransition(int agentId, int boundaryId, int transitionType)
        {
            // HS-ims boundary transition feedback (/boundary/event/id <agentId> <boundaryId> <0=enter,1=exit>)
            SendAndLog(new OscMessage("/boundary/event/id", agentId, boundaryId, transitionType));
        }

        private void HandleUserHeadPoseSampled(Vector3 posUnity, Quaternion rotUnity)
        {
            if (!StreamUserPoseEnabled) return;
            Vector3 oscMm = _mapper.UnityMetersToOscMillimeters(posUnity);
            SendAndLog(new OscMessage("/xr/user/pose", oscMm.x, oscMm.y, oscMm.z, rotUnity.x, rotUnity.y, rotUnity.z, rotUnity.w));
        }

        private void HandleUserHandPoseSampled(int handIndex, Vector3 posUnity, bool isPinching)
        {
            if (!StreamUserPoseEnabled) return;
            Vector3 oscMm = _mapper.UnityMetersToOscMillimeters(posUnity);
            SendAndLog(new OscMessage("/xr/hands/pose", handIndex, oscMm.x, oscMm.y, oscMm.z, isPinching ? 1 : 0));
        }

        private void SendAndLog(OscMessage msg)
        {
            _client.Send(msg);
            _bus.LogOscTraffic("OUT", msg.Address, msg.FormatArgumentsCsv());
        }

        public void Dispose()
        {
            _bus.OnAgentMovedByUserUnity -= HandleAgentMovedByUser;
            _bus.OnAgentTouchEvent -= HandleAgentTouch;
            _bus.OnAgentControlClickEvent -= HandleAgentControlClick;
            _bus.OnLocalCommDeliveredUnity -= HandleLocalCommDelivered;
            _bus.OnBoundaryTouchedEvent -= HandleBoundaryTouched;
            _bus.OnBoundaryTransitionEvent -= HandleBoundaryTransition;
            _bus.OnUserHeadPoseSampledUnity -= HandleUserHeadPoseSampled;
            _bus.OnUserHandPoseSampledUnity -= HandleUserHandPoseSampled;
        }
    }
}
