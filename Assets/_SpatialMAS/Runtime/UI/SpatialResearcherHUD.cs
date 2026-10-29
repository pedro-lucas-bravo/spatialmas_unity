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
using UnityEngine.XR;

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
        private SpatialCalibrationReference _calibration;
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
        private Canvas _worldCanvas;
        private XRUIInputModule _worldInputModule;
        private bool _worldPanelWasInXr;
        private bool _worldPanelNeedsTrackedPlacement = true;
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
        private bool _pendingToggleReferenceVisible;
        private bool _pendingToggleReferenceLocked;
        private bool _pendingPlaceReference;
        private bool _pendingResetReference;
        private bool _pendingRotateReference;
        private bool _pendingSetScenarioCenter;
        private int _pendingLayout = -1;

        private readonly Rect _hudRect = new Rect(14f, 14f, 450f, 610f);
        private bool IsHeadsetActive => (_platformAdapter != null && _platformAdapter.IsXRHeadsetActive) ||
            (Camera.main != null && Camera.main.stereoEnabled);

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
            DesktopInteractionRig desktopRig = null,
            SpatialCalibrationReference calibrationReference = null)
        {
            _bus = bus;
            _calibration = calibrationReference;
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
                ConfigureWorldCanvas();
                return;
            }

            var canvasGo = new GameObject("SpatialResearcherWorldCanvas");
            _worldCanvasObject = canvasGo;
            canvasGo.transform.SetParent(transform, false);
            canvasGo.transform.localScale = Vector3.one * 0.0010f;

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            RectTransform rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(620f, 865f);

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

            CreateWorldButton(rt, "Next: Compact", new Rect(18, 554, 288, 42), () => _pendingLayout = 0);
            CreateWorldButton(rt, "Next: Room Layout", new Rect(314, 554, 288, 42), () => _pendingLayout = 1);
            CreateWorldButton(rt, "Spawn Next Scenario Here", new Rect(18, 604, 584, 42), () => _pendingSetScenarioCenter = true);
            CreateWorldButton(rt, "Reference Show / Hide", new Rect(18, 654, 288, 42), () => _pendingToggleReferenceVisible = true);
            CreateWorldButton(rt, "Reference Lock / Unlock", new Rect(314, 654, 288, 42), () => _pendingToggleReferenceLocked = true);
            CreateWorldButton(rt, "Place Reference Ahead", new Rect(18, 704, 288, 42), () => _pendingPlaceReference = true);
            CreateWorldButton(rt, "Rotate Reference 90°", new Rect(314, 704, 288, 42), () => _pendingRotateReference = true);
            CreateWorldButton(rt, "Reset Reference Pose", new Rect(18, 754, 288, 42), () => _pendingResetReference = true);
            CreateWorldButton(rt, "Clear All", new Rect(314, 754, 288, 42), () => _pendingRemoveAllAgents = true);
            CreateWorldButton(rt, "Move Panel Here", new Rect(18, 804, 584, 42), PositionWorldPanel);
            ConfigureWorldCanvas();
            PositionWorldPanel();
        }

        private void ConfigureWorldCanvas()
        {
            _worldCanvas = _worldCanvasObject.GetComponent<Canvas>();
            if (_worldCanvas == null) _worldCanvas = _worldCanvasObject.AddComponent<Canvas>();
            _worldCanvas.renderMode = RenderMode.WorldSpace;
            _worldCanvas.worldCamera = Camera.main;
            var raycaster = _worldCanvasObject.GetComponent<TrackedDeviceGraphicRaycaster>();
            if (raycaster == null) raycaster = _worldCanvasObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            raycaster.ignoreReversedGraphics = false;
            raycaster.checkFor2DOcclusion = false;
            raycaster.checkFor3DOcclusion = false;
            _worldInputModule = EnsureXRUIEventSystem(transform);
            _worldInputModule.uiCamera = _worldCanvas.worldCamera;
            _worldInputModule.RegisterWaitlistInteractors();

            // Keep explicit SPI variants in Resources, including in headset builds. Text still supplies its font atlas.
            Material material = Resources.Load<Material>("Mat_SpatialMAS_WorldUI");
            if (material != null)
                foreach (var graphic in _worldCanvasObject.GetComponentsInChildren<Graphic>(true))
                    graphic.material = material;
        }

        private static XRUIInputModule EnsureXRUIEventSystem(Transform parent)
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
            return module;
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
            if (_worldCanvasObject == null) return;
            if (TryGetWorldHeadPose(out Pose trackedPose))
            {
                PlaceWorldPanel(trackedPose);
                return;
            }
            // A stereo camera can still contain its authored desktop pose while the XR provider starts.
            // Keep the request pending until real tracking is available, including "Move Panel Here".
            if (IsHeadsetActive)
            {
                _worldPanelNeedsTrackedPlacement = true;
                return;
            }
            if (Camera.main != null)
                PlaceWorldPanel(new Pose(Camera.main.transform.position, Camera.main.transform.rotation));
        }

        protected virtual bool TryGetWorldHeadPose(out Pose pose)
        {
            var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) && tracked &&
                device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 position) &&
                device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion rotation))
            {
                Transform origin = XRInteractionRigSetup.GetTrackingOrigin(Camera.main);
                pose = XRInteractionRigSetup.TrackingPoseToWorld(new Pose(position, rotation), origin);
                return true;
            }
            pose = default;
            return false;
        }

        private void PlaceWorldPanel(Pose head)
        {
            Vector3 forward = Vector3.ProjectOnPlane(head.rotation * Vector3.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
            Vector3 position = head.position + forward * 1.25f - Vector3.up * 0.1f;
            _worldCanvasObject.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
            _worldPanelNeedsTrackedPlacement = false;
        }

        private void LateUpdate()
        {
            UpdateWorldPanelTracking(IsHeadsetActive);
        }

        private void UpdateWorldPanelTracking(bool headsetActive)
        {
            if (_worldCanvasObject == null) return;
            if (!headsetActive)
            {
                _worldPanelWasInXr = false;
                _worldPanelNeedsTrackedPlacement = true;
                return;
            }
            if (!_worldPanelWasInXr || (_worldCanvas != null && _worldCanvas.worldCamera != Camera.main))
            {
                ConfigureWorldCanvas();
                _worldPanelNeedsTrackedPlacement = true;
            }
            _worldPanelWasInXr = true;
            if (_worldPanelNeedsTrackedPlacement && TryGetWorldHeadPose(out Pose trackedPose)) PlaceWorldPanel(trackedPose);
        }

        private Vector3 PositionAhead()
        {
            if (Camera.main == null) return new Vector3(0f, 1.2f, 0.8f);
            Transform head = Camera.main.transform;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            return head.position + forward * 0.8f - Vector3.up * 0.3f;
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

            if (_pendingLayout >= 0)
            {
                _bus?.SetScenarioLayout((ScenarioLayout)_pendingLayout);
                _pendingLayout = -1;
            }
            if (_pendingSetScenarioCenter) { _pendingSetScenarioCenter = false; _bus?.SetScenarioCenter(PositionAhead()); }
            if (_pendingToggleReferenceVisible) { _pendingToggleReferenceVisible = false; if (_calibration != null) _bus?.SetCalibrationVisible(!_calibration.Visible); }
            if (_pendingToggleReferenceLocked) { _pendingToggleReferenceLocked = false; if (_calibration != null) _bus?.SetCalibrationLocked(!_calibration.Locked); }
            if (_pendingPlaceReference) { _pendingPlaceReference = false; if (_calibration != null) _bus?.SetCalibrationPose(PositionAhead(), _calibration.Snapshot.Rotation); }
            if (_pendingResetReference) { _pendingResetReference = false; _bus?.SetCalibrationPose(Vector3.zero, Quaternion.identity); }
            if (_pendingRotateReference) { _pendingRotateReference = false; if (_calibration != null) _bus?.SetCalibrationPose(_calibration.Snapshot.Position, Quaternion.AngleAxis(90f, Vector3.up) * _calibration.Snapshot.Rotation); }
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
            if (kb[Key.R].wasPressedThisFrame && (_desktopRig == null || !_desktopRig.IsDraggingObject))
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
            string mode = _modeSwitcher != null ? _modeSwitcher.DisplayStatus : "MR preview (no headset)";
            string scenario = _scenarioManager != null ? _scenarioManager.ActiveScenarioName : "0. External OSC";
            string runState = (_scenarioManager != null && _scenarioManager.IsPaused) ? " [PAUSED]" : "";
            int agents = _agentManager != null ? _agentManager.ActiveCount : 0;
            int oscPort = _oscServer != null ? _oscServer.ListenPort : 6011;
            long rxCount = _oscServer != null ? _oscServer.TotalMessagesReceived : 0;
            string wifiUrl = _wifiServer != null ? _wifiServer.DashboardUrl : "http://127.0.0.1:8080/";
            string recState = (_recorder != null && _recorder.IsRecording) ? "RECORDING CSV" : "PAUSED";

            _worldHudStatusText.text =
                $"<b>SPATIALMAS — MULTIPLATFORM XR LAB</b>\n" +
                $"Platform: {platform} | FPS: {_smoothedFps:F0}\n" +
                $"Display: <b>{mode}</b> | Units: Unity world meters\n" +
                $"Scenario: <b>{scenario}{runState}</b>\n" +
                $"Active Agents: <b>{agents}</b>  |  OSC In (:{oscPort}): <b>{rxCount} msgs</b>\n" +
                $"Telemetry: <b>{recState}</b> | Next layout: {_scenarioManager?.NextLayout}\n" +
                $"Reference: {(_calibration == null ? "unavailable" : (_calibration.Locked ? "LOCKED" : "UNLOCKED"))} | Visible: {_calibration?.Visible}\n" +
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

            string mode = _modeSwitcher != null ? _modeSwitcher.DisplayStatus : "MR preview (no headset)";
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

            GUILayout.Label($"Unity world meters | Next layout: {_scenarioManager?.NextLayout}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Next: Compact")) _pendingLayout = 0;
            if (GUILayout.Button("Next: Room Layout")) _pendingLayout = 1;
            if (GUILayout.Button("Spawn Next Here")) _pendingSetScenarioCenter = true;
            if (GUILayout.Button("Clear All")) _pendingRemoveAllAgents = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("Placement/layout apply on next scenario load or reset.");
            if (_calibration != null)
            {
                var state = _calibration.Snapshot;
                GUILayout.Label($"Reference #{state.Revision}: {state.Position:F3} m | {(state.Locked ? "LOCKED" : "UNLOCKED (drag marker; R rotates)")}");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(state.Visible ? "Hide Reference" : "Show Reference")) _pendingToggleReferenceVisible = true;
                if (GUILayout.Button(state.Locked ? "Unlock Reference" : "Lock Reference")) _pendingToggleReferenceLocked = true;
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUI.enabled = !state.Locked;
                if (GUILayout.Button("Reference Ahead")) _pendingPlaceReference = true;
                if (GUILayout.Button("Rotate 90°")) _pendingRotateReference = true;
                if (GUILayout.Button("Reset Reference")) _pendingResetReference = true;
                GUI.enabled = true;
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
