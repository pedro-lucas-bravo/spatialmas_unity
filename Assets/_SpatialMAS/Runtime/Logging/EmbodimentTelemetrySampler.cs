using SpatialMAS.Agents;
using SpatialMAS.Core;
using SpatialMAS.Scenarios;
using SpatialMAS.XR;
using UnityEngine;

namespace SpatialMAS.Logging
{
    /// <summary>
    /// Samples XR Embodiment Telemetry (Head 6DoF, Left/Right Hand 6DoF, Pinch events,
    /// Active Scenario, WorldScale, ActiveAgents, and Frametime ms) at a configurable rate (default 50 Hz).
    /// </summary>
    public class EmbodimentTelemetrySampler : MonoBehaviour
    {
        [SerializeField] private float _sampleRateHz = 50.0f;

        private DataRecordingService _recordingService;
        private XRUserEmbodimentBridge _embodiment;
        private XRModeSwitcher _modeSwitcher;
        private ScenarioManager _scenarioManager;
        private AgentManager _agentManager;
        private SpatialCoordinateMapper _mapper;

        private float _sampleTimer;
        private float _flushTimer;

        public void Initialize(
            DataRecordingService recordingService,
            XRUserEmbodimentBridge embodiment,
            XRModeSwitcher modeSwitcher,
            ScenarioManager scenarioManager,
            AgentManager agentManager,
            SpatialCoordinateMapper mapper,
            float sampleRateHz = 50.0f)
        {
            _recordingService = recordingService;
            _embodiment = embodiment;
            _modeSwitcher = modeSwitcher;
            _scenarioManager = scenarioManager;
            _agentManager = agentManager;
            _mapper = mapper;
            _sampleRateHz = Mathf.Clamp(sampleRateHz, 5f, 120f);
        }

        private void Update()
        {
            if (_recordingService == null || !_recordingService.IsRecording)
                return;

            float dt = Time.unscaledDeltaTime;
            _sampleTimer += dt;
            _flushTimer += dt;

            float interval = 1.0f / Mathf.Max(1.0f, _sampleRateHz);
            if (_sampleTimer >= interval)
            {
                _sampleTimer -= interval;
                var row = new EmbodimentTelemetryRow
                {
                    Frame = Time.frameCount,
                    ScenarioName = _scenarioManager != null ? _scenarioManager.ActiveScenarioName : "External",
                    XRMode = _modeSwitcher != null ? _modeSwitcher.CurrentMode.ToString() : "MRPassthrough",
                    WorldScale = _mapper != null ? _mapper.WorldScale : 1.0f,
                    ActiveAgents = _agentManager != null ? _agentManager.ActiveCount : 0,
                    FrameTimeMs = dt * 1000.0f,
                    HeadPos = _embodiment != null ? _embodiment.HeadPositionWorld : Vector3.zero,
                    HeadRot = _embodiment != null ? _embodiment.HeadRotationWorld : Quaternion.identity,
                    LeftHandPos = _embodiment != null ? _embodiment.LeftHandPositionWorld : Vector3.zero,
                    LeftPinch = _embodiment != null && _embodiment.IsLeftHandPinching,
                    RightHandPos = _embodiment != null ? _embodiment.RightHandPositionWorld : Vector3.zero,
                    RightPinch = _embodiment != null && _embodiment.IsRightHandPinching
                };
                _recordingService.LogEmbodimentRow(in row);
            }

            // Periodically flush buffered streams every 2 seconds so Wi-Fi browser downloads see live rows
            if (_flushTimer >= 2.0f)
            {
                _flushTimer = 0f;
                _recordingService.Flush();
            }
        }
    }
}
