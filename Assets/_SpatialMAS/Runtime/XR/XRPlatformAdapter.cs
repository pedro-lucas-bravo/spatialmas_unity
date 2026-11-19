using System.Collections;
using System.Collections.Generic;
using SpatialMAS.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;
using Unity.XR.CoreUtils;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

#if UNITY_ANDROID && !UNITY_EDITOR && SPATIALMAS_META_OPENXR
using Unity.Collections;
using UnityEngine.XR.OpenXR.Features.Meta;
#endif

namespace SpatialMAS.XR
{
    /// <summary>
    /// Vendor-neutral multiplatform XR hardware abstraction layer.
    /// Automatically detects Meta Quest 3 (Android OpenXR), Apple Vision Pro (visionOS),
    /// Generic OpenXR headsets (Pico, Android XR, HTC, PCVR), or Desktop Standalone (Windows/macOS/Linux),
    /// configuring frame pacing, refresh rates, and input polling without vendor lock-in.
    /// </summary>
    public class XRPlatformAdapter : MonoBehaviour
    {
        [SerializeField] private float _targetRefreshRate = 90.0f;
        private readonly List<XRDisplaySubsystem> _displays = new List<XRDisplaySubsystem>();

        public XRHardwarePlatform ActivePlatform { get; private set; } = XRHardwarePlatform.DesktopStandalone;
        public string ActiveDeviceName { get; private set; } = "Desktop Standalone";
        public bool IsXRHeadsetActive { get; private set; }
        private bool _hasStartedXrTracking;
        private XROrigin _roomOrigin;
        private ARAnchorManager _roomAnchorManager;
        private ARAnchor _roomAnchor;
        private Pose? _roomAnchorTarget;
        private bool _addingRoomAnchor;
        private float _anchorRetryAt;

        public void Initialize(float targetRefreshRate = 90.0f)
        {
            _targetRefreshRate = Mathf.Clamp(targetRefreshRate, 60f, 120f);
            Application.runInBackground = true;

            XRInteractionRigSetup.ConfigureHeadTracking(Camera.main);
            XRInteractionRigSetup.EnsureHandInteractors(Camera.main);

            DetectPlatform();
            ConfigureFramePacing();
        }

        private void DetectPlatform()
        {
            string model = SystemInfo.deviceModel ?? string.Empty;
            SubsystemManager.GetSubsystems(_displays);
            IsXRHeadsetActive = _displays.Exists(display => display != null && display.running);

#if UNITY_VISIONOS
            ActivePlatform = XRHardwarePlatform.AppleVisionPro;
            ActiveDeviceName = "Apple Vision Pro (visionOS)";
            IsXRHeadsetActive = true;
#elif UNITY_ANDROID && !UNITY_EDITOR
            if (model.IndexOf("Quest", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                model.IndexOf("Oculus", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                model.IndexOf("Meta", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ActivePlatform = XRHardwarePlatform.MetaQuestOpenXR;
                ActiveDeviceName = $"Meta Quest ({model})";
            }
            else
            {
                ActivePlatform = XRHardwarePlatform.GenericOpenXR;
                ActiveDeviceName = $"Android OpenXR ({model})";
            }
            IsXRHeadsetActive = true;
#else
            if (IsXRHeadsetActive)
            {
                if (model.IndexOf("Vision", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ActivePlatform = XRHardwarePlatform.AppleVisionPro;
                    ActiveDeviceName = "Apple Vision Pro";
                }
                else if ((OpenXRRuntime.name ?? string.Empty).IndexOf("Oculus", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                         (OpenXRRuntime.name ?? string.Empty).IndexOf("Meta", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ActivePlatform = XRHardwarePlatform.MetaQuestOpenXR;
                    ActiveDeviceName = "Meta Quest (Link / OpenXR)";
                }
                else
                {
                    ActivePlatform = XRHardwarePlatform.GenericOpenXR;
                    ActiveDeviceName = $"OpenXR Headset ({model})";
                }
            }
            else
            {
                ActivePlatform = XRHardwarePlatform.DesktopStandalone;
                ActiveDeviceName = $"Desktop ({SystemInfo.operatingSystem})";
            }
#endif
            if (Camera.main != null)
            {
                var tpd = Camera.main.GetComponent<TrackedPoseDriver>();
                if (tpd != null)
                {
                    _hasStartedXrTracking |= IsXRHeadsetActive;
                    tpd.enabled = _hasStartedXrTracking;
                }
            }
        }

        private void Update()
        {
            // Link can finish initializing after Awake and the first Start frame.
            // Keep desktop controls, tracked pose input, and telemetry source in sync.
            DetectPlatform();
        }

        private void LateUpdate()
        {
            if (ActivePlatform != XRHardwarePlatform.MetaQuestOpenXR || !Application.isMobilePlatform)
                return;
            if (_roomOrigin == null)
            {
                var camera = Camera.main;
                if (camera == null) return;
                _roomOrigin = camera.GetComponentInParent<XROrigin>();
                if (_roomOrigin == null) return;
                _roomAnchorManager = _roomOrigin.GetComponent<ARAnchorManager>();
                if (_roomAnchorManager == null)
                    _roomAnchorManager = _roomOrigin.gameObject.AddComponent<ARAnchorManager>();
            }

            if (_roomAnchor == null)
            {
                if (!_roomAnchorTarget.HasValue && !_addingRoomAnchor &&
                    Time.unscaledTime >= _anchorRetryAt && _roomAnchorManager.subsystem?.running == true)
                    CreateRoomAnchor();
                return;
            }
            if (_roomAnchor.trackingState != TrackingState.Tracking) return;

            var tracked = new Pose(_roomAnchor.transform.position, _roomAnchor.transform.rotation);
            if (!_roomAnchorTarget.HasValue)
            {
                _roomAnchorTarget = tracked;
                Debug.Log("[SpatialMAS] Quest room anchor acquired.");
                return;
            }
            Pose target = _roomAnchorTarget.Value;
            if (Vector3.Distance(target.position, tracked.position) < 0.002f &&
                Quaternion.Angle(target.rotation, tracked.rotation) < 0.2f) return;

            var origin = _roomOrigin.transform;
            // Correct the tracking rig so the observed physical anchor returns to its
            // original Unity pose. Simulation objects and recorded world coordinates stay put.
            Quaternion correction = target.rotation * Quaternion.Inverse(tracked.rotation);
            origin.SetPositionAndRotation(target.position + correction * (origin.position - tracked.position),
                correction * origin.rotation);
        }

        private async void CreateRoomAnchor()
        {
            var camera = Camera.main;
            if (camera == null) return;
            _addingRoomAnchor = true;
            try
            {
                var result = await _roomAnchorManager.TryAddAnchorAsync(new Pose(camera.transform.position, Quaternion.identity));
                if (this == null) return;
                if (result.status.IsSuccess()) _roomAnchor = result.value;
                else Debug.LogWarning($"[SpatialMAS] Quest room anchor unavailable: {result.status}.");
            }
            catch (System.Exception error)
            {
                if (this != null) Debug.LogWarning($"[SpatialMAS] Quest room anchor unavailable: {error.Message}");
            }
            finally
            {
                if (this != null)
                {
                    _addingRoomAnchor = false;
                    _anchorRetryAt = Time.unscaledTime + 5f;
                }
            }
        }

        private void ConfigureFramePacing()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Mathf.RoundToInt(_targetRefreshRate);
            QualitySettings.maxQueuedFrames = 1;
            Time.fixedDeltaTime = 1.0f / _targetRefreshRate;

            if (InputSystem.settings != null)
            {
                InputSystem.settings.SetInternalFeatureFlag("USE_OPTIMIZED_CONTROLS", true);
            }
        }

        private IEnumerator Start()
        {
            yield return null;
            DetectPlatform();

#if UNITY_ANDROID && !UNITY_EDITOR && SPATIALMAS_META_OPENXR
            if (ActivePlatform == XRHardwarePlatform.MetaQuestOpenXR)
            {
                var displays = new List<XRDisplaySubsystem>();
                SubsystemManager.GetSubsystems(displays);
                if (displays.Count > 0)
                {
                    var display = displays[0];
                    if (MetaOpenXRDisplaySubsystemExtensions.TryGetSupportedDisplayRefreshRates(display, Allocator.Temp, out var rates))
                    {
                        using (rates)
                        {
                            float bestRate = 72.0f;
                            foreach (var rate in rates)
                            {
                                if (Mathf.Approximately(rate, _targetRefreshRate))
                                {
                                    bestRate = _targetRefreshRate;
                                    break;
                                }
                                if (rate > bestRate && rate <= _targetRefreshRate)
                                {
                                    bestRate = rate;
                                }
                            }
                            MetaOpenXRDisplaySubsystemExtensions.TryRequestDisplayRefreshRate(display, bestRate);
                        }
                    }
                }
            }
#endif
        }
    }
}
