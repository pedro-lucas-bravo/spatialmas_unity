using SpatialMAS.Agents;
using SpatialMAS.Audio;
using SpatialMAS.Environment;
using SpatialMAS.LocalComm;
using SpatialMAS.Logging;
using SpatialMAS.Networking;
using SpatialMAS.Scenarios;
using SpatialMAS.UI;
using SpatialMAS.XR;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace SpatialMAS.Core
{
    /// <summary>
    /// Master runtime bootstrapper and dependency composition root for SpatialMAS.
    /// Initializes and wires all networking, coordinate mapping, pooled world managers,
    /// multiplatform XR/Desktop interaction rigs, built-in scenarios, CSV telemetry logging,
    /// and the Wi-Fi HTTP log export server.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class SpatialMASBootstrapper : MonoBehaviour
    {
        [SerializeField] private SpatialMASConfig _config;
        [SerializeField] private Camera _mainCamera;
        [SerializeField] private ARSession _arSession;
        [SerializeField] private ARCameraManager _arCameraManager;
        [SerializeField] private Material _agentMaterial;
        [SerializeField] private Material _boundaryMaterial;

        public void ConfigureReferences(
            SpatialMASConfig config,
            Camera mainCamera,
            ARSession arSession,
            ARCameraManager arCameraManager,
            Material agentMaterial,
            Material boundaryMaterial)
        {
            _config = config;
            _mainCamera = mainCamera;
            _arSession = arSession;
            _arCameraManager = arCameraManager;
            _agentMaterial = agentMaterial;
            _boundaryMaterial = boundaryMaterial;
        }

        public SpatialMASConfig Config => _config;
        public SimulationCommandBus CommandBus { get; private set; }
        public SpatialCoordinateMapper CoordinateMapper { get; private set; }
        public SpatialCalibrationReference CalibrationReference { get; private set; }
        public OscUdpServer OscServer { get; private set; }
        public OscUdpClient OscClient { get; private set; }
        public OscProtocolRouter OscRouter { get; private set; }

        public AgentManager AgentManager { get; private set; }
        public BoundaryManager BoundaryManager { get; private set; }
        public EnvironmentArrowManager ArrowManager { get; private set; }
        public LocalCommManager LocalCommManager { get; private set; }
        public AudioListenerProxy ListenerProxy { get; private set; }

        public XRPlatformAdapter PlatformAdapter { get; private set; }
        public XRModeSwitcher ModeSwitcher { get; private set; }
        public XRUserEmbodimentBridge EmbodimentBridge { get; private set; }
        public DesktopInteractionRig DesktopRig { get; private set; }

        public ScenarioManager ScenarioManager { get; private set; }
        public DataRecordingService RecordingService { get; private set; }
        public EmbodimentTelemetrySampler TelemetrySampler { get; private set; }
        public WifiLogExportServer WifiLogServer { get; private set; }
        public SpatialResearcherHUD ResearcherHud { get; private set; }

        private void Awake()
        {
            if (_config == null)
            {
                _config = Resources.Load<SpatialMASConfig>("DefaultSpatialMASConfig");
            }
            if (_config == null)
            {
                _config = ScriptableObject.CreateInstance<SpatialMASConfig>();
            }
            _config.LoadJsonOverridesIfPresent();

            if (_mainCamera == null) _mainCamera = Camera.main;

            // Pre-warm all procedural and primitive meshes during Awake so zero GPU mesh allocations occur mid-frame
            AgentEntity.WarmupPrimitiveMeshes();

            // 1. Core Bus & Coordinate Mapper
            CommandBus = new SimulationCommandBus();
            CoordinateMapper = new SpatialCoordinateMapper();
            CalibrationReference = GetOrAddChildComponent<SpatialCalibrationReference>("CalibrationReference");
            CalibrationReference.Initialize(CommandBus, _config.calibrationPosition, _config.calibrationRotation,
                _config.calibrationVisible, _config.calibrationLocked);

            // 2. Multiplatform XR Adapter & Mode Switcher
            PlatformAdapter = GetOrAddComponent<XRPlatformAdapter>(gameObject);
            PlatformAdapter.Initialize(_config.targetRefreshRate);

            if (GetComponent<XRHandTrackingProcessor>() == null)
            {
                gameObject.AddComponent<XRHandTrackingProcessor>();
            }

            ModeSwitcher = GetOrAddComponent<XRModeSwitcher>(gameObject);
            ModeSwitcher.Initialize(
                CommandBus,
                _mainCamera,
                _arSession,
                _arCameraManager,
                null,
                _config.defaultXRMode);

            // 3. XR Embodiment & Desktop Rig
            EmbodimentBridge = GetOrAddComponent<XRUserEmbodimentBridge>(gameObject);
            var pinchReaders = FindObjectsByType<XRHandPinchHysteresis>(FindObjectsInactive.Include);
            XRHandPinchHysteresis leftPinch = null;
            XRHandPinchHysteresis rightPinch = null;
            for (int i = 0; i < pinchReaders.Length; i++)
            {
                if (pinchReaders[i].Handedness == UnityEngine.XR.Hands.Handedness.Left) leftPinch = pinchReaders[i];
                else rightPinch = pinchReaders[i];
            }
            EmbodimentBridge.Initialize(CommandBus, _mainCamera != null ? _mainCamera.transform : transform, leftPinch, rightPinch);
            EmbodimentBridge.UserRepellerEnabled = _config.enableUserRepellerField;
            EmbodimentBridge.UserRepellerRadiusMeters = _config.userRepellerRadiusMeters;
            EmbodimentBridge.UserRepellerForce = _config.userRepellerForce;

            DesktopRig = GetOrAddComponent<DesktopInteractionRig>(gameObject);
            DesktopRig.Initialize(_mainCamera, CommandBus);

            // 4. Pooled World Entities & 3D Audio Listener Proxy
            AgentManager = GetOrAddChildComponent<AgentManager>("AgentManager");
            AgentManager.Initialize(CommandBus, _agentMaterial, _boundaryMaterial);

            BoundaryManager = GetOrAddChildComponent<BoundaryManager>("BoundaryManager");
            BoundaryManager.Initialize(CommandBus, AgentManager, _boundaryMaterial);

            ArrowManager = GetOrAddChildComponent<EnvironmentArrowManager>("EnvironmentArrowManager");
            ArrowManager.Initialize(CommandBus);

            LocalCommManager = GetOrAddChildComponent<LocalCommManager>("LocalCommManager");
            LocalCommManager.Initialize(CommandBus, AgentManager);

            ListenerProxy = GetOrAddChildComponent<AudioListenerProxy>("AudioListenerProxy");
            ListenerProxy.Initialize(CommandBus, _mainCamera != null ? _mainCamera.transform : transform, _config.lockAudioListenerToHead);

            // 5. OSC v1 & v2 UDP Networking
            OscServer = new OscUdpServer();
            OscServer.StartServer(_config.oscListenPort, _config.udpReceiveBufferSize);

            OscClient = new OscUdpClient(_config.defaultRemoteIp, _config.defaultRemotePort);
            OscRouter = new OscProtocolRouter(CommandBus, CoordinateMapper, OscServer, OscClient)
            {
                StreamUserPoseEnabled = _config.streamUserPoseOverOsc
            };

            // 6. Data Logging & Wi-Fi HTTP Export Server
            RecordingService = new DataRecordingService(CommandBus, calibrationReference: CalibrationReference, config: _config);
            if (_config.autoStartRecording)
            {
                RecordingService.StartRecording();
            }

            WifiLogServer = new WifiLogExportServer(RecordingService);
            WifiLogServer.StartServer(_config.httpLogServerPort);

            // 7. Scenario Manager (Built-In Scenarios 0..5)
            ScenarioManager = GetOrAddChildComponent<ScenarioManager>("ScenarioManager");
            ScenarioManager.Initialize(CommandBus, _config.scenarioCenter, AgentManager, EmbodimentBridge,
                _config.startupScenarioIndex, _config.scenarioLayout);

            TelemetrySampler = GetOrAddChildComponent<EmbodimentTelemetrySampler>("EmbodimentTelemetrySampler");
            TelemetrySampler.Initialize(
                RecordingService,
                EmbodimentBridge,
                ModeSwitcher,
                ScenarioManager,
                AgentManager,
                CalibrationReference,
                _config.telemetrySampleRateHz,
                PlatformAdapter,
                _mainCamera,
                BoundaryManager,
                ArrowManager);

            // 8. Spatial Researcher HUD
            ResearcherHud = GetOrAddChildComponent<SpatialResearcherHUD>("SpatialResearcherHUD");
            ResearcherHud.Initialize(
                CommandBus,
                CoordinateMapper,
                ScenarioManager,
                ModeSwitcher,
                PlatformAdapter,
                AgentManager,
                OscServer,
                OscClient,
                RecordingService,
                WifiLogServer,
                DesktopRig,
                CalibrationReference);
        }

        private void Update()
        {
            OscRouter?.ProcessIncomingMessages();
        }

        private void OnDestroy()
        {
            WifiLogServer?.Dispose();
            RecordingService?.Dispose();
            OscRouter?.Dispose();
            OscServer?.Dispose();
            OscClient?.Dispose();
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T comp = target.GetComponent<T>();
            return comp != null ? comp : target.AddComponent<T>();
        }

        private T GetOrAddChildComponent<T>(string childName) where T : Component
        {
            Transform existing = transform.Find(childName);
            GameObject childGo = existing != null ? existing.gameObject : new GameObject(childName);
            childGo.transform.SetParent(transform, false);
            return GetOrAddComponent<T>(childGo);
        }
    }
}
