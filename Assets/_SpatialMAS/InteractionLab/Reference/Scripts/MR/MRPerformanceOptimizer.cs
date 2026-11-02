using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

#if UNITY_ANDROID && !UNITY_EDITOR
using Unity.Collections;
using UnityEngine.XR.OpenXR.Features.Meta;
#endif

namespace EnigmaLux.MR
{
    /// <summary>
    /// Optimizes Meta Quest 3 frame pacing, display refresh rate, and hand tracking responsiveness.
    /// Locks 90 FPS / 90 Hz, disables laggy post-processing filters on hands, cuts GPU queue latency,
    /// synchronizes global hand pinch + palm grasp hysteresis parameters, and ensures both distance
    /// interaction modality cubes (Attract-to-Hand Direct Pick + Ray Pull Tether) are present for testing.
    /// </summary>
    public class MRPerformanceOptimizer : MonoBehaviour
    {
        [Header("Frame Rate & Refresh Rate")]
        [SerializeField] private float m_TargetRefreshRate = 90.0f;
        [SerializeField] private bool m_DisableHandSmoothingLag = true;

        [Header("Hand Visual Mode in Passthrough")]
        [Tooltip("Controls visual hand mesh rendering in Mixed Reality Passthrough.\n- TranslucentHolographic: Renders the Horizon OS translucent cyan hand overlay with wrist dissolve.\n- HiddenRealHandsOnly: Hides the 3D hand mesh so you see your physical hands in Passthrough with zero latency and zero displacement offset (colliders, pinch tracking, and poke remain 100% active).")]
        [SerializeField] private MRMetaHandMeshController.HandVisualMode m_HandVisualMode = MRMetaHandMeshController.HandVisualMode.TranslucentHolographic;

        public MRMetaHandMeshController.HandVisualMode handVisualMode
        {
            get => m_HandVisualMode;
            set
            {
                m_HandVisualMode = value;
                CheckAndConfigureHandOverlays();
            }
        }

        [Header("Hand Overlay Wrist Smoothing & Model Fidelity")]
        [Tooltip("If true, enables Horizon OS soft wrist dissolve gradient in MRMetaHandMeshController.")]
        [SerializeField] private bool m_EnableWristFadeSmoothing = true;

        [Tooltip("If true, attempts runtime procedural anatomical mesh binding (XR_FB_hand_tracking_mesh). If false (recommended for zero latency and zero jitter), uses the clean artist-rigged hand mesh.")]
        [SerializeField] private bool m_UseProceduralAnatomicalMesh = false;

        [Header("Hand Pinch & Palm Grasp Hysteresis (Global)")]
        [Tooltip("Distance between thumb and finger tip in meters to ACTIVATE grab (e.g. 0.028 = 2.8 cm, reliable physical fingertip contact).")]
        [Range(0.005f, 0.05f)]
        [SerializeField] private float m_PinchActivateDistance = 0.028f; // 2.8 cm - reliable physical fingertip contact

        [Tooltip("Distance between thumb and finger tip in meters to RELEASE grab (e.g. 0.045 = 4.5 cm).")]
        [Range(0.015f, 0.08f)]
        [SerializeField] private float m_PinchReleaseDistance = 0.045f; // 4.5 cm

        [Tooltip("Which fingers qualify to trigger pinch grabbing against the thumb.")]
        [SerializeField] private MRHandPinchHysteresis.PinchFingerMode m_PinchFingerMode = MRHandPinchHysteresis.PinchFingerMode.IndexOrMiddle;

        [Tooltip("Allow whole-hand palm grasping (Middle + Ring curl into palm) guarded by Anti-Poke.")]
        [SerializeField] private bool m_EnablePalmGrab = true;

        [Tooltip("Near-field direct grab sphere radius in meters.")]
        [Range(0.020f, 0.080f)]
        [SerializeField] private float m_NearCastRadius = 0.040f; // 4.0 cm reach

        [Header("Distance Modality Demo Cubes")]
        [Tooltip("Ensure both Distance Interaction Modality cubes (Cyan = AttractToHandDirect, Gold = RayPullTether) are spawned side-by-side.")]
        [SerializeField] private bool m_EnsureDualModalityDemoCubes = true;

        private void OnValidate()
        {
            if (m_PinchReleaseDistance < m_PinchActivateDistance + 0.005f)
            {
                m_PinchReleaseDistance = m_PinchActivateDistance + 0.005f;
            }
            ApplyPinchHysteresisSettings();
        }

        public void ApplyPinchHysteresisSettings()
        {
            var pinchHandlers = FindObjectsByType<MRHandPinchHysteresis>(FindObjectsInactive.Include);
            foreach (var ph in pinchHandlers)
            {
                if (ph != null)
                {
                    ph.activateDistance = m_PinchActivateDistance;
                    ph.releaseDistance = m_PinchReleaseDistance;
                    ph.pinchFingerMode = m_PinchFingerMode;
                    ph.enablePalmGrab = m_EnablePalmGrab;
                    ph.nearCastRadius = m_NearCastRadius; // 4.0 cm contact radius
                }
            }
        }

        private void Awake()
        {
            // Legacy Default-Line material renders inconsistently with URP stereo through Link.
            foreach (var visual in FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.CurveVisualController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                SpatialMAS.XR.XRInteractionRigSetup.ConfigureRayVisualMaterial(visual);

            // 1. Lock Target Frame Rate to 90 FPS
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Mathf.RoundToInt(m_TargetRefreshRate);

            // 2. Reduce GPU buffering to minimum (1 frame) to eliminate hand-to-render latency
            QualitySettings.maxQueuedFrames = 1;

            // 3. Align physics update frequency with the 90 Hz VR display
            Time.fixedDeltaTime = 1.0f / m_TargetRefreshRate;

            // 4. Optimize Input System polling
            InputSystem.settings.SetInternalFeatureFlag("USE_OPTIMIZED_CONTROLS", true);

            // 5. Disable legacy flawed HandsOneEuroFilterPostProcessor from XIT demo
            if (m_DisableHandSmoothingLag)
            {
                var euroFilters = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
                foreach (var mb in euroFilters)
                {
                    if (mb != null && mb.GetType().Name == "HandsOneEuroFilterPostProcessor")
                    {
                        mb.enabled = false;
                        mb.gameObject.SetActive(false);
                    }
                }
            }

            // 6. Ensure MRHandSmoothingProcessor is configured for zero visual latency (0ms added delay on visual mesh)
            var smoothingProcessor = GetComponent<MRHandSmoothingProcessor>() ?? FindAnyObjectByType<MRHandSmoothingProcessor>();
            if (smoothingProcessor == null)
            {
                smoothingProcessor = gameObject.AddComponent<MRHandSmoothingProcessor>();
            }
            smoothingProcessor.filterSubsystemJoints = false; // Never delay visual hand mesh in passthrough

            // 7. Attach MRMetaHandMeshController to hand skeletons and SkinnedMeshRenderers for Quest anatomical fit and Horizon OS translucent overlay
            CheckAndConfigureHandOverlays();

            // 8. Apply global hand pinch & palm grasp hysteresis thresholds and 2.2cm physical anchor configuration
            ApplyPinchHysteresisSettings();

            // 9. Ensure both distance interaction modality cubes are available side-by-side
            if (m_EnsureDualModalityDemoCubes)
            {
                EnsureDualModalityCubesInScene();
            }
        }

        private float m_NextHandCheckTime;

        private void Update()
        {
            if (Time.time >= m_NextHandCheckTime)
            {
                m_NextHandCheckTime = Time.time + 0.5f;
                CheckAndConfigureHandOverlays();
            }
        }

        /// <summary>
        /// Continuously ensures all hand visuals (both Quest and AndroidXR, procedural anatomical or fallback)
        /// have MRMetaHandMeshController active, wrist smoothing enabled, and the pristine translucent passthrough material applied.
        /// Also sanitizes any XRI Affordance MaterialHelpers so IndexOutOfRangeException cannot occur.
        /// </summary>
        public void CheckAndConfigureHandOverlays()
        {
            var trackingEvents = FindObjectsByType<UnityEngine.XR.Hands.XRHandTrackingEvents>(FindObjectsInactive.Include);
            for (int i = 0; i < trackingEvents.Length; i++)
            {
                var te = trackingEvents[i];
                if (te != null)
                {
                    // Dynamic update guarantees that joint poses update synchronously during Update()
                    // with zero forward-prediction extrapolation noise or BeforeRender micro-jitter.
                    te.updateType = UnityEngine.XR.Hands.XRHandTrackingEvents.UpdateTypes.Dynamic;
                }
            }

            var skeletonDrivers = FindObjectsByType<UnityEngine.XR.Hands.XRHandSkeletonDriver>(FindObjectsInactive.Include);
            for (int i = 0; i < skeletonDrivers.Length; i++)
            {
                var sd = skeletonDrivers[i];
                if (sd != null)
                {
                    var controller = sd.GetComponent<MRMetaHandMeshController>() ?? sd.gameObject.AddComponent<MRMetaHandMeshController>();
                    controller.visualMode = m_HandVisualMode;
                    controller.bindAnatomicalRuntimeMesh = m_UseProceduralAnatomicalMesh;
                    controller.enableWristFadeSmoothing = m_EnableWristFadeSmoothing;
                    controller.ResolveReferences();
                    controller.EnsureTranslucentPassthroughMaterial();
                    controller.ApplyVisualMode();
                }
            }

            var handRenderers = FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include);
            for (int i = 0; i < handRenderers.Length; i++)
            {
                var r = handRenderers[i];
                if (r == null) continue;

                // Explicitly skip affordance, indicator, and pinch visuals
                string rName = r.gameObject.name;
                if (rName.IndexOf("Pinch", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rName.IndexOf("Affordance", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rName.IndexOf("Poke", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rName.IndexOf("Ray", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                // Only actual hand visuals
                bool isHand = r.GetComponentInParent<UnityEngine.XR.Hands.XRHandMeshController>() != null ||
                              r.GetComponentInParent<UnityEngine.XR.Hands.XRHandSkeletonDriver>() != null;

                if (isHand)
                {
                    var controller = r.GetComponent<MRMetaHandMeshController>() ??
                                     r.GetComponentInParent<MRMetaHandMeshController>() ??
                                     r.gameObject.AddComponent<MRMetaHandMeshController>();
                    controller.visualMode = m_HandVisualMode;
                    controller.bindAnatomicalRuntimeMesh = m_UseProceduralAnatomicalMesh;
                    controller.enableWristFadeSmoothing = m_EnableWristFadeSmoothing;
                    controller.ResolveReferences();
                    controller.EnsureTranslucentPassthroughMaterial();
                    controller.ApplyVisualMode();
                }
            }

#pragma warning disable 0618 // MaterialHelperBase deprecation in XRI 3.6
            // Sanitize all XRI Material Affordance Receivers in the scene to completely eliminate IndexOutOfRangeException
            var materialHelpers = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Rendering.MaterialHelperBase>(FindObjectsInactive.Include);
            for (int i = 0; i < materialHelpers.Length; i++)
            {
                var mh = materialHelpers[i];
                if (mh != null && mh.rendererTarget != null)
                {
                    var sharedMats = mh.rendererTarget.sharedMaterials;
                    if (sharedMats == null || sharedMats.Length == 0)
                    {
                        mh.enabled = false;
                    }
                    else if (mh.materialIndex >= sharedMats.Length)
                    {
                        mh.materialIndex = Mathf.Max(0, sharedMats.Length - 1);
                    }
                }
            }
#pragma warning restore 0618
        }

        private static void EnsureDualModalityCubesInScene()
        {
            var cubes = FindObjectsByType<MRCubeInteraction>(FindObjectsInactive.Exclude);
            if (cubes == null || cubes.Length == 0)
                return;

            if (cubes.Length == 1)
            {
                var primaryCube = cubes[0];
                primaryCube.distanceGrabModality = MRCubeInteraction.DistanceGrabModality.AttractToHandDirect;
                primaryCube.transform.position = new Vector3(-0.16f, 1.25f, 0.55f);

                GameObject pullCubeGo = Instantiate(primaryCube.gameObject, new Vector3(0.16f, 1.25f, 0.55f), Quaternion.identity);
                pullCubeGo.name = "MR_Cube_Pull_10cm";
                var pullCube = pullCubeGo.GetComponent<MRCubeInteraction>();
                if (pullCube != null)
                {
                    pullCube.distanceGrabModality = MRCubeInteraction.DistanceGrabModality.RayPullTether;
                    pullCube.IdleColor = new Color(1.0f, 0.72f, 0.0f, 1.0f); // Solar Gold (#FFB800)
                }
            }
        }

        private IEnumerator Start()
        {
            // Give OpenXR subsystems 1 frame to initialize
            yield return null;

#if UNITY_ANDROID && !UNITY_EDITOR
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
                            if (Mathf.Approximately(rate, m_TargetRefreshRate))
                            {
                                bestRate = m_TargetRefreshRate;
                                break;
                            }
                            if (rate > bestRate && rate <= m_TargetRefreshRate)
                            {
                                bestRate = rate;
                            }
                        }
                        if (MetaOpenXRDisplaySubsystemExtensions.TryRequestDisplayRefreshRate(display, bestRate))
                        {
                            Debug.Log($"[MRPerformanceOptimizer] Successfully requested Meta Quest display refresh rate: {bestRate} Hz");
                        }
                    }
                }
            }
#endif
        }
    }
}
