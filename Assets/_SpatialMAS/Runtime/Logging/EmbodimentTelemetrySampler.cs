using System;
using System.Collections.Generic;
using SpatialMAS.Agents;
using SpatialMAS.Core;
using SpatialMAS.Environment;
using SpatialMAS.Scenarios;
using SpatialMAS.XR;
using UnityEngine;
using UnityEngine.XR;

namespace SpatialMAS.Logging
{
    /// <summary>Actual world poses after simulation updates; one recorder timestamp for each complete sample.</summary>
    [DefaultExecutionOrder(10000)]
    public class EmbodimentTelemetrySampler : MonoBehaviour
    {
        [SerializeField] private float _sampleRateHz = 50f;
        private DataRecordingService _recorder;
        private XRUserEmbodimentBridge _embodiment;
        private XRModeSwitcher _mode;
        private ScenarioManager _scenarios;
        private AgentManager _agents;
        private SpatialCalibrationReference _reference;
        private XRPlatformAdapter _platform;
        private Camera _camera;
        private BoundaryManager _boundaries;
        private EnvironmentArrowManager _arrows;
        private readonly List<ObjectTelemetryRow> _objects = new List<ObjectTelemetryRow>(128);
        private readonly List<XRInputSubsystem> _inputSubsystems = new List<XRInputSubsystem>();
        private readonly HashSet<XRInputSubsystem> _subscribedInputs = new HashSet<XRInputSubsystem>();
        private string _lastScenarioState;
        private string _lastSessionId;
        private bool _trackingOriginUpdated;
        private float _sampleTimer, _flushTimer;
        private bool _wasRecording, _hasTrackingFrame;
        private int _trackingRevision;
        private Vector3 _trackingPosition, _trackingScale = Vector3.one;
        private Quaternion _trackingRotation = Quaternion.identity;

        [Serializable] private class ScenarioState
        {
            public int scenarioId;
            public string scenarioName, activeLayout, nextLayout;
            public Vector3 activeCenter_m, nextCenter_m;
            public bool paused;
        }
        [Serializable] private class ParameterChange { public string name; public float value; }
        [Serializable] private class PresetChange { public int presetIndex; }
        [Serializable] private class TrackingFrame
        {
            public int revision;
            public Vector3 position_m, scale;
            public Quaternion rotation_xyzw;
            public bool coordinatesValid;
            public string reason;
        }

        public void Initialize(DataRecordingService recordingService, XRUserEmbodimentBridge embodiment,
            XRModeSwitcher modeSwitcher, ScenarioManager scenarioManager, AgentManager agentManager,
            SpatialCalibrationReference calibrationReference, float sampleRateHz = 50f,
            XRPlatformAdapter platform = null, Camera camera = null,
            BoundaryManager boundaries = null, EnvironmentArrowManager arrows = null)
        {
            Unsubscribe();
            _recorder = recordingService; _embodiment = embodiment; _mode = modeSwitcher;
            _scenarios = scenarioManager; _agents = agentManager; _reference = calibrationReference;
            _platform = platform; _camera = camera != null ? camera : Camera.main;
            _boundaries = boundaries; _arrows = arrows;
            _sampleRateHz = Mathf.Clamp(sampleRateHz, 5f, 120f);
            if (_scenarios != null)
            {
                _scenarios.OnScenarioChanged += HandleScenarioChanged;
                _scenarios.ParameterApplied += HandleParameter;
                _scenarios.PresetApplied += HandlePreset;
            }
        }

        private void Unsubscribe()
        {
            if (_scenarios == null) return;
            _scenarios.OnScenarioChanged -= HandleScenarioChanged;
            _scenarios.ParameterApplied -= HandleParameter;
            _scenarios.PresetApplied -= HandlePreset;
        }
        private void OnDestroy()
        {
            Unsubscribe();
            foreach (var input in _subscribedInputs) input.trackingOriginUpdated -= HandleTrackingOriginUpdated;
        }
        private void HandleTrackingOriginUpdated(XRInputSubsystem _) => _trackingOriginUpdated = true;
        private void HandleScenarioChanged(IScenarioModule _) => LogScenarioState();
        private void HandleParameter(string name, float value)
            => _recorder?.LogEvent("scenario_parameter", JsonUtility.ToJson(new ParameterChange { name = name, value = value }), Time.frameCount);
        private void HandlePreset(int preset)
            => _recorder?.LogEvent("scenario_preset", JsonUtility.ToJson(new PresetChange { presetIndex = preset }), Time.frameCount);

        private void LogScenarioState()
        {
            if (_scenarios == null) return;
            string state = JsonUtility.ToJson(new ScenarioState
            {
                scenarioId = _scenarios.ActiveScenarioId, scenarioName = _scenarios.ActiveScenarioName,
                activeLayout = _scenarios.ActiveLayout.ToString(), nextLayout = _scenarios.NextLayout.ToString(),
                activeCenter_m = _scenarios.ActiveScenarioCenter, nextCenter_m = _scenarios.NextScenarioCenter,
                paused = _scenarios.IsPaused
            });
            if (_recorder != null && _recorder.IsRecording && state != _lastScenarioState)
            {
                _recorder.LogEvent("scenario_state", state, Time.frameCount);
                _lastScenarioState = state;
            }
        }

        private void LateUpdate()
        {
            SubsystemManager.GetSubsystems(_inputSubsystems);
            foreach (var input in _inputSubsystems)
                if (_subscribedInputs.Add(input)) input.trackingOriginUpdated += HandleTrackingOriginUpdated;
            if (_recorder == null || !_recorder.IsRecording)
            {
                _wasRecording = false; _sampleTimer = 0f; return;
            }
            bool firstSample = !_wasRecording || _lastSessionId != _recorder.SessionId;
            if (firstSample)
            {
                _lastSessionId = _recorder.SessionId;
                _lastScenarioState = null;
                _hasTrackingFrame = false;
                _sampleTimer = 0f;
                _flushTimer = 0f;
            }
            _wasRecording = true;
            float dt = Time.unscaledDeltaTime;
            _sampleTimer += dt; _flushTimer += dt;
            float interval = 1f / _sampleRateHz;
            if (firstSample || _sampleTimer >= interval)
            {
                // Never manufacture duplicate catch-up observations at a low rendering frame rate.
                _sampleTimer %= interval;
                CaptureSample(dt);
            }
            if (_flushTimer >= 2f) { _flushTimer = 0f; _recorder.Flush(); }
        }

        private void CaptureSample(float dt)
        {
            LogScenarioState();
            if (_camera == null) _camera = Camera.main;
            Transform origin = XRInteractionRigSetup.GetTrackingOrigin(_camera);
            Vector3 originPos = origin != null ? origin.position : Vector3.zero;
            Quaternion originRot = origin != null ? origin.rotation : Quaternion.identity;
            Vector3 originScale = origin != null ? origin.lossyScale : Vector3.one;
            bool unitScale = (originScale - Vector3.one).sqrMagnitude < 0.000001f &&
                (_camera == null || (_camera.transform.lossyScale - Vector3.one).sqrMagnitude < 0.000001f);
            if (!_hasTrackingFrame || _trackingOriginUpdated || Vector3.Distance(originPos, _trackingPosition) > 0.000001f ||
                Quaternion.Angle(originRot, _trackingRotation) > 0.0001f ||
                (originScale - _trackingScale).sqrMagnitude > 0.00000001f)
            {
                string reason = !_hasTrackingFrame ? "initial" : _trackingOriginUpdated ? "tracking_origin_updated" : "rig_transform_changed";
                _trackingOriginUpdated = false;
                _hasTrackingFrame = true; _trackingPosition = originPos;
                _trackingRotation = originRot; _trackingScale = originScale; _trackingRevision++;
                _recorder.LogEvent("tracking_frame", JsonUtility.ToJson(new TrackingFrame
                {
                    revision = _trackingRevision, position_m = originPos, rotation_xyzw = originRot,
                    scale = originScale, coordinatesValid = unitScale, reason = reason
                }), Time.frameCount);
            }
            bool xr = _platform != null && _platform.IsXRHeadsetActive;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool headTracked = xr && head.isValid && head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
            CalibrationSnapshot reference = _reference != null ? _reference.Snapshot : default;
            var row = new EmbodimentTelemetryRow
            {
                Frame = Time.frameCount, ScenarioName = _scenarios != null ? _scenarios.ActiveScenarioName : "External",
                Layout = _scenarios != null ? _scenarios.ActiveLayout.ToString() : "External",
                ScenarioCenter = _scenarios != null ? _scenarios.ActiveScenarioCenter : Vector3.zero,
                XRMode = _mode != null ? _mode.CurrentMode.ToString() : "Unknown",
                ActiveAgents = _agents != null ? _agents.ActiveCount : 0, FrameTimeMs = dt * 1000f,
                HeadPos = _camera != null ? _camera.transform.position : _embodiment != null ? _embodiment.HeadPositionWorld : Vector3.zero,
                HeadRot = _camera != null ? _camera.transform.rotation : _embodiment != null ? _embodiment.HeadRotationWorld : Quaternion.identity,
                HeadTracked = headTracked, HeadSource = xr ? (headTracked ? "XRTracked" : "XRUntracked") : "DesktopCamera",
                LeftHandPos = _embodiment != null ? _embodiment.LeftHandPositionWorld : Vector3.zero,
                LeftHandRot = _embodiment != null ? _embodiment.LeftHandRotationWorld : Quaternion.identity,
                LeftHandTracked = _embodiment != null && _embodiment.IsLeftHandTracked,
                LeftPinch = _embodiment != null && _embodiment.IsLeftHandPinching,
                RightHandPos = _embodiment != null ? _embodiment.RightHandPositionWorld : Vector3.zero,
                RightHandRot = _embodiment != null ? _embodiment.RightHandRotationWorld : Quaternion.identity,
                RightHandTracked = _embodiment != null && _embodiment.IsRightHandTracked,
                RightPinch = _embodiment != null && _embodiment.IsRightHandPinching,
                CalibrationId = reference.CalibrationId, CalibrationRevision = reference.Revision,
                TrackingRevision = _trackingRevision, TrackingOriginPos = originPos, TrackingOriginRot = originRot,
                TrackingOriginScale = originScale, CoordinatesValid = unitScale,
                UserRepellerEnabled = _embodiment != null && _embodiment.UserRepellerEnabled,
                UserRepellerRadiusMeters = _embodiment != null ? _embodiment.UserRepellerRadiusMeters : 0f,
                UserRepellerStrength = _embodiment != null ? _embodiment.UserRepellerForce : 0f
            };
            _objects.Clear();
            if (_agents != null)
                foreach (var pair in _agents.ActiveAgents)
                {
                    AgentEntity agent = pair.Value;
                    if (agent == null) continue;
                    _objects.Add(new ObjectTelemetryRow
                    {
                        Kind = "Agent", Id = pair.Key, Shape = agent.ShapeType.ToString(),
                        Position = agent.transform.position, Rotation = agent.transform.rotation,
                        Dimensions = GetDimensions(agent.gameObject), MovementMode = agent.MovementMode.ToString(),
                        Grabbed = agent.IsGrabbedByUser, Interactable = agent.IsInteractable
                    });
                }
            if (_boundaries != null)
                foreach (var pair in _boundaries.ActiveBoundaries)
                {
                    BoundaryInstance boundary = pair.Value;
                    _objects.Add(new ObjectTelemetryRow
                    {
                        Kind = "Boundary", Id = pair.Key, Shape = boundary.Shape.ToString(),
                        Position = boundary.CenterUnity, Rotation = boundary.RotationUnity,
                        Dimensions = boundary.Shape == AgentShapeType.Cube ? boundary.HalfExtentsMeters * 2f : Vector3.one * boundary.SizeMeters * 2f
                    });
                }
            if (_arrows != null)
                foreach (var pair in _arrows.ActiveArrows)
                {
                    GameObject arrow = pair.Value;
                    if (arrow == null || !arrow.activeInHierarchy) continue;
                    _objects.Add(new ObjectTelemetryRow
                    {
                        Kind = "Arrow", Id = pair.Key, Shape = "Arrow", Position = arrow.transform.position,
                        Rotation = arrow.transform.rotation, Dimensions = GetDimensions(arrow)
                    });
                }
            _recorder.LogWorldSample(in row, _objects);
        }

        private static Vector3 GetDimensions(GameObject go)
        {
            Vector3 scale = go.transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            return go.TryGetComponent<MeshFilter>(out var mesh) && mesh.sharedMesh != null
                ? Vector3.Scale(mesh.sharedMesh.bounds.size, scale) : scale;
        }
    }
}
