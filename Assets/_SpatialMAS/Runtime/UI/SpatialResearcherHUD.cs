using SpatialMAS.Agents;
using SpatialMAS.Core;
using SpatialMAS.Logging;
using SpatialMAS.Networking;
using SpatialMAS.Scenarios;
using SpatialMAS.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;

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
        private GameObject _worldCanvasObject;
        private Slider _worldScaleSlider;
        private readonly Button[] _worldPresetButtons = new Button[5];
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
        private bool IsHeadsetActive => _platformAdapter != null && _platformAdapter.IsXRHeadsetActive;

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
                _worldCanvasObject = existing.gameObject;
                return;
            }

            var canvasGo = new GameObject("SpatialResearcherWorldCanvas");
            _worldCanvasObject = canvasGo;
            canvasGo.transform.SetParent(transform, false);
            canvasGo.transform.localScale = Vector3.one * 0.0012f;

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;
            canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();
            EnsureXRUIEventSystem(transform);

            RectTransform rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(620f, 750f);

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
            _worldHudStatusText.raycastTarget = false;
            var textRt = textGo.GetComponent<RectTransform>();
            SetTopLeftRect(textRt, new Rect(18f, 18f, 584f, 195f));

            string[] scenarioNames = { "0 · External OSC", "1 · Crowd Avoidance", "2 · Swarmalators", "3 · Attractors", "4 · Musical Encounters", "5 · Self-Assembly" };
            for (int i = 0; i < scenarioNames.Length; i++)
            {
                int scenario = i;
                CreateWorldButton(rt, scenarioNames[i], new Rect(18 + (i % 2) * 296, 220 + (i / 2) * 54, 288, 46),
                    () => _pendingScenarioToLoad = scenario);
            }
            CreateWorldButton(rt, "Pause / Resume", new Rect(18, 390, 288, 46), () => _pendingTogglePause = true);
            CreateWorldButton(rt, "Reset Scenario", new Rect(314, 390, 288, 46), () => _pendingResetScenario = true);
            CreateWorldButton(rt, "MR / VR", new Rect(18, 444, 186, 46), () => _pendingToggleDisplayMode = true);
            CreateWorldButton(rt, "Start / Stop CSV", new Rect(214, 444, 192, 46), () => _pendingToggleRecording = true);
            CreateWorldButton(rt, "Agent IDs", new Rect(416, 444, 186, 46), () => _pendingToggleAgentIds = true);
            for (int i = 0; i < _worldPresetButtons.Length; i++)
            {
                int preset = i;
                _worldPresetButtons[i] = CreateWorldButton(rt, "Preset " + (i + 1), new Rect(18 + i * 118, 500, 110, 46),
                    () => _pendingPresetToApply = preset);
            }

            var scaleLabel = CreateWorldText(rt, "World scale", 18);
            SetTopLeftRect(scaleLabel.rectTransform, new Rect(18, 560, 140, 35));
            var sliderGo = new GameObject("World Scale", typeof(RectTransform), typeof(Image), typeof(Slider));
            sliderGo.transform.SetParent(rt, false);
            SetTopLeftRect(sliderGo.GetComponent<RectTransform>(), new Rect(160, 560, 442, 35));
            sliderGo.GetComponent<Image>().color = new Color(0.18f, 0.25f, 0.32f);
            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(sliderGo.transform, false);
            var handle = handleGo.GetComponent<RectTransform>();
            handle.sizeDelta = new Vector2(32f, 45f);
            handleGo.GetComponent<Image>().color = new Color(0.2f, 0.85f, 1f);
            _worldScaleSlider = sliderGo.GetComponent<Slider>();
            _worldScaleSlider.handleRect = handle;
            _worldScaleSlider.targetGraphic = handleGo.GetComponent<Image>();
            _worldScaleSlider.minValue = 0.05f;
            _worldScaleSlider.maxValue = 1.5f;
            _worldScaleSlider.SetValueWithoutNotify(_mapper != null ? _mapper.WorldScale : 0.2f);
            _worldScaleSlider.onValueChanged.AddListener(scale => _pendingWorldScale = scale);
            CreateWorldButton(rt, "Center Arena Ahead", new Rect(18, 624, 288, 46), CenterArenaAhead);
            CreateWorldButton(rt, "Clear All", new Rect(314, 624, 288, 46), () => _pendingRemoveAllAgents = true);
            CreateWorldButton(rt, "Move Panel Here", new Rect(18, 680, 584, 46), PositionWorldPanel);
            PositionWorldPanel();
        }

        private static void EnsureXRUIEventSystem(Transform parent)
        {
            var system = EventSystem.current;
            if (system == null)
            {
                var go = new GameObject("SpatialMAS EventSystem");
                go.transform.SetParent(parent, false);
                system = go.AddComponent<EventSystem>();
            }
            var module = system.GetComponent<XRUIInputModule>();
            foreach (var other in system.GetComponents<BaseInputModule>())
                if (other != module) other.enabled = false;
            if (module == null) module = system.gameObject.AddComponent<XRUIInputModule>();
            module.enableXRInput = true;
            module.enableMouseInput = true;
            module.enabled = true;
        }

        private static void SetTopLeftRect(RectTransform rect, Rect bounds)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(bounds.x, -bounds.y);
            rect.sizeDelta = bounds.size;
        }

        private static Text CreateWorldText(Transform parent, string label, int fontSize)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = Color.white;
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateWorldButton(Transform parent, string label, Rect bounds, UnityAction action)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            SetTopLeftRect(go.GetComponent<RectTransform>(), bounds);
            var image = go.GetComponent<Image>();
            image.color = new Color(0.12f, 0.28f, 0.38f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            var text = CreateWorldText(go.transform, label, 19);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private void PositionWorldPanel()
        {
            if (_worldCanvasObject == null || Camera.main == null) return;
            Transform head = Camera.main.transform;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
            Vector3 position = head.position + forward * 1.1f - Vector3.Cross(Vector3.up, forward) * 0.55f - Vector3.up * 0.1f;
            _worldCanvasObject.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
        }

        private void CenterArenaAhead()
        {
            if (Camera.main == null) return;
            Transform head = Camera.main.transform;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            _bus?.SetOriginOffset(head.position + forward * 0.8f - Vector3.up * 0.3f);
        }

        private void Update()
        {
            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            _smoothedFps = Mathf.Lerp(_smoothedFps, 1.0f / dt, 0.08f);

            if (_desktopRig != null)
            {
                _desktopRig.IgnoredScreenGuiRect = _showScreenOverlayOnDesktop && !IsHeadsetActive ? _hudRect : Rect.zero;
            }
            if (_worldCanvasObject != null) _worldCanvasObject.SetActive(IsHeadsetActive);
            if (_worldScaleSlider != null && _mapper != null) _worldScaleSlider.SetValueWithoutNotify(_mapper.WorldScale);
            for (int i = 0; i < _worldPresetButtons.Length; i++)
                if (_worldPresetButtons[i] != null) _worldPresetButtons[i].gameObject.SetActive(i < 3 || (_scenarioManager != null && _scenarioManager.ActiveScenarioId == 2));

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
                $"Wi-Fi Log Download: <b>{wifiUrl}</b>";
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            if (IsHeadsetActive) return;

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
