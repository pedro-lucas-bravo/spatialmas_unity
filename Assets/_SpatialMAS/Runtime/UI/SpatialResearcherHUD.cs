using TMPro;
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
        [SerializeField, Min(0.35f), Tooltip("Fixed horizontal distance of the researcher panel from the tracked head, in meters.")]
        private float _panelHorizontalRadius = 1.25f;
        public float PanelHorizontalRadius { get => _panelHorizontalRadius; set => _panelHorizontalRadius = Mathf.Max(0.35f, value); }
        public float PanelAngularDeadZone { get; set; } = 8f;
        public float PanelCatchUpSeconds { get; set; } = .25f;
        private PanelSettings _requestedPanelSettings, _appliedPanelSettings;
        private bool _hasPanelSettings, _hasAppliedPanelSettings;
        public bool HasPendingPanelSettings => _hasPanelSettings && (!_hasAppliedPanelSettings || !_requestedPanelSettings.Same(_appliedPanelSettings));

        private struct PanelSettings
        {
            public float Radius, MinimumDistance, MaximumDistance, DepthDeadZone;
            public float Angular, AngularTime, Horizontal, HorizontalStop, HorizontalTime, Vertical, VerticalStop, VerticalTime;
            private static float Safe(float value, float fallback, float min, float max = 1000f) =>
                float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
            public static PanelSettings Read(SpatialMASConfig c)
            {
                var s = new PanelSettings {
                    Radius = Safe(c.researcherPanelRadius, 1.25f, .35f), Angular = Safe(c.researcherPanelAngularDeadZone, 8f, 0f, 180f),
                    MinimumDistance = Safe(c.researcherPanelMinDistance, .65f, .35f),
                    MaximumDistance = Safe(c.researcherPanelMaxDistance, 2.25f, .35f),
                    DepthDeadZone = Safe(c.researcherPanelDepthDragDeadZone, .03f, 0f),
                    AngularTime = Safe(c.researcherPanelCatchUpSeconds, .25f, .01f),
                    Horizontal = Safe(c.researcherPanelHorizontalDeadZone, .1f, 0f), HorizontalTime = Safe(c.researcherPanelHorizontalCatchUpSeconds, .25f, .01f),
                    Vertical = Safe(c.researcherPanelVerticalDeadZone, .08f, 0f), VerticalTime = Safe(c.researcherPanelVerticalCatchUpSeconds, .25f, .01f) };
                s.HorizontalStop = Safe(c.researcherPanelHorizontalStopDistance, .05f, 0f, s.Horizontal);
                s.VerticalStop = Safe(c.researcherPanelVerticalStopDistance, .04f, 0f, s.Vertical);
                return s;
            }
            public bool Same(PanelSettings s) => SameDistance(s) && Angular == s.Angular && AngularTime == s.AngularTime &&
                Horizontal == s.Horizontal && HorizontalStop == s.HorizontalStop && HorizontalTime == s.HorizontalTime &&
                Vertical == s.Vertical && VerticalStop == s.VerticalStop && VerticalTime == s.VerticalTime;
            public bool SameDistance(PanelSettings s) => Radius == s.Radius && MinimumDistance == s.MinimumDistance &&
                MaximumDistance == s.MaximumDistance && DepthDeadZone == s.DepthDeadZone;
        }

        public bool ApplyPanelSettings(SpatialMASConfig config)
        {
            if (config == null) return false;
            bool paletteChanged = ApplyPanelPalette(config.researcherPanelPalette);
            PanelSettings settings = PanelSettings.Read(config);
            if (_hasPanelSettings && settings.Same(_requestedPanelSettings)) return paletteChanged;
            _requestedPanelSettings = settings;
            _hasPanelSettings = true;
            TryApplyPanelSettings();
            return true;
        }

        private void TryApplyPanelSettings()
        {
            if (!HasPendingPanelSettings) return;
            if ((_moveHandle != null && _moveHandle.IsDragging) || PanelHasPointerIntent()) return;
            var s = _requestedPanelSettings;
            PanelHorizontalRadius = s.Radius;
            PanelAngularDeadZone = s.Angular;
            PanelCatchUpSeconds = s.AngularTime;
            if (_panelFollow == null) return; // Retain pending data until construction completes.
            if (!_hasAppliedPanelSettings || !s.SameDistance(_appliedPanelSettings))
                _panelFollow.ConfigureDistance(s.MinimumDistance, s.Radius, s.MaximumDistance, s.DepthDeadZone);
            if (!_hasAppliedPanelSettings || s.Angular != _appliedPanelSettings.Angular || s.AngularTime != _appliedPanelSettings.AngularTime)
                _panelFollow.ConfigureTuning(s.Angular, s.AngularTime);
            if (!_hasAppliedPanelSettings || s.Horizontal != _appliedPanelSettings.Horizontal || s.HorizontalStop != _appliedPanelSettings.HorizontalStop ||
                s.HorizontalTime != _appliedPanelSettings.HorizontalTime || s.Vertical != _appliedPanelSettings.Vertical ||
                s.VerticalStop != _appliedPanelSettings.VerticalStop || s.VerticalTime != _appliedPanelSettings.VerticalTime)
                _panelFollow.ConfigurePositionTuning(s.Horizontal, s.HorizontalStop, s.HorizontalTime, s.Vertical, s.VerticalStop, s.VerticalTime);
            _appliedPanelSettings = s;
            _hasAppliedPanelSettings = true;
        }

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

        private TextMeshProUGUI _worldHudStatusText;
        private TextMeshProUGUI _runStatus, _recordStatus, _pauseLabel, _recordLabel, _layoutStatus;
        private TextMeshProUGUI _calibrationStatus, _displayStatus, _sessionStatus, _diagnosticsStatus;
        private TextMeshProUGUI _recordDot;
        private TextMeshProUGUI _moveHint;
        private float _moveHintUntil;
        private bool _moveHintShown;
        private readonly GameObject[] _tabPages = new GameObject[3];
        private readonly GameObject[] _tabIndicators = new GameObject[3];
        private readonly GameObject[] _scenarioIndicators = new GameObject[6];
        private readonly TextMeshProUGUI[] _presetLabels = new TextMeshProUGUI[5];
        private static readonly string[] DefaultPresetNames = { "Default", "Fast / High", "Calm" };
        private static readonly string[] SwarmPresetNames = { "Static Sync", "Static Async", "Phase Wave", "Splinter", "Active Wave" };
        private GameObject _clearConfirmation, _diagnostics;
        private Button _diagnosticsButton;
        private float _nextPanelStatusRefresh;
        private static readonly ResearcherPanelPalette DefaultPanelPalette = new ResearcherPanelPalette();
        private ResearcherPanelPalette _panelPalette = DefaultPanelPalette;
        private readonly System.Collections.Generic.List<System.Action<ResearcherPanelPalette>> _paletteBindings =
            new System.Collections.Generic.List<System.Action<ResearcherPanelPalette>>();
        private readonly Button[] _tabButtons = new Button[3];
        private int _selectedTab;

        private bool ApplyPanelPalette(ResearcherPanelPalette palette)
        {
            palette = palette ?? DefaultPanelPalette;
            if (_panelPalette.Matches(palette)) return false;
            _panelPalette = palette.Copy();
            foreach (var binding in _paletteBindings) binding(_panelPalette);
            UpdateTabSurfaceColors();
            if (_moveHandle != null) _moveHandle.SetOutlineColor(_panelPalette.dragOutline);
            _nextPanelStatusRefresh = 0f;
            UpdateWorldCanvasText();
            return true;
        }

        private void BindPanelColor(Graphic graphic, System.Func<ResearcherPanelPalette, Color> color)
        {
            graphic.color = color(_panelPalette);
            _paletteBindings.Add(palette => graphic.color = color(palette));
        }
        private GameObject _worldCanvasObject;
        private Canvas _worldCanvas;
        private XRUIInputModule _worldInputModule;
        private XRResearcherPanelLifecycle _panelLifecycle;
        private XRPanelFollow _panelFollow;
        private XRPanelMoveHandle _moveHandle;
        private Button _followButton;
        private bool _wasPanelDragging;
        private UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor[] _panelPointers;
        private float _nextPointerRefresh;
        private readonly System.Collections.Generic.HashSet<int> _panelPresses = new System.Collections.Generic.HashSet<int>();
        private bool _worldPanelWasInXr;
        private bool _worldPanelNeedsTrackedPlacement = true;
        private bool _hasTrackedPanelPlacement;
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
                _worldCanvasObject = existing.gameObject;
                ConfigureWorldCanvas();
                ConfigurePanelLifecycle();
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
            rt.sizeDelta = new Vector2(620f, 965f);

            var bgGo = new GameObject("PanelBackground");
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bgImg = bgGo.AddComponent<Image>();
            BindPanelColor(bgImg, palette => palette.shell);
            bgImg.raycastTarget = false;
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            CreatePanelLabel(rt, "Researcher Panel", new Rect(18, 14, 424, 46), 26, palette => palette.primaryText);
            CreateWorldButton(rt, "Close", new Rect(454, 14, 148, 46), () => _panelLifecycle.Hide());
            _worldHudStatusText = CreatePanelLabel(rt, "", new Rect(18, 67, 584, 28), 22, palette => palette.primaryText);
            _runStatus = CreatePanelLabel(rt, "", new Rect(18, 99, 235, 26), 19, palette => _scenarioManager?.IsPaused == true ? palette.paused : palette.running);
            _recordDot = CreatePanelLabel(rt, "●", new Rect(254, 99, 20, 26), 17, palette => palette.recording);
            _recordStatus = CreatePanelLabel(rt, "", new Rect(276, 99, 326, 26), 19,
                palette => _recorder?.IsRecording == true ? palette.recording : palette.notRecording);

            CreatePanelImage(rt, "Content Inset", new Rect(18, 183, 584, 612), palette => palette.inset);
            string[] tabNames = { "Run", "Setup", "Session" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                int tab = i;
                var button = CreateWorldButton(rt, tabNames[i], new Rect(18 + i * 198, 135, 188, 48), () => SelectPanelTab(tab), tab: true);
                _tabButtons[i] = button;
                _tabIndicators[i] = CreatePanelImage(button.transform, "Active tab marker",
                    new Rect(12, 0, 164, 3), palette => palette.tabMarker).gameObject;
                _tabPages[i] = CreatePanelGroup(rt, tabNames[i] + " Content");
            }
            BuildRunTab(_tabPages[0].transform);
            BuildSetupTab(_tabPages[1].transform);
            BuildSessionTab(_tabPages[2].transform);
            SelectPanelTab(0);

            CreateWorldButton(rt, "Recenter", new Rect(18, 804, 288, 46), PositionWorldPanel);
            _followButton = CreateWorldButton(rt, "Pin in room", new Rect(314, 804, 288, 46), TogglePanelFollow);
            CreateWorldButton(rt, "Participant Mode", new Rect(18, 854, 584, 46), () => _panelLifecycle.ResearcherMode = false);
            _moveHint = CreatePanelLabel(rt, "Pinch and drag blank space to move", new Rect(18, 908, 584, 35), 19, palette => palette.accent);
            _moveHint.gameObject.SetActive(false);
            ConfigureWorldCanvas();
            ConfigurePanelLifecycle();
            PositionWorldPanel();
            UpdateWorldCanvasText();
        }

        private void BuildRunTab(Transform page)
        {
            _pauseLabel = CreateWorldButton(page, "Pause", new Rect(18, 200, 584, 60),
                () => _pendingTogglePause = true, palette => palette.accent).GetComponentInChildren<TextMeshProUGUI>();
            _pauseLabel.fontSize = 24;
            CreatePanelLabel(page, "Load scenario", new Rect(18, 277, 584, 26), 20, palette => palette.secondaryText);
            string[] names = { "0 · External OSC", "1 · Crowd Avoidance", "2 · Swarmalators", "3 · Attractors", "4 · Musical Encounters", "5 · Self-Assembly" };
            for (int i = 0; i < names.Length; i++)
            {
                int scenario = i;
                var button = CreateWorldButton(page, names[i], new Rect(18 + i % 2 * 296, 311 + i / 2 * 57, 288, 51),
                    () => _pendingScenarioToLoad = scenario);
                _scenarioIndicators[i] = CreateSelectionMark(button).gameObject;
            }
            CreatePanelLabel(page, "Scenario presets", new Rect(18, 495, 584, 26), 20, palette => palette.secondaryText);
            for (int i = 0; i < _worldPresetButtons.Length; i++)
            {
                int preset = i;
                _worldPresetButtons[i] = CreateWorldButton(page, "Preset " + (i + 1),
                    new Rect(18 + i % 3 * 198, 528 + i / 3 * 57, 188, 51), () => _pendingPresetToApply = preset);
                _presetLabels[i] = _worldPresetButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            }
            CreatePanelLabel(page, "Restart the active scenario", new Rect(18, 651, 584, 26), 19, palette => palette.secondaryText);
            CreateWorldButton(page, "Reset Scenario", new Rect(18, 686, 288, 50), () => _pendingResetScenario = true);
        }

        private void BuildSetupTab(Transform page)
        {
            _layoutStatus = CreatePanelLabel(page, "", new Rect(18, 199, 584, 52), 20, palette => palette.pending);
            CreateWorldButton(page, "Next: Compact", new Rect(18, 257, 288, 48), () => _pendingLayout = 0);
            CreateWorldButton(page, "Next: Room Layout", new Rect(314, 257, 288, 48), () => _pendingLayout = 1);
            CreateWorldButton(page, "Spawn Next Scenario Here", new Rect(18, 313, 584, 48), () => _pendingSetScenarioCenter = true);
            _calibrationStatus = CreatePanelLabel(page, "", new Rect(18, 380, 584, 31), 20, palette => palette.secondaryText);
            CreateWorldButton(page, "Reference Show / Hide", new Rect(18, 419, 288, 48), () => _pendingToggleReferenceVisible = true);
            CreateWorldButton(page, "Reference Lock / Unlock", new Rect(314, 419, 288, 48), () => _pendingToggleReferenceLocked = true);
            CreateWorldButton(page, "Place Reference Ahead", new Rect(18, 475, 288, 48), () => _pendingPlaceReference = true);
            CreateWorldButton(page, "Rotate Reference 90°", new Rect(314, 475, 288, 48), () => _pendingRotateReference = true);
            CreateWorldButton(page, "Reset Reference Pose", new Rect(18, 531, 288, 48), () => _pendingResetReference = true);
            _displayStatus = CreatePanelLabel(page, "", new Rect(18, 605, 584, 31), 20, palette => palette.secondaryText);
            CreateWorldButton(page, "MR / VR", new Rect(18, 645, 288, 48), () => _pendingToggleDisplayMode = true);
            CreateWorldButton(page, "Agent IDs", new Rect(314, 645, 288, 48), () => _pendingToggleAgentIds = true);
            BuildSecondaryActions(page);
        }

        private void BuildSessionTab(Transform page)
        {
            _recordLabel = CreateWorldButton(page, "Start recording", new Rect(18, 200, 584, 51),
                () => _pendingToggleRecording = true).GetComponentInChildren<TextMeshProUGUI>();
            _sessionStatus = CreatePanelLabel(page, "", new Rect(18, 264, 584, 120), 19, palette => palette.primaryText);
            _diagnosticsButton = CreateWorldButton(page, "Show Diagnostics", new Rect(18, 389, 584, 48),
                () => SetDiagnosticsVisible(!_diagnostics.activeSelf));
            _diagnostics = CreatePanelGroup(page, "Diagnostics");
            _diagnosticsStatus = CreatePanelLabel(_diagnostics.transform, "", new Rect(18, 449, 584, 71), 18, palette => palette.secondaryText);
            SetDiagnosticsVisible(false);
        }

        private void BuildSecondaryActions(Transform page)
        {
            CreatePanelLabel(page, "Secondary actions", new Rect(18, 695, 584, 26), 18, palette => palette.secondaryText);
            CreateWorldButton(page, "Clear All", new Rect(18, 733, 288, 48), () => SetClearConfirmation(true));
            _clearConfirmation = CreatePanelGroup(page, "Clear Confirmation");
            var confirmation = CreatePanelImage(_clearConfirmation.transform, "Confirmation backdrop",
                new Rect(18, 727, 584, 66), palette => palette.inset, true);
            CreateWorldButton(confirmation.transform, "Confirm clear all agents", new Rect(0, 6, 352, 48), () =>
            {
                SetClearConfirmation(false);
                _pendingRemoveAllAgents = true;
            }, palette => palette.confirmation);
            CreateWorldButton(confirmation.transform, "Cancel", new Rect(364, 6, 220, 48), () => SetClearConfirmation(false));
            SetClearConfirmation(false);
        }

        private void SetDiagnosticsVisible(bool visible)
        {
            _diagnostics.SetActive(visible);
            _diagnosticsButton.GetComponentInChildren<TextMeshProUGUI>().text = visible ? "Hide Diagnostics" : "Show Diagnostics";
            _nextPanelStatusRefresh = 0f;
        }

        private void SetClearConfirmation(bool visible)
        {
            if (_clearConfirmation != null) _clearConfirmation.SetActive(visible);
        }

        private void SelectPanelTab(int selected)
        {
            SetClearConfirmation(false);
            _selectedTab = selected;
            for (int i = 0; i < _tabPages.Length; i++)
            {
                _tabPages[i].SetActive(i == selected);
                _tabIndicators[i].SetActive(i == selected);
            }
            UpdateTabSurfaceColors();
            _nextPanelStatusRefresh = 0f;
        }

        private void UpdateTabSurfaceColors()
        {
            for (int i = 0; i < _tabButtons.Length; i++)
                if (_tabButtons[i] != null)
                    _tabButtons[i].image.color = i == _selectedTab ? _panelPalette.inset : _panelPalette.inactiveTab;
        }

        private void ConfigurePanelLifecycle()
        {
            _panelLifecycle = GetComponent<XRResearcherPanelLifecycle>();
            if (_panelLifecycle == null) _panelLifecycle = gameObject.AddComponent<XRResearcherPanelLifecycle>();
            _panelLifecycle.Initialize(_worldCanvasObject.transform, PositionWorldPanel);
            _panelFollow = GetComponent<XRPanelFollow>();
            if (_panelFollow == null) _panelFollow = gameObject.AddComponent<XRPanelFollow>();
            _panelFollow.Initialize(_worldCanvasObject.transform);
            _panelFollow.ConfigureRadius(_panelHorizontalRadius);
            _panelFollow.ConfigureTuning(PanelAngularDeadZone, PanelCatchUpSeconds);
            _moveHandle = _worldCanvasObject.GetComponentInChildren<XRPanelMoveHandle>(true);
            if (_moveHandle != null)
            {
                _moveHandle.ConfigureRadial(_panelFollow, () => TryGetWorldHeadPose(out Pose h) ? (Pose?)h : null);
                _moveHandle.SetOutlineColor(_panelPalette.dragOutline);
            }
            TryApplyPanelSettings();
            _panelLifecycle.Hidden -= CancelPendingPanelActions;
            _panelLifecycle.Hidden += CancelPendingPanelActions;
            var launcher = GetComponent<XRBraceletLauncher>();
            if (launcher == null) launcher = gameObject.AddComponent<XRBraceletLauncher>();
            launcher.Initialize(_panelLifecycle);
        }

        private void CancelPendingPanelActions()
        {
            _panelPresses.Clear();
            SetClearConfirmation(false);
            _pendingScenarioToLoad = _pendingPresetToApply = _pendingLayout = -1;
            _pendingToggleDisplayMode = _pendingTogglePause = _pendingResetScenario = false;
            _pendingRemoveAllAgents = _pendingCenterCamera = _pendingToggleFpsLook = false;
            _pendingToggleAgentIds = _pendingToggleRecording = false;
            _pendingToggleReferenceVisible = _pendingToggleReferenceLocked = false;
            _pendingPlaceReference = _pendingResetReference = _pendingRotateReference = false;
            _pendingSetScenarioCenter = false;
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

            // Images use the existing world UI shader. TMP labels have their own SDF material.
            Material material = Resources.Load<Material>("Mat_SpatialMAS_WorldUI");
            if (material != null)
                foreach (var graphic in _worldCanvasObject.GetComponentsInChildren<Graphic>(true))
                    if (graphic is Image) graphic.material = material;
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

        private static TextMeshProUGUI CreateWorldText(Transform parent, string label, int fontSize)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            text.fontSharedMaterial = Resources.Load<Material>("Mat_SpatialMAS_WorldUISdf");
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.fontSize = fontSize;
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject CreatePanelGroup(Transform parent, string name)
        {
            var group = new GameObject(name, typeof(RectTransform));
            group.transform.SetParent(parent, false);
            var rect = (RectTransform)group.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return group;
        }

        private TextMeshProUGUI CreatePanelLabel(Transform parent, string label, Rect bounds, int size,
            System.Func<ResearcherPanelPalette, Color> color)
        {
            var text = CreateWorldText(parent, label, size);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            BindPanelColor(text, color);
            SetTopLeftRect(text.rectTransform, bounds);
            return text;
        }

        private Image CreatePanelImage(Transform parent, string name, Rect bounds,
            System.Func<ResearcherPanelPalette, Color> color, bool hitTarget = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            SetTopLeftRect((RectTransform)go.transform, bounds);
            var image = go.GetComponent<Image>();
            BindPanelColor(image, color);
            image.raycastTarget = hitTarget;
            return image;
        }

        private Image CreateSelectionMark(Button button)
        {
            var rect = (RectTransform)button.transform;
            return CreatePanelImage(rect, "Selected underline",
                new Rect(12, rect.sizeDelta.y - 5, rect.sizeDelta.x - 24, 3), palette => palette.selectionMarker);
        }

        private Button CreateWorldButton(Transform parent, string label, Rect bounds, UnityAction action,
            System.Func<ResearcherPanelPalette, Color> textColor = null, bool tab = false)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            SetTopLeftRect(go.GetComponent<RectTransform>(), bounds);
            var image = go.GetComponent<Image>();
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            var text = CreateWorldText(go.transform, label, 19);
            BindPanelColor(text, textColor ?? (palette => palette.primaryText));
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            System.Action<bool> setHover;
            if (tab)
            {
                // Leave the shared bottom edge open, including on hover.
                button.transition = Selectable.Transition.None;
                var outline = CreatePanelGroup(go.transform, "Tab hover outline");
                CreatePanelImage(outline.transform, "Top", new Rect(0, 0, bounds.width, 2), palette => palette.hoverOutline);
                CreatePanelImage(outline.transform, "Left", new Rect(0, 2, 2, bounds.height - 2), palette => palette.hoverOutline);
                CreatePanelImage(outline.transform, "Right", new Rect(bounds.width - 2, 2, 2, bounds.height - 2), palette => palette.hoverOutline);
                outline.SetActive(false);
                setHover = outline.SetActive;
            }
            else
            {
                BindPanelColor(image, palette => palette.button);
                void ApplyButtonTints(ResearcherPanelPalette palette)
                {
                    var colors = button.colors;
                    colors.highlightedColor = palette.buttonHoverTint;
                    colors.pressedColor = palette.buttonPressedTint;
                    colors.selectedColor = palette.buttonSelectedTint;
                    colors.disabledColor = palette.buttonDisabledTint;
                    button.colors = colors;
                }
                ApplyButtonTints(_panelPalette);
                _paletteBindings.Add(ApplyButtonTints);
                var outline = go.AddComponent<Outline>();
                outline.effectColor = _panelPalette.hoverOutline;
                _paletteBindings.Add(palette => outline.effectColor = palette.hoverOutline);
                outline.effectDistance = new Vector2(1.5f, 1.5f);
                outline.enabled = false;
                setHover = value => outline.enabled = value;
            }
            var events = go.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => setHover(button.IsInteractable()));
            events.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => setHover(false));
            events.triggers.Add(exit);
            return button;
        }

        private void PositionWorldPanel()
        {
            if (_worldCanvasObject == null) return;
            if (TryGetWorldHeadPose(out Pose trackedPose))
            {
                PlaceWorldPanel(trackedPose);
                _hasTrackedPanelPlacement = true;
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
            if (_panelFollow != null)
            {
                _panelFollow.Recall(head);
                _worldPanelNeedsTrackedPlacement = false;
                return;
            }
            Vector3 forward = Vector3.ProjectOnPlane(head.rotation * Vector3.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
            Vector3 position = head.position + forward * 1.25f - Vector3.up * 0.1f;
            _worldCanvasObject.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
            _worldPanelNeedsTrackedPlacement = false;
        }

        private void LateUpdate()
        {
            TryApplyPanelSettings();
            UpdateWorldPanelTracking(IsHeadsetActive);
            if (_panelFollow == null) return;
            bool tracked = TryGetWorldHeadPose(out Pose head);
            bool dragging = _moveHandle != null && _moveHandle.IsDragging;
            if (_wasPanelDragging && !dragging && tracked) _panelFollow.CommitPlacement(head);
            _wasPanelDragging = dragging;
            bool frozen = dragging || PanelHasPointerIntent() || _panelLifecycle == null || !_panelLifecycle.Visible || !_panelLifecycle.HeadsetActive;
            bool followTracking = tracked && _panelLifecycle != null && _panelLifecycle.Visible && _panelLifecycle.HeadsetActive;
            _panelFollow.Step(head, followTracking, frozen, Time.unscaledDeltaTime, dragging);
        }

        private void TogglePanelFollow()
        {
            if (_panelFollow == null || !TryGetWorldHeadPose(out Pose head)) return;
            _panelFollow.SetPinned(!_panelFollow.Pinned, head);
            if (_followButton != null) _followButton.GetComponentInChildren<TextMeshProUGUI>().text = _panelFollow.Pinned ? "Follow user" : "Pin in room";
        }

        private bool PanelHasPointerIntent()
        {
            if (_worldCanvasObject == null || !_worldCanvasObject.activeInHierarchy) return false;
            if (_panelPointers == null || Time.unscaledTime >= _nextPointerRefresh)
            {
                _panelPointers = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>(FindObjectsSortMode.None);
                _nextPointerRefresh = Time.unscaledTime + 1f;
            }
            var rect = (RectTransform)_worldCanvasObject.transform;
            foreach (var pointer in _panelPointers)
            {
                if (pointer == null || !pointer.isActiveAndEnabled || !pointer.TryGetUIModel(out var model)) continue;
                if (!model.select) { _panelPresses.Remove(model.pointerId); continue; }
                else if (_panelPresses.Contains(model.pointerId)) return true;
                var target = model.currentRaycast.gameObject;
                if (target == null || !target.transform.IsChildOf(rect)) continue;
                var points = model.raycastPoints;
                if (points == null || points.Count < 2) continue;
                Vector3 direction = points[1] - points[0];
                if (direction.sqrMagnitude < .000001f) continue;
                var ray = new Ray(points[0], direction.normalized);
                if (!new Plane(rect.forward, rect.position).Raycast(ray, out float distance)) continue;
                Vector3 local = rect.InverseTransformPoint(ray.GetPoint(distance));
                if (rect.rect.Contains(local))
                {
                    if (model.select) _panelPresses.Add(model.pointerId);
                    return true;
                }
            }
            return false;
        }

        private void UpdateWorldPanelTracking(bool headsetActive)
        {
            if (_worldCanvasObject == null) return;
            if (!headsetActive)
            {
                _worldPanelWasInXr = false;
                return;
            }
            if (!_worldPanelWasInXr || (_worldCanvas != null && _worldCanvas.worldCamera != Camera.main))
            {
                ConfigureWorldCanvas();
                _worldPanelNeedsTrackedPlacement = !_hasTrackedPanelPlacement;
            }
            _worldPanelWasInXr = true;
            if (_worldPanelNeedsTrackedPlacement && TryGetWorldHeadPose(out Pose trackedPose))
            {
                PlaceWorldPanel(trackedPose);
                _hasTrackedPanelPlacement = true;
            }
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
            if (_panelLifecycle != null) _panelLifecycle.SetHeadsetActive(IsHeadsetActive);
            UpdateMoveHint();
            ProcessDeferredUiActions();
            HandleKeyboardShortcuts();
            UpdateWorldCanvasText();
        }

        private void UpdateMoveHint()
        {
            if (_moveHint == null || _panelLifecycle == null) return;
            bool visible = _panelLifecycle.HeadsetActive && _panelLifecycle.Visible;
            if (visible && !_moveHintShown)
            {
                _moveHintShown = true;
                _moveHintUntil = Time.unscaledTime + 5f;
            }
            _moveHint.gameObject.SetActive(visible && Time.unscaledTime < _moveHintUntil);
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
            if (_panelLifecycle != null && !_panelLifecycle.ResearcherMode) return;
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
            if (_worldHudStatusText == null || Time.unscaledTime < _nextPanelStatusRefresh) return;
            _nextPanelStatusRefresh = Time.unscaledTime + .2f;
            int active = _scenarioManager?.ActiveScenarioId ?? 0;
            bool paused = _scenarioManager?.IsPaused ?? false;
            bool recording = _recorder?.IsRecording ?? false;
            _worldHudStatusText.text = _scenarioManager?.ActiveScenarioName ?? "External OSC";
            _runStatus.text = paused ? "Paused" : "Running";
            _runStatus.color = paused ? _panelPalette.paused : _panelPalette.running;
            _recordStatus.text = recording ? "Recording" : "Not recording";
            _recordStatus.color = recording ? _panelPalette.recording : _panelPalette.notRecording;
            _recordDot.gameObject.SetActive(recording);
            _pauseLabel.text = paused ? "Resume" : "Pause";
            _recordLabel.text = recording ? "Stop recording" : "Start recording";
            for (int i = 0; i < _scenarioIndicators.Length; i++)
                _scenarioIndicators[i].SetActive(i == active);
            string[] presets = active == 2 ? SwarmPresetNames : DefaultPresetNames;
            for (int i = 0; i < _worldPresetButtons.Length; i++)
            {
                _worldPresetButtons[i].gameObject.SetActive(i < presets.Length);
                if (i < presets.Length) _presetLabels[i].text = presets[i];
            }

            _layoutStatus.text = _scenarioManager == null ? "Next load layout · unavailable" :
                $"Active layout · {_scenarioManager.ActiveLayout}\nNext load layout · {_scenarioManager.NextLayout}";
            _calibrationStatus.text = _calibration == null ? "Calibration reference · unavailable" :
                $"Calibration reference · {(_calibration.Visible ? "Visible" : "Hidden")} / {(_calibration.Locked ? "Locked" : "Unlocked")}";
            _displayStatus.text = $"{_modeSwitcher?.DisplayStatus ?? "MR preview"}  ·  Agent IDs {(_agentManager?.ShowAgentIds == true ? "on" : "off")}";
            string session = _recorder?.SessionId;
            string sessionLabel = string.IsNullOrEmpty(session) ? "No recording session" :
                $"{(recording ? "Current" : "Last")} session · {session}";
            _sessionStatus.text = $"{sessionLabel}\nLog download address\n{_wifiServer?.DashboardUrl ?? "Unavailable"}";
            if (_diagnostics.activeInHierarchy)
            {
                string osc = _oscServer == null ? "OSC unavailable" : $"OSC UDP {_oscServer.ListenPort} · {_oscServer.TotalMessagesReceived} received";
                _diagnosticsStatus.text = $"{_platformAdapter?.ActiveDeviceName ?? "XR/Desktop"} · {_smoothedFps:F0} FPS\n{osc}\nAgents · {_agentManager?.ActiveCount ?? 0}";
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            if (_panelLifecycle != null && !_panelLifecycle.ResearcherMode) return;
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
