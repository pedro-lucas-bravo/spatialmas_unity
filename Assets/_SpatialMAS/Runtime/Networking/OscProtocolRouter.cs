using System;
using System.Collections.Generic;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Networking
{
    /// <summary>
    /// Translates all incoming OSC v1 (HS-ims backward-compatible) and v2 (SpatialMAS XR & Scenario)
    /// messages into SimulationCommandBus commands, and forwards outgoing world events back over UDP.
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
            _bus.OnLocalCommDeliveredUnity += HandleLocalCommDelivered;
            _bus.OnBoundaryTouchedEvent += HandleBoundaryTouched;
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
                // v1 HS-ims Backward-Compatible Endpoints
                // =====================================================================
                case "/test/alive":
                {
                    string ip = msg.GetString(0, _client.RemoteIp);
                    int port = msg.GetInt(1, _client.RemotePort);
                    float testVal = msg.GetFloat(2, 0f);
                    _client.SetDestination(ip, port);
                    _bus.NotifyTestAlive(ip, port, testVal);
                    break;
                }

                case "/agents/instantiate":
                {
                    if (msg.ArgCount < 6) break;
                    var shape = (AgentShapeType)Mathf.Clamp(msg.GetInt(0), 0, 3);
                    var movMode = (AgentMovementMode)Mathf.Clamp(msg.GetInt(1), 0, 1);
                    int nAgents = Mathf.Max(0, msg.GetInt(2));
                    float rawScale = Mathf.Max(0.005f, msg.GetFloat(3, 0.1f));
                    string remoteIp = msg.GetString(4, _client.RemoteIp);
                    int remotePort = msg.GetInt(5, _client.RemotePort);

                    _client.SetDestination(remoteIp, remotePort);
                    float scaledRadius = _mapper.ScaleAgentMeterRadius(rawScale);
                    _bus.InstantiateAgents(shape, movMode, nAgents, scaledRadius, remoteIp, remotePort);
                    break;
                }

                case "/agents/add":
                {
                    if (msg.ArgCount < 6) break;
                    var shape = (AgentShapeType)Mathf.Clamp(msg.GetInt(0), 0, 3);
                    var movMode = (AgentMovementMode)Mathf.Clamp(msg.GetInt(1), 0, 1);
                    float rawScale = Mathf.Max(0.005f, msg.GetFloat(2, 0.1f));
                    string remoteIp = msg.GetString(3, _client.RemoteIp);
                    int remotePort = msg.GetInt(4, _client.RemotePort);

                    _client.SetDestination(remoteIp, remotePort);
                    int idCount = msg.ArgCount - 5;
                    int[] ids = new int[idCount];
                    for (int i = 0; i < idCount; i++)
                    {
                        ids[i] = msg.GetInt(5 + i);
                    }
                    float scaledRadius = _mapper.ScaleAgentMeterRadius(rawScale);
                    _bus.AddAgents(shape, movMode, scaledRadius, remoteIp, remotePort, ids);
                    break;
                }

                case "/agents/remove":
                {
                    if (msg.ArgCount == 0)
                    {
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

                case "/agents/position/id":
                {
                    if (msg.ArgCount < 4) break;
                    int id = msg.GetInt(0);
                    float xMm = msg.GetFloat(1);
                    float yMm = msg.GetFloat(2);
                    float zMm = msg.GetFloat(3);
                    Vector3 posUnity = _mapper.OscMillimetersToUnityMeters(xMm, yMm, zMm);
                    _bus.SetAgentPositionUnity(id, posUnity);
                    break;
                }

                case "/agents/position":
                {
                    int n = msg.ArgCount / 3;
                    if (n <= 0) break;
                    Vector3[] positions = new Vector3[n];
                    for (int i = 0; i < n; i++)
                    {
                        int baseIdx = i * 3;
                        positions[i] = _mapper.OscMillimetersToUnityMeters(
                            msg.GetFloat(baseIdx),
                            msg.GetFloat(baseIdx + 1),
                            msg.GetFloat(baseIdx + 2));
                    }
                    _bus.SetBatchAgentPositionsUnity(positions);
                    break;
                }

                case "/agents/color/id":
                {
                    if (msg.ArgCount < 5) break;
                    int id = msg.GetInt(0);
                    Color c = new Color(
                        Mathf.Clamp01(msg.GetFloat(1)),
                        Mathf.Clamp01(msg.GetFloat(2)),
                        Mathf.Clamp01(msg.GetFloat(3)),
                        Mathf.Clamp01(msg.GetFloat(4, 1f)));
                    _bus.SetAgentColor(id, c);
                    break;
                }

                case "/agents/color":
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

                case "/agents/localcomm":
                {
                    if (msg.ArgCount < 4) break;
                    string msgType = msg.GetString(0, "msg");
                    int senderId = msg.GetInt(1);
                    float rangeMm = msg.GetFloat(2, 100f);
                    float latencySec = Mathf.Max(0.001f, msg.GetFloat(3, 0.1f));

                    int extraCount = msg.ArgCount - 4;
                    object[] extraArgs = extraCount > 0 ? new object[extraCount] : Array.Empty<object>();
                    for (int i = 0; i < extraCount; i++)
                    {
                        extraArgs[i] = msg.Arguments[4 + i];
                    }

                    float rangeMeters = _mapper.OscMillimeterLengthToUnityMeters(rangeMm);
                    _bus.TriggerLocalComm(msgType, senderId, rangeMeters, latencySec, extraArgs);
                    break;
                }

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

                case "/agents/audio/synth/note":
                {
                    if (msg.ArgCount < 4) break;
                    int id = msg.GetInt(0);
                    int noteAction = msg.GetInt(1);
                    int midiNote = msg.GetInt(2, 60);
                    int velocity = msg.GetInt(3, 100);
                    _bus.SetAgentSynthNote(id, noteAction, midiNote, velocity);
                    break;
                }

                case "/agents/audio/synth/param_val":
                {
                    if (msg.ArgCount < 3) break;
                    int id = msg.GetInt(0);
                    int ccId = msg.GetInt(1);
                    float val = msg.GetFloat(2);
                    _bus.SetAgentSynthParam(id, ccId, val);
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

                case "/boundaries/add":
                {
                    if (msg.ArgCount < 6) break;
                    int id = msg.GetInt(0);
                    var shape = (AgentShapeType)Mathf.Clamp(msg.GetInt(1), 0, 1);
                    Vector3 posUnity = _mapper.OscMillimetersToUnityMeters(msg.GetFloat(2), msg.GetFloat(3), msg.GetFloat(4));
                    float sizeMeters = _mapper.OscMillimeterLengthToUnityMeters(msg.GetFloat(5, 1000f));
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

                case "/boundaries/remove":
                {
                    if (msg.ArgCount == 0) _bus.RemoveBoundary(-1);
                    else _bus.RemoveBoundary(msg.GetInt(0));
                    break;
                }

                case "/listener/position":
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

                case "/environment/remove_arrow":
                {
                    if (msg.ArgCount == 0) _bus.RemoveArrow(-1);
                    else _bus.RemoveArrow(msg.GetInt(0));
                    break;
                }

                // =====================================================================
                // v2 SpatialMAS XR & Scenario Control Endpoints
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
        }

        private void HandleBoundaryTouched(int agentId, int boundaryId)
        {
            SendAndLog(new OscMessage("/boundaries/Touched", agentId, boundaryId));
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
            _bus.OnLocalCommDeliveredUnity -= HandleLocalCommDelivered;
            _bus.OnBoundaryTouchedEvent -= HandleBoundaryTouched;
            _bus.OnUserHeadPoseSampledUnity -= HandleUserHeadPoseSampled;
            _bus.OnUserHandPoseSampledUnity -= HandleUserHandPoseSampled;
        }
    }
}
