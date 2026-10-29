using System.Collections;
using System.Collections.Generic;
using SpatialMAS.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

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

        public XRHardwarePlatform ActivePlatform { get; private set; } = XRHardwarePlatform.DesktopStandalone;
        public string ActiveDeviceName { get; private set; } = "Desktop Standalone";
        public bool IsXRHeadsetActive { get; private set; }

        public void Initialize(float targetRefreshRate = 90.0f)
        {
            _targetRefreshRate = Mathf.Clamp(targetRefreshRate, 60f, 120f);
            Application.runInBackground = true;

            DetectPlatform();
            ConfigureFramePacing();
        }

        private void DetectPlatform()
        {
            string model = SystemInfo.deviceModel ?? string.Empty;
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            IsXRHeadsetActive = displays.Count > 0 && displays[0].running;

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
                    tpd.enabled = IsXRHeadsetActive;
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
