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
    /// and a responsive Desktop HUD with game-like HCI controls, hover inspector, scenario presets,
    /// and deferred command execution (never mutating scene hierarchy or GPU resources inside OnGUI).
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
        private DesktopInteractionRig _desktopRig;

        private Text _worldHudStatusText;
        private float _smoothedFps = 90f;

        // Deferred UI action flags (drained in Update() so scene/GPU mutations never run inside OnGUI!)
        private int _pendingScenarioToLoad = -1;
        private int _pendingPresetToApply = -1;
        private bool _pendingToggleDisplayMode;
        private bool _pendingTogglePause;
        private bool _pendingResetScenario;
        private bool _pendingRemoveAllAgents;
        private bool _pendingCenterCamera;
        private bool _pendingToggleFpsLook;
        private bool _pendingToggleAgentIds;
        private bool _pendingToggleRecording;
        private float _pendingWorldScale = -1f;

        private readonly Rect _hudRect = new Rect(14f, 14f, 430f, 485f);

        public bool ShowScreenOverlayOnDesktop
        {
            get => _showScreenOverlayOnDesktop;
            set => _showScreenOverlayOnDesktop = value;
        }

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
            WifiLogExportServer wifiServer,
            DesktopInteractionRig desktopRig = null)
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
            _desktopRig = desktopRig;

            BuildWorldSpaceResearcherPanel();
        }

        private void BuildWorldSpaceResearcherPanel()
        {
            Transform existing = transform.Find("SpatialResearcherWorldCanvas");
            if (existing != null)
            {
                _worldHudStatusText = existing.GetComponentInChildren<Text>();
                return;
            }

            var canvasGo = new GameObject("SpatialResearcherWorldCanvas");
            canvasGo.transform.SetParent(transform, false);
            canvasGo.transform.position = new Vector3(-0.72f, 1.35f, 1.15f);
            canvasGo.transform.rotation = Quaternion.Euler(8f, 25f, 0f);
            canvasGo.transform.localScale = Vector3.one * 0.0012f;

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;
            canvasGo.AddComponent<GraphicRaycaster>();

            RectTransform rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(540f, 360f);

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

            if (_desktopRig != null)
            {
                _desktopRig.IgnoredScreenGuiRect = _showScreenOverlayOnDesktop ? _hudRect : Rect.zero;
            }

            ProcessDeferredUiActions();
            HandleKeyboardShortcuts();
            UpdateWorldCanvasText();
        }

        private void ProcessDeferredUiActions()
        {
            if (_pendingScenarioToLoad >= 0)
            {
                int target = _pendingScenarioToLoad;
                _pendingScenarioToLoad = -1;
                _bus?.LoadScenario(target);
            }

            if (_pendingPresetToApply >= 0)
            {
                int preset = _pendingPresetToApply;
                _pendingPresetToApply = -1;
                _scenarioManager?.ApplyActiveScenarioPreset(preset);
            }

            if (_pendingToggleDisplayMode)
            {
                _pendingToggleDisplayMode = false;
                _modeSwitcher?.ToggleDisplayMode();
            }

            if (_pendingTogglePause)
            {
                _pendingTogglePause = false;
                _scenarioManager?.TogglePause();
            }

            if (_pendingResetScenario)
            {
                _pendingResetScenario = false;
                _scenarioManager?.ResetActiveScenario();
            }

            if (_pendingRemoveAllAgents)
            {
                _pendingRemoveAllAgents = false;
                _bus?.RemoveAgents(null);
                _bus?.RemoveBoundary(-1);
                _bus?.RemoveArrow(-1);
            }

            if (_pendingCenterCamera)
            {
                _pendingCenterCamera = false;
                _desktopRig?.CenterCamera();
            }

            if (_pendingToggleFpsLook)
            {
                _pendingToggleFpsLook = false;
                _desktopRig?.ToggleFpsCursorLock();
            }

            if (_pendingToggleAgentIds)
            {
                _pendingToggleAgentIds = false;
                if (_agentManager != null)
                {
                    _bus?.SetShowAgentIds(!_agentManager.ShowAgentIds);
                }
            }

            if (_pendingToggleRecording)
            {
                _pendingToggleRecording = false;
                if (_recorder != null)
                {
                    _bus?.SetRecordingState(!_recorder.IsRecording);
                }
            }

            if (_pendingWorldScale > 0f)
            {
                float newScale = _pendingWorldScale;
                _pendingWorldScale = -1f;
                _bus?.SetWorldScale(newScale);
            }
        }

        private void HandleKeyboardShortcuts()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb[Key.Digit0].wasPressedThisFrame || kb[Key.Numpad0].wasPressedThisFrame) _bus?.LoadScenario(0);
            if (kb[Key.Digit1].wasPressedThisFrame || kb[Key.Numpad1].wasPressedThisFrame) _bus?.LoadScenario(1);
            if (kb[Key.Digit2].wasPressedThisFrame || kb[Key.Numpad2].wasPressedThisFrame) _bus?.LoadScenario(2);
            if (kb[Key.Digit3].wasPressedThisFrame || kb[Key.Numpad3].wasPressedThisFrame) _bus?.LoadScenario(3);
            if (kb[Key.Digit4].wasPressedThisFrame || kb[Key.Numpad4].wasPressedThisFrame) _bus?.LoadScenario(4);
            if (kb[Key.Digit5].wasPressedThisFrame || kb[Key.Numpad5].wasPressedThisFrame) _bus?.LoadScenario(5);

            // [M] or [Tab]: Toggle MR Passthrough / VR Immersive Studio
            if ((kb[Key.M].wasPressedThisFrame || kb[Key.Tab].wasPressedThisFrame) && _modeSwitcher != null)
            {
                _modeSwitcher.ToggleDisplayMode();
            }

            // [Space]: Pause / Resume active scenario
            if (kb[Key.Space].wasPressedThisFrame && _scenarioManager != null)
            {
                _scenarioManager.TogglePause();
            }

            // [R]: Reset active scenario (only when not currently dragging an agent, where R rotates the grabbed cube)
            if (kb[Key.R].wasPressedThisFrame && (_desktopRig == null || _desktopRig.CurrentlyGrabbedAgent == null))
            {
                _scenarioManager?.ResetActiveScenario();
            }

            // [I]: Toggle floating 3D Agent ID labels
            if (kb[Key.I].wasPressedThisFrame && _agentManager != null)
            {
                _bus?.SetShowAgentIds(!_agentManager.ShowAgentIds);
            }

            // [H]: Show / Hide Desktop HUD Overlay
            if (kb[Key.H].wasPressedThisFrame)
            {
                _showScreenOverlayOnDesktop = !_showScreenOverlayOnDesktop;
            }

            // [L]: Start / Stop CSV Telemetry Recording
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
            string runState = (_scenarioManager != null && _scenarioManager.IsPaused) ? " [PAUSED]" : "";
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
                $"Scenario: <b>{scenario}{runState}</b>\n" +
                $"Active Agents: <b>{agents}</b>  |  OSC In (:{oscPort}): <b>{rxCount} msgs</b>\n" +
                $"Telemetry: <b>{recState}</b>\n" +
                $"Wi-Fi Log Download: <b>{wifiUrl}</b>\n\n" +
                $"<i>Shortcuts: [0-5] Scenarios | [Space] Pause | [R] Reset | [Tab/M] MR/VR\n" +
                $"Desktop: WASD+EQ Walk | RMB or [F] Look | LMB Drag | [C] Center Cam</i>";
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;

            // Draw center reticle crosshair when in First-Person Game Look Mode ([F])
            if (_desktopRig != null && _desktopRig.IsFpsCursorLocked)
            {
                float cx = Screen.width * 0.5f;
                float cy = Screen.height * 0.5f;
                GUI.Label(new Rect(cx - 8f, cy - 12f, 24f, 24f), "<b>+</b>");
            }

            // Draw live Hovered / Grabbed Agent Inspector Badge at bottom-center of screen
            DrawHoveredOrGrabbedAgentBadge();

            if (!_showScreenOverlayOnDesktop)
            {
                if (GUI.Button(new Rect(14f, 14f, 175f, 28f), "Show HUD Panel [H]"))
                {
                    _showScreenOverlayOnDesktop = true;
                }
                return;
            }

            GUILayout.BeginArea(_hudRect, GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>SpatialMAS Researcher Control Panel</b>");
            if (GUILayout.Button("Hide [H]", GUILayout.Width(70f)))
            {
                _showScreenOverlayOnDesktop = false;
            }
            GUILayout.EndHorizontal();

            string mode = _modeSwitcher != null ? _modeSwitcher.CurrentMode.ToString() : "MR";
            string wifiUrl = _wifiServer != null ? _wifiServer.DashboardUrl : "http://127.0.0.1:8080/";
            int agents = _agentManager != null ? _agentManager.ActiveCount : 0;
            long rxMsgs = _oscServer != null ? _oscServer.TotalMessagesReceived : 0;
            bool isPaused = _scenarioManager != null && _scenarioManager.IsPaused;

            GUILayout.Label($"FPS: {_smoothedFps:F0} | Agents: {agents} | Mode: {mode} | OSC Rx: {rxMsgs}");
            GUILayout.Label($"Wi-Fi CSV Logs: {wifiUrl}");

            // Row 1: Display Mode, CSV Recording, Agent IDs
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("MR / VR [Tab/M]")) _pendingToggleDisplayMode = true;
            bool isRec = _recorder != null && _recorder.IsRecording;
            if (GUILayout.Button(isRec ? "Stop CSV [L]" : "Start CSV [L]")) _pendingToggleRecording = true;
            bool showIds = _agentManager != null && _agentManager.ShowAgentIds;
            if (GUILayout.Button(showIds ? "Hide IDs [I]" : "Show IDs [I]")) _pendingToggleAgentIds = true;
            GUILayout.EndHorizontal();

            // Row 2: Camera & Simulation Lifecycle Controls (HS-ims + Game HCI)
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(isPaused ? "Resume [Space]" : "Pause [Space]")) _pendingTogglePause = true;
            if (GUILayout.Button("Reset [R]")) _pendingResetScenario = true;
            if (GUILayout.Button("Center Cam [C]")) _pendingCenterCamera = true;
            bool fpsLocked = _desktopRig != null && _desktopRig.IsFpsCursorLocked;
            if (GUILayout.Button(fpsLocked ? "Unlock [F]" : "FPS Look [F]")) _pendingToggleFpsLook = true;
            GUILayout.EndHorizontal();

            // Row 3: World Scale Calibration Slider
            if (_mapper != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"World Scale ({_mapper.WorldScale:F2}x):", GUILayout.Width(135f));
                float newScale = GUILayout.HorizontalSlider(_mapper.WorldScale, 0.05f, 1.5f);
                if (!Mathf.Approximately(newScale, _mapper.WorldScale))
                {
                    _pendingWorldScale = newScale;
                }
                if (GUILayout.Button("Clear All", GUILayout.Width(72f))) _pendingRemoveAllAgents = true;
                GUILayout.EndHorizontal();
            }

            // Row 4: Built-In Scenarios (0..5)
            int activeId = _scenarioManager != null ? _scenarioManager.ActiveScenarioId : 0;
            GUILayout.Label($"<b>Scenarios (Active: #{activeId} {(isPaused ? "[PAUSED]" : "")}):</b>");
            if (GUILayout.Button("0. External OSC Mode (HS-ims Port 6011)")) _pendingScenarioToLoad = 0;
            if (GUILayout.Button("1. Crowd Navigation & Human Avoidance")) _pendingScenarioToLoad = 1;
            if (GUILayout.Button("2. 3D Swarmalators (Sync & Swarm)")) _pendingScenarioToLoad = 2;
            if (GUILayout.Button("3. Orbital Attractors & Repellers")) _pendingScenarioToLoad = 3;
            if (GUILayout.Button("4. Musical Encounters (SysMus25)")) _pendingScenarioToLoad = 4;
            if (GUILayout.Button("5. Self-Assembly (6-Face Musical Cubes)")) _pendingScenarioToLoad = 5;

            // Row 5: Scenario Presets Bar
            GUILayout.BeginHorizontal();
            GUILayout.Label("Presets:", GUILayout.Width(55f));
            if (activeId == 2)
            {
                if (GUILayout.Button("Sync")) _pendingPresetToApply = 0;
                if (GUILayout.Button("Async")) _pendingPresetToApply = 1;
                if (GUILayout.Button("PhaseWave")) _pendingPresetToApply = 2;
                if (GUILayout.Button("Splinter")) _pendingPresetToApply = 3;
                if (GUILayout.Button("ActiveWave")) _pendingPresetToApply = 4;
            }
            else
            {
                if (GUILayout.Button("Preset 1 (Default)")) _pendingPresetToApply = 0;
                if (GUILayout.Button("Preset 2 (Fast/High)")) _pendingPresetToApply = 1;
                if (GUILayout.Button("Preset 3 (Calm)")) _pendingPresetToApply = 2;
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("<i>Navigation: WASD/Arrows + E/Q Move (Shift=Fast) | Hold RMB or [F] Look | MMB Pan | Scroll Zoom | LMB Drag Cubes (Alt+LMB Any)</i>");
            GUILayout.EndArea();
        }

        private void DrawHoveredOrGrabbedAgentBadge()
        {
            if (_desktopRig == null) return;
            AgentEntity target = _desktopRig.CurrentlyGrabbedAgent != null
                ? _desktopRig.CurrentlyGrabbedAgent
                : _desktopRig.HoveredAgent;

            if (target == null) return;

            string stateLabel = _desktopRig.CurrentlyGrabbedAgent != null
                ? "GRABBED (Drag to Move | Scroll Depth | [R] 90° Snap)"
                : (target.IsInteractable ? "INTERACTIVE (LMB Drag | Click)" : "AUTONOMOUS (Click | Alt+LMB Force-Drag)");

            Vector3 pos = target.transform.position;
            string badgeText = $"<b>Agent #{target.AgentId}</b> [{target.ShapeType}] — Pos: ({pos.x:F2}, {pos.y:F2}, {pos.z:F2})m — {stateLabel}";

            Rect badgeRect = new Rect((Screen.width - 640f) * 0.5f, Screen.height - 42f, 640f, 30f);
            GUI.Box(badgeRect, badgeText);
        }
    }
}
