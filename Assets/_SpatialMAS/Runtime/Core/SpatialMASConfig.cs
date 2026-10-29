using System;
using System.IO;
using UnityEngine;

namespace SpatialMAS.Core
{
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

        [Header("Spatial Coordinate Frame & Scale")]
        [Tooltip("Multiplier applied after millimeter-to-meter (0.001) conversion. 1.0 = Room-Scale, 0.20 = Tabletop/Lab Arena Scale.")]
        [Range(0.05f, 5.0f)]
        public float worldScale = 0.20f;

        [Tooltip("Default center of the simulation arena in Unity world space (meters). Chest height (1.2m) in front of user (0.8m).")]
        public Vector3 originOffset = new Vector3(0f, 1.2f, 0.8f);

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
            public float worldScale = 0.20f;
            public float originX = 0f;
            public float originY = 1.2f;
            public float originZ = 0.8f;
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

                string json = File.ReadAllText(path);
                JsonConfigData data = JsonUtility.FromJson<JsonConfigData>(json);
                if (data == null) return;

                oscListenPort = data.oscListenPort > 0 ? data.oscListenPort : oscListenPort;
                if (!string.IsNullOrWhiteSpace(data.defaultRemoteIp)) defaultRemoteIp = data.defaultRemoteIp;
                defaultRemotePort = data.defaultRemotePort > 0 ? data.defaultRemotePort : defaultRemotePort;
                httpLogServerPort = data.httpLogServerPort > 0 ? data.httpLogServerPort : httpLogServerPort;
                autoStartRecording = data.autoStartRecording;
                worldScale = Mathf.Clamp(data.worldScale, 0.05f, 5.0f);
                originOffset = new Vector3(data.originX, data.originY, data.originZ);
                defaultXRMode = (XRDisplayMode)Mathf.Clamp(data.defaultXRMode, 0, 1);
                lockAudioListenerToHead = data.lockAudioListenerToHead;
                streamUserPoseOverOsc = data.streamUserPoseOverOsc;
                startupScenarioIndex = Mathf.Clamp(data.startupScenarioIndex, 0, 5);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpatialMASConfig] Failed to load JSON config override: {ex.Message}");
            }
        }

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
                    worldScale = worldScale,
                    originX = originOffset.x,
                    originY = originOffset.y,
                    originZ = originOffset.z,
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
