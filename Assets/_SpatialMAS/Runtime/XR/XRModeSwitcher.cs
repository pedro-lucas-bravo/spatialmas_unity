using System.Collections.Generic;
using SpatialMAS.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;

namespace SpatialMAS.XR
{
    /// <summary>
    /// Switches at runtime between Mixed Reality Passthrough (MR) and Virtual Reality Immersive Studio (VR)
    /// across Meta Quest 3, Apple Vision Pro, Generic OpenXR headsets, and Desktop.
    /// Can be triggered via UI button, [Tab]/[M] keys, or OSC command (/xr/mode 0|1).
    /// </summary>
    public class XRModeSwitcher : MonoBehaviour
    {
        [SerializeField] private Camera _mainCamera;
        [SerializeField] private ARSession _arSession;
        [SerializeField] private ARCameraManager _arCameraManager;
        [SerializeField] private GameObject _vrStudioEnvironmentRoot;
        [SerializeField] private Color _vrBackdropColor = new Color(0.05f, 0.067f, 0.105f, 1.0f);

        private SimulationCommandBus _bus;
        private XRDisplayMode _currentMode = XRDisplayMode.MRPassthrough;
        private GameObject _studioFloorDisc;
        private GameObject _gridLinesRoot;

        private static readonly List<XRDisplaySubsystem> s_DisplaySubsystems = new List<XRDisplaySubsystem>();

        public XRDisplayMode CurrentMode => _currentMode;

        public void Initialize(
            SimulationCommandBus bus,
            Camera mainCam,
            ARSession arSession,
            ARCameraManager arCamMgr,
            GameObject vrStudioRoot,
            XRDisplayMode initialMode = XRDisplayMode.MRPassthrough)
        {
            _bus = bus;
            _mainCamera = mainCam != null ? mainCam : Camera.main;
            _arSession = arSession;
            _arCameraManager = arCamMgr;
            _vrStudioEnvironmentRoot = vrStudioRoot;

            if (_vrStudioEnvironmentRoot == null)
            {
                _vrStudioEnvironmentRoot = CreateProceduralVRStudioFloorGrid();
            }

            if (_bus != null)
            {
                _bus.OnSetXRDisplayMode += SetDisplayMode;
            }

            SetDisplayMode(initialMode);
        }

        private void OnDestroy()
        {
            if (_bus != null)
            {
                _bus.OnSetXRDisplayMode -= SetDisplayMode;
            }
        }

        public void ToggleDisplayMode()
        {
            SetDisplayMode(_currentMode == XRDisplayMode.MRPassthrough
                ? XRDisplayMode.VRImmersive
                : XRDisplayMode.MRPassthrough);
        }

        private static bool IsPhysicalXRHeadsetRunning()
        {
#if UNITY_VISIONOS || (UNITY_ANDROID && !UNITY_EDITOR)
            return true;
#else
            SubsystemManager.GetSubsystems(s_DisplaySubsystems);
            for (int i = 0; i < s_DisplaySubsystems.Count; i++)
            {
                if (s_DisplaySubsystems[i] != null && s_DisplaySubsystems[i].running)
                {
                    return true;
                }
            }
            return false;
#endif
        }

        public void SetDisplayMode(XRDisplayMode mode)
        {
            _currentMode = mode;
            if (_mainCamera == null) _mainCamera = Camera.main;

            bool isMR = (mode == XRDisplayMode.MRPassthrough);
            bool hasPhysicalHmd = IsPhysicalXRHeadsetRunning();

            // 1. Configure AR Passthrough Subsystem ONLY when a real XR headset is active.
            // Keeping ARSession/ARCameraManager disabled on Desktop prevents Unity's XRSimulation
            // from hijacking the camera or running background cubemap/plane raycast scans in the Editor.
            if (_arCameraManager == null && _mainCamera != null)
            {
                _arCameraManager = _mainCamera.GetComponent<ARCameraManager>();
            }
            if (_arCameraManager != null)
            {
                _arCameraManager.enabled = isMR && hasPhysicalHmd;
            }
            if (_arSession != null)
            {
                _arSession.enabled = isMR && hasPhysicalHmd;
            }

            // 2. Configure Camera Background Alpha & URP Post-Processing
            if (_mainCamera != null)
            {
                _mainCamera.clearFlags = CameraClearFlags.SolidColor;
                // Crucial for Quest 3 & OpenXR Passthrough: Alpha MUST be 0.0 in MR mode!
                _mainCamera.backgroundColor = isMR
                    ? new Color(0f, 0f, 0f, 0f)
                    : _vrBackdropColor;

                var urpData = _mainCamera.GetComponent<UniversalAdditionalCameraData>();
                if (urpData == null)
                {
                    urpData = _mainCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                }
                // In MR Passthrough mode, URP UberPost MUST be disabled so alpha is not overwritten to 1.0
                urpData.renderPostProcessing = !isMR;
            }

            // 3. Toggle Virtual VR Studio Reference Floor & Grid
            if (_vrStudioEnvironmentRoot != null)
            {
                if (!isMR)
                {
                    // Full VR Immersive Studio: Show both solid studio floor disc and grid lines
                    _vrStudioEnvironmentRoot.SetActive(true);
                    if (_studioFloorDisc != null) _studioFloorDisc.SetActive(true);
                    if (_gridLinesRoot != null) _gridLinesRoot.SetActive(true);
                }
                else if (!hasPhysicalHmd)
                {
                    // Desktop / Editor MR Preview (no physical camera feed):
                    // Keep the subtle 3D spatial grid lines visible (without the opaque floor disc)
                    // so researchers have clear 3D spatial depth & motion grounding while walking with WASD!
                    _vrStudioEnvironmentRoot.SetActive(true);
                    if (_studioFloorDisc != null) _studioFloorDisc.SetActive(false);
                    if (_gridLinesRoot != null) _gridLinesRoot.SetActive(true);
                }
                else
                {
                    // Physical XR Headset in MR Passthrough mode: Hide virtual floor so real room is unobstructed
                    _vrStudioEnvironmentRoot.SetActive(false);
                }
            }
        }

        private GameObject CreateProceduralVRStudioFloorGrid()
        {
            var root = new GameObject("VR_ImmersiveStudioEnvironment");
            root.transform.SetParent(transform, false);

            Material floorMat = Resources.Load<Material>("Mat_SpatialMAS_VRFloor");
            if (floorMat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                floorMat = new Material(shader)
                {
                    name = "SpatialMAS_VRFloorMat",
                    color = new Color(0.08f, 0.10f, 0.15f, 1.0f)
                };
                floorMat.SetColor("_BaseColor", new Color(0.08f, 0.10f, 0.15f, 1.0f));
                floorMat.SetFloat("_Smoothness", 0.35f);
            }

            Material lineMat = Resources.Load<Material>("Mat_SpatialMAS_VRGridLine");
            if (lineMat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                lineMat = new Material(shader)
                {
                    name = "SpatialMAS_VRGridLineMat",
                    color = new Color(0.0f, 0.72f, 0.95f, 1.0f)
                };
                lineMat.SetColor("_BaseColor", new Color(0.0f, 0.72f, 0.95f, 1.0f));
            }

            // Base dark studio floor disc
            _studioFloorDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _studioFloorDisc.name = "StudioFloorDisc";
            _studioFloorDisc.transform.SetParent(root.transform, false);
            _studioFloorDisc.transform.localPosition = new Vector3(0f, -0.01f, 0f);
            _studioFloorDisc.transform.localScale = new Vector3(12f, 0.005f, 12f);
            var col = _studioFloorDisc.GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = false;
                Destroy(col);
            }
            _studioFloorDisc.GetComponent<Renderer>().sharedMaterial = floorMat;

            _gridLinesRoot = new GameObject("HolographicGridLines");
            _gridLinesRoot.transform.SetParent(root.transform, false);

            // Subtle reference grid lines (-4m to +4m)
            for (int i = -4; i <= 4; i++)
            {
                GameObject lineX = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lineX.name = $"GridLine_X_{i}";
                lineX.transform.SetParent(_gridLinesRoot.transform, false);
                lineX.transform.localPosition = new Vector3(0f, 0.001f, i * 1.0f);
                lineX.transform.localScale = new Vector3(8.0f, 0.002f, 0.008f);
                var cx = lineX.GetComponent<Collider>();
                if (cx != null) { cx.enabled = false; Destroy(cx); }
                lineX.GetComponent<Renderer>().sharedMaterial = lineMat;

                GameObject lineZ = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lineZ.name = $"GridLine_Z_{i}";
                lineZ.transform.SetParent(_gridLinesRoot.transform, false);
                lineZ.transform.localPosition = new Vector3(i * 1.0f, 0.001f, 0f);
                lineZ.transform.localScale = new Vector3(0.008f, 0.002f, 8.0f);
                var cz = lineZ.GetComponent<Collider>();
                if (cz != null) { cz.enabled = false; Destroy(cz); }
                lineZ.GetComponent<Renderer>().sharedMaterial = lineMat;
            }

            return root;
        }
    }
}
