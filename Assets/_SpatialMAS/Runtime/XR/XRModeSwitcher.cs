using SpatialMAS.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;

namespace SpatialMAS.XR
{
    /// <summary>
    /// Switches at runtime between Mixed Reality Passthrough (MR) and Virtual Reality Immersive Studio (VR)
    /// across Meta Quest 3, Apple Vision Pro, Generic OpenXR headsets, and Desktop.
    /// Can be triggered via UI button or OSC command (/xr/mode 0|1).
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

        public void SetDisplayMode(XRDisplayMode mode)
        {
            _currentMode = mode;
            if (_mainCamera == null) _mainCamera = Camera.main;

            bool isMR = (mode == XRDisplayMode.MRPassthrough);

            // 1. Configure AR Passthrough Subsystem (Quest 3 / Vision Pro / Android XR)
            if (_arCameraManager == null && _mainCamera != null)
            {
                _arCameraManager = _mainCamera.GetComponent<ARCameraManager>();
            }
            if (_arCameraManager != null)
            {
                _arCameraManager.enabled = isMR;
            }
            if (_arSession != null)
            {
                _arSession.enabled = isMR;
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
                _vrStudioEnvironmentRoot.SetActive(!isMR);
            }
        }

        private GameObject CreateProceduralVRStudioFloorGrid()
        {
            var root = new GameObject("VR_ImmersiveStudioEnvironment");
            root.transform.SetParent(transform, false);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var floorMat = new Material(shader)
            {
                name = "SpatialMAS_VRFloorMat",
                color = new Color(0.08f, 0.10f, 0.15f, 1.0f)
            };
            floorMat.SetColor("_BaseColor", new Color(0.08f, 0.10f, 0.15f, 1.0f));
            floorMat.SetFloat("_Smoothness", 0.35f);

            var lineMat = new Material(shader)
            {
                name = "SpatialMAS_VRGridLineMat",
                color = new Color(0.0f, 0.75f, 1.0f, 1.0f)
            };
            lineMat.SetColor("_BaseColor", new Color(0.0f, 0.75f, 1.0f, 1.0f));
            lineMat.EnableKeyword("_EMISSION");
            lineMat.SetColor("_EmissionColor", new Color(0.0f, 0.45f, 0.65f, 1.0f));

            // Base dark studio floor disc
            GameObject floorDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floorDisc.name = "StudioFloorDisc";
            floorDisc.transform.SetParent(root.transform, false);
            floorDisc.transform.localPosition = new Vector3(0f, -0.01f, 0f);
            floorDisc.transform.localScale = new Vector3(12f, 0.005f, 12f);
            var col = floorDisc.GetComponent<Collider>();
            if (col != null) Destroy(col);
            floorDisc.GetComponent<Renderer>().sharedMaterial = floorMat;

            // Subtle holographic grid lines (-4m to +4m)
            for (int i = -4; i <= 4; i++)
            {
                GameObject lineX = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lineX.name = $"GridLine_X_{i}";
                lineX.transform.SetParent(root.transform, false);
                lineX.transform.localPosition = new Vector3(0f, 0.001f, i * 1.0f);
                lineX.transform.localScale = new Vector3(8.0f, 0.002f, 0.008f);
                var cx = lineX.GetComponent<Collider>();
                if (cx != null) Destroy(cx);
                lineX.GetComponent<Renderer>().sharedMaterial = lineMat;

                GameObject lineZ = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lineZ.name = $"GridLine_Z_{i}";
                lineZ.transform.SetParent(root.transform, false);
                lineZ.transform.localPosition = new Vector3(i * 1.0f, 0.001f, 0f);
                lineZ.transform.localScale = new Vector3(0.008f, 0.002f, 8.0f);
                var cz = lineZ.GetComponent<Collider>();
                if (cz != null) Destroy(cz);
                lineZ.GetComponent<Renderer>().sharedMaterial = lineMat;
            }

            return root;
        }
    }
}
