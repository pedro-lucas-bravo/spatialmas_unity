using System;
using System.IO;
using UnityEngine;

namespace SpatialMAS.Core
{
    public enum ScenarioLayout { Compact = 0, RoomScale = 1 }

    public enum AgentShapeType
    {
        Sphere = 0,
        Cube = 1,
        Pyramid = 2,
        Custom = 3
    }

    public enum AgentMovementMode
    {
        Teleport = 0,
        DirectVelocity = 1,
        Follow = 1,
        Velocity = 2
    }

    public enum XRDisplayMode
    {
        MRPassthrough = 0,
        VRImmersive = 1
    }

    public enum XRHardwarePlatform
    {
        DesktopStandalone = 0,
        MetaQuestOpenXR = 1,
        AppleVisionPro = 2,
        GenericOpenXR = 3
    }

    /// <summary>
    /// Persistent runtime and editor configuration for SpatialMAS.
    /// Supports both ScriptableObject Inspector defaults and JSON overrides in Application.persistentDataPath.
    /// </summary>
    [CreateAssetMenu(fileName = "SpatialMASConfig", menuName = "SpatialMAS/Configuration", order = 1)]
    public class SpatialMASConfig : ScriptableObject
    {
        [Header("Network & OSC Configuration")]
        [Tooltip("Local UDP port where SpatialMAS listens for incoming OSC v1 & v2 messages.")]
        public int oscListenPort = 6011;

        [Tooltip("Default remote IP address for outgoing OSC messages (overridden automatically when /test/alive or /agents/instantiate arrives).")]
        public string defaultRemoteIp = "127.0.0.1";

        [Tooltip("Default remote UDP port for outgoing OSC messages.")]
        public int defaultRemotePort = 6012;

        [Tooltip("UDP socket receive buffer size in bytes (1 MB prevents packet drops at 250+ agents).")]
        public int udpReceiveBufferSize = 1048576;

        [Header("Wi-Fi HTTP & UDP Log Export Server")]
        [Tooltip("Local TCP port for the embedded Researcher Web Dashboard (http://<headset-ip>:8080/).")]
        public int httpLogServerPort = 8080;

        [Tooltip("Automatically start recording OSC & XR Embodiment CSV logs on launch.")]
        public bool autoStartRecording = true;

        [Tooltip("Sampling rate (Hz) for XR Embodiment Telemetry (head 6DoF, hand poses, pinches, frametime).")]
        [Range(10f, 90f)]
        public float telemetrySampleRateHz = 50f;

        [Header("Physical Scenario Placement")]
        [Tooltip("Spawn center for the next built-in scenario, in Unity-world meters. Does not redefine the coordinate frame.")]
        public Vector3 scenarioCenter = new Vector3(0f, 1.2f, 0.8f);
        public ScenarioLayout scenarioLayout = ScenarioLayout.Compact;

        [Header("External Calibration Reference (Unity World)")]
        public Vector3 calibrationPosition = Vector3.zero;
        public Quaternion calibrationRotation = Quaternion.identity;
        public bool calibrationVisible = true;
        public bool calibrationLocked = true;

        [Header("XR & Multiplatform Display")]
        [Tooltip("Default display mode on startup (0 = MR Passthrough, 1 = VR Immersive Studio).")]
        public XRDisplayMode defaultXRMode = XRDisplayMode.MRPassthrough;

        [Tooltip("Target display refresh rate in Hz (90 Hz recommended for Quest 3 and Vision Pro).")]
        public float targetRefreshRate = 90f;

        [Tooltip("Lock the 3D AudioListener automatically to the user's physical headset (MainCamera) 6DoF pose.")]
        public bool lockAudioListenerToHead = true;

        [Tooltip("Stream the user's physical head & hand poses out via OSC (/xr/user/pose and /xr/hands/pose).")]
        public bool streamUserPoseOverOsc = true;

        [Tooltip("Allow the user's physical body/head to act as a live repulsive social-force field on autonomous agents.")]
        public bool enableUserRepellerField = true;

        [Tooltip("Personal space repulsion radius around the XR user in meters.")]
        public float userRepellerRadiusMeters = 0.65f;

        [Tooltip("Repulsive force multiplier exerted by the XR user on nearby autonomous agents.")]
        public float userRepellerForce = 1.5f;

        [Header("Default Scenario")]
        [Tooltip("0=External OSC, 1=Crowd Avoidance, 2=3D Swarmalators, 3=Attractors & Repellers, 4=Musical Encounters, 5=Self-Assembly.")]
        [Range(0, 5)]
        public int startupScenarioIndex = 0;

        [Serializable]
        private class JsonConfigData
        {
            public int oscListenPort = 6011;
            public string defaultRemoteIp = "127.0.0.1";
            public int defaultRemotePort = 6012;
            public int httpLogServerPort = 8080;
            public bool autoStartRecording = true;
            public Vector3 scenarioCenter = new Vector3(0f, 1.2f, 0.8f);
            public int scenarioLayout = 0;
            public Vector3 calibrationPosition = Vector3.zero;
            public Quaternion calibrationRotation = Quaternion.identity;
            public bool calibrationVisible = true;
            public bool calibrationLocked = true;
            public int defaultXRMode = 0;
            public bool lockAudioListenerToHead = true;
            public bool streamUserPoseOverOsc = true;
            public int startupScenarioIndex = 0;
        }

        public static string GetJsonConfigPath()
        {
            return Path.Combine(Application.persistentDataPath, "spatialmas_config.json");
        }

        public void LoadJsonOverridesIfPresent()
        {
            try
            {
                string path = GetJsonConfigPath();
                if (!File.Exists(path)) return;

                ApplyJsonOverrides(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpatialMASConfig] Failed to load JSON config override: {ex.Message}");
            }
        }

        public void ApplyJsonOverrides(string json)
        {
            // Start with the actual asset defaults so a partial or legacy JSON cannot zero new fields.
            var data = new JsonConfigData
            {
                oscListenPort = oscListenPort, defaultRemoteIp = defaultRemoteIp,
                defaultRemotePort = defaultRemotePort, httpLogServerPort = httpLogServerPort,
                autoStartRecording = autoStartRecording, scenarioCenter = scenarioCenter,
                scenarioLayout = (int)scenarioLayout, calibrationPosition = calibrationPosition,
                calibrationRotation = calibrationRotation, calibrationVisible = calibrationVisible,
                calibrationLocked = calibrationLocked, defaultXRMode = (int)defaultXRMode,
                lockAudioListenerToHead = lockAudioListenerToHead, streamUserPoseOverOsc = streamUserPoseOverOsc,
                startupScenarioIndex = startupScenarioIndex
            };
            JsonUtility.FromJsonOverwrite(json, data);
            // Legacy worldScale/originX/Y/Z are ignored; reference pose never changes world coordinates.
            if (!Finite(data.scenarioCenter) || !Finite(data.calibrationPosition) ||
                !Finite(new Vector3(data.calibrationRotation.x, data.calibrationRotation.y, data.calibrationRotation.z)) ||
                float.IsNaN(data.calibrationRotation.w) || float.IsInfinity(data.calibrationRotation.w) ||
                Quaternion.Dot(data.calibrationRotation, data.calibrationRotation) < 0.000001f)
                throw new ArgumentException("Scenario/reference poses must be finite and rotation must be nonzero.");
            oscListenPort = data.oscListenPort > 0 ? data.oscListenPort : oscListenPort;
            if (!string.IsNullOrWhiteSpace(data.defaultRemoteIp)) defaultRemoteIp = data.defaultRemoteIp;
            defaultRemotePort = data.defaultRemotePort > 0 ? data.defaultRemotePort : defaultRemotePort;
            httpLogServerPort = data.httpLogServerPort > 0 ? data.httpLogServerPort : httpLogServerPort;
            autoStartRecording = data.autoStartRecording;
            scenarioCenter = data.scenarioCenter;
            scenarioLayout = (ScenarioLayout)Mathf.Clamp(data.scenarioLayout, 0, 1);
            calibrationPosition = data.calibrationPosition;
            calibrationRotation = data.calibrationRotation.normalized;
            calibrationVisible = data.calibrationVisible;
            calibrationLocked = data.calibrationLocked;
            defaultXRMode = (XRDisplayMode)Mathf.Clamp(data.defaultXRMode, 0, 1);
            lockAudioListenerToHead = data.lockAudioListenerToHead;
            streamUserPoseOverOsc = data.streamUserPoseOverOsc;
            startupScenarioIndex = Mathf.Clamp(data.startupScenarioIndex, 0, 5);
        }

        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        public void SaveJsonOverrides()
        {
            try
            {
                var data = new JsonConfigData
                {
                    oscListenPort = oscListenPort,
                    defaultRemoteIp = defaultRemoteIp,
                    defaultRemotePort = defaultRemotePort,
                    httpLogServerPort = httpLogServerPort,
                    autoStartRecording = autoStartRecording,
                    scenarioCenter = scenarioCenter,
                    scenarioLayout = (int)scenarioLayout,
                    calibrationPosition = calibrationPosition,
                    calibrationRotation = calibrationRotation,
                    calibrationVisible = calibrationVisible,
                    calibrationLocked = calibrationLocked,
                    defaultXRMode = (int)defaultXRMode,
                    lockAudioListenerToHead = lockAudioListenerToHead,
                    streamUserPoseOverOsc = streamUserPoseOverOsc,
                    startupScenarioIndex = startupScenarioIndex
                };
                File.WriteAllText(GetJsonConfigPath(), JsonUtility.ToJson(data, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpatialMASConfig] Failed to save JSON config: {ex.Message}");
            }
        }
    }
}
