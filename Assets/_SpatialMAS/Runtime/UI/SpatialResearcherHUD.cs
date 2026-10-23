using SpatialMAS.Agents;
using SpatialMAS.Core;
using SpatialMAS.Logging;
using SpatialMAS.Networking;
using SpatialMAS.Scenarios;
using SpatialMAS.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpatialMAS.UI
{
    /// <summary>
    /// Spatial Researcher HUD & Desktop/XR Quick Control Panel.
    /// Provides both a floating World-Space XR Canvas (positioned comfortably in the user's periphery)
    /// and keyboard/screen controls for no-code scenario switching, MR/VR toggling, WorldScale calibration,
    /// and Wi-Fi CSV log status.
    /// </summary>
    public class SpatialResearcherHUD : MonoBehaviour
    {
        [SerializeField] private bool _showScreenOverlayOnDesktop = true;

        private SimulationCommandBus _bus;
        private SpatialCoordinateMapper _mapper;
        private ScenarioManager _scenarioManager;
        private XRModeSwitcher _modeSwitcher;
        private XRPlatformAdapter _platformAdapter;
        private AgentManager _agentManager;
        private OscUdpServer _oscServer;
        private OscUdpClient _oscClient;
        private DataRecordingService _recorder;
        private WifiLogExportServer _wifiServer;

        private Text _worldHudStatusText;
        private float _smoothedFps = 90f;

        public void Initialize(
            SimulationCommandBus bus,
            SpatialCoordinateMapper mapper,
            ScenarioManager scenarioManager,
            XRModeSwitcher modeSwitcher,
            XRPlatformAdapter platformAdapter,
            AgentManager agentManager,
            OscUdpServer oscServer,
            OscUdpClient oscClient,
            DataRecordingService recorder,
            WifiLogExportServer wifiServer)
        {
            _bus = bus;
            _mapper = mapper;
            _scenarioManager = scenarioManager;
            _modeSwitcher = modeSwitcher;
            _platformAdapter = platformAdapter;
            _agentManager = agentManager;
            _oscServer = oscServer;
            _oscClient = oscClient;
            _recorder = recorder;
            _wifiServer = wifiServer;

            BuildWorldSpaceResearcherPanel();
        }

        private void BuildWorldSpaceResearcherPanel()
        {
            var canvasGo = new GameObject("SpatialResearcherWorldCanvas");
            canvasGo.transform.SetParent(transform, false);
            canvasGo.transform.position = new Vector3(-0.65f, 1.35f, 1.15f);
            canvasGo.transform.rotation = Quaternion.Euler(8f, 25f, 0f);
            canvasGo.transform.localScale = Vector3.one * 0.0012f;

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;
            canvasGo.AddComponent<GraphicRaycaster>();

            RectTransform rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(520f, 340f);

            var bgGo = new GameObject("PanelBackground");
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.06f, 0.09f, 0.14f, 0.88f);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            var textGo = new GameObject("StatusText");
            textGo.transform.SetParent(canvasGo.transform, false);
            _worldHudStatusText = textGo.AddComponent<Text>();
            _worldHudStatusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _worldHudStatusText.fontSize = 18;
            _worldHudStatusText.color = new Color(0.9f, 0.96f, 1.0f, 1.0f);
            _worldHudStatusText.alignment = TextAnchor.UpperLeft;
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(18f, 18f);
            textRt.offsetMax = new Vector2(-18f, -18f);
        }

        private void Update()
        {
            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            _smoothedFps = Mathf.Lerp(_smoothedFps, 1.0f / dt, 0.08f);

            HandleKeyboardShortcuts();
            UpdateWorldCanvasText();
        }

        private void HandleKeyboardShortcuts()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb[Key.Digit0].wasPressedThisFrame) _bus?.LoadScenario(0);
            if (kb[Key.Digit1].wasPressedThisFrame) _bus?.LoadScenario(1);
            if (kb[Key.Digit2].wasPressedThisFrame) _bus?.LoadScenario(2);
            if (kb[Key.Digit3].wasPressedThisFrame) _bus?.LoadScenario(3);
            if (kb[Key.Digit4].wasPressedThisFrame) _bus?.LoadScenario(4);
            if (kb[Key.Digit5].wasPressedThisFrame) _bus?.LoadScenario(5);

            if (kb[Key.M].wasPressedThisFrame && _modeSwitcher != null)
            {
                _modeSwitcher.ToggleDisplayMode();
            }

            if (kb[Key.L].wasPressedThisFrame && _recorder != null)
            {
                _bus?.SetRecordingState(!_recorder.IsRecording);
            }
        }

        private void UpdateWorldCanvasText()
        {
            if (_worldHudStatusText == null) return;

            string platform = _platformAdapter != null ? _platformAdapter.ActiveDeviceName : "XR/Desktop";
            string mode = _modeSwitcher != null ? _modeSwitcher.CurrentMode.ToString() : "MRPassthrough";
            string scenario = _scenarioManager != null ? _scenarioManager.ActiveScenarioName : "0. External OSC";
            int agents = _agentManager != null ? _agentManager.ActiveCount : 0;
            float scale = _mapper != null ? _mapper.WorldScale : 0.2f;
            int oscPort = _oscServer != null ? _oscServer.ListenPort : 6011;
            long rxCount = _oscServer != null ? _oscServer.TotalMessagesReceived : 0;
            string wifiUrl = _wifiServer != null ? _wifiServer.DashboardUrl : "http://127.0.0.1:8080/";
            string recState = (_recorder != null && _recorder.IsRecording) ? "RECORDING CSV" : "PAUSED";

            _worldHudStatusText.text =
                $"<b>SPATIALMAS — MULTIPLATFORM XR LAB</b>\n" +
                $"Platform: {platform} | FPS: {_smoothedFps:F0}\n" +
                $"Display Mode: <b>{mode}</b>  |  World Scale: <b>{scale:F2}x</b>\n" +
                $"Scenario: <b>{scenario}</b>\n" +
                $"Active Agents: <b>{agents}</b>  |  OSC In (:{oscPort}): <b>{rxCount} msgs</b>\n" +
                $"Telemetry: <b>{recState}</b>\n" +
                $"Wi-Fi Log Download: <b>{wifiUrl}</b>\n\n" +
                $"<i>Shortcuts: [0-5] Scenarios | [M] Toggle MR/VR | [L] Log CSV\n" +
                $"Desktop: Hold RMB+WASD Fly | LMB Drag Interactive Cubes ([R] 90° Snap)</i>";
        }

        private void OnGUI()
        {
            if (!_showScreenOverlayOnDesktop || !Application.isPlaying) return;

            GUILayout.BeginArea(new Rect(14f, 14f, 410f, 340f), GUI.skin.box);
            GUILayout.Label("<b>SpatialMAS Researcher Control Panel</b>");

            string mode = _modeSwitcher != null ? _modeSwitcher.CurrentMode.ToString() : "MR";
            string wifiUrl = _wifiServer != null ? _wifiServer.DashboardUrl : "http://127.0.0.1:8080/";
            int agents = _agentManager != null ? _agentManager.ActiveCount : 0;

            GUILayout.Label($"FPS: {_smoothedFps:F0} | Agents: {agents} | Mode: {mode}");
            GUILayout.Label($"Wi-Fi CSV Logs: {wifiUrl}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Toggle MR / VR [M]")) _modeSwitcher?.ToggleDisplayMode();
            bool isRec = _recorder != null && _recorder.IsRecording;
            if (GUILayout.Button(isRec ? "Stop CSV Log [L]" : "Start CSV Log [L]"))
            {
                _bus?.SetRecordingState(!isRec);
            }
            GUILayout.EndHorizontal();

            if (_mapper != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"World Scale ({_mapper.WorldScale:F2}x):", GUILayout.Width(135f));
                float newScale = GUILayout.HorizontalSlider(_mapper.WorldScale, 0.05f, 1.5f);
                if (!Mathf.Approximately(newScale, _mapper.WorldScale))
                {
                    _bus?.SetWorldScale(newScale);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Label("<b>No-Code Built-in Scenarios (or keys 0-5):</b>");
            if (GUILayout.Button("0. External OSC Mode (HS-ims Port 6011)")) _bus?.LoadScenario(0);
            if (GUILayout.Button("1. Crowd Navigation & Human Avoidance")) _bus?.LoadScenario(1);
            if (GUILayout.Button("2. 3D Swarmalators (Sync & Swarm)")) _bus?.LoadScenario(2);
            if (GUILayout.Button("3. Orbital Attractors & Repellers")) _bus?.LoadScenario(3);
            if (GUILayout.Button("4. Musical Encounters (SysMus25)")) _bus?.LoadScenario(4);
            if (GUILayout.Button("5. Self-Assembly (6-Face Musical Cubes)")) _bus?.LoadScenario(5);

            GUILayout.EndArea();
        }
    }
}
