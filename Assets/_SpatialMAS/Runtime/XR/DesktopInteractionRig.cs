using SpatialMAS.Agents;
using System.Collections.Generic;
using SpatialMAS.Core;
using SpatialMAS.Environment;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace SpatialMAS.XR
{
    /// <summary>
    /// Game-like Desktop Mouse & Keyboard navigation and 3D agent manipulation rig
    /// (inspired by HS-ims FirstPersonCamera, CubeGrabber, and MouseTouchInteractor, enhanced with modern 3D HCI):
    /// - W/A/S/D or Arrow Keys + E/Q: Always-active smooth 3D translation (NO need to hold RMB!).
    /// - Left Shift (Sprint 2.4x) / Left Ctrl (Precision Slow 0.35x).
    /// - Hold Right Mouse Button (RMB) + Mouse Move: Look around (Pitch & Yaw).
    /// - Press [F]: Toggle First-Person Game Look Mode (locks cursor for effortless mouse-look without holding RMB; Esc unlocks).
    /// - Hold Middle Mouse Button (MMB) + Mouse Move: Pan camera in the view plane.
    /// - Mouse Scroll Wheel: Zoom/dolly camera forward/backward (or push/pull grabbed agent while dragging).
    /// - Left Mouse Button (LMB) Click & Drag on interactive agent (or Alt+LMB on any agent): Smooth 3D plane/ray manipulation.
    /// - Left/Right Click on any agent: Emits HS-ims /agents/control <id> <0=LMB,1=RMB> <1=press,0=release>.
    /// - Press [R] while dragging an agent: Snap-rotate by 90 degrees.
    /// - Press [C]: Reset camera to default arena overview pose.
    /// </summary>
    public class DesktopInteractionRig : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private float _moveSpeed = 2.4f;
        [SerializeField] private float _lookSensitivity = 0.16f;
        [SerializeField] private float _panSensitivity = 0.0035f;
        [SerializeField] private float _scrollZoomSpeed = 0.0025f;

        private SimulationCommandBus _bus;
        private XRPlatformAdapter _platform;
        private AgentManager _agentManager;
        private GameObject _dragCue;
        private Mesh _dragCueMesh;
        private MeshRenderer _dragCueRenderer;
        private MaterialPropertyBlock _cueFaceColor;
        private MaterialPropertyBlock _cueArrowColor;
        private AgentEntity _hoveredAgent;
        private AgentEntity _grabbedAgent;
        private SpatialCalibrationReference _grabbedReference;
        private SpatialCalibrationReference _calibrationReference;
        private int _referenceManipulationRevision;
        private Vector3 _ringAxis, _ringStart;
        private Quaternion _ringRotation;

        public void SetCalibrationReference(SpatialCalibrationReference reference) => _calibrationReference = reference;
        private BoundaryHandle _grabbedBoundaryHandle;
        private int _leftPressedAgentId = -1;
        private int _rightPressedAgentId = -1;
        private readonly RaycastHit[] _agentHits = new RaycastHit[1024];

        private Plane _dragPlane;
        private Vector3 _grabOffsetWorld;
        private float _grabDepthMeters;
        private bool _facePlaneMode;
        private DragFace _hoverFace;
        private DragFace _capturedFace;
        private bool _overviewEnabled;
        private bool _desktopViewApplied;
        private bool _orthographicView = true;
        private bool _originalOrthographic;
        private float _originalOrthographicSize;
        private Vector3 _originalLocalPosition;
        private Quaternion _originalLocalRotation;
        private Vector3 _overviewCenter;
        private Vector3 _orbitPivot;

        private struct DragFace
        {
            public Transform Target;
            public Vector3 Normal;
            public Vector3 Up;
            public float Size;
        }

        private Vector3 _defaultCameraPosition = new Vector3(0f, 1.35f, -0.65f);
        private Quaternion _defaultCameraRotation = Quaternion.Euler(8f, 0f, 0f);
        private Vector3 _currentMoveVelocity;

        private float _yaw;
        private float _pitch;
        private bool _anglesInitialized;
        private bool _fpsCursorLockMode;

        public Rect IgnoredScreenGuiRect { get; set; } = new Rect(12f, 12f, 430f, 480f);
        public AgentEntity HoveredAgent => _hoveredAgent;
        public AgentEntity CurrentlyGrabbedAgent => _grabbedAgent;
        public bool IsDraggingObject => _grabbedAgent != null || _grabbedReference != null || _grabbedBoundaryHandle != null;
        public bool IsFpsCursorLocked => _fpsCursorLockMode;
        public bool IsOrthographic => _camera != null && _camera.orthographic;
        public bool UsesFacePlane => _facePlaneMode;
        private bool _xrViewSeen;
        private bool IsXrViewActive => XRSettings.isDeviceActive || (_platform?.IsXRHeadsetActive ?? false);
        private bool IsDesktop => !_xrViewSeen && !IsXrViewActive;

        public void Initialize(Camera cam, SimulationCommandBus bus = null)
        {
            _camera = cam != null ? cam : Camera.main;
            _bus = bus;
            _platform = GetComponent<XRPlatformAdapter>();
            if (_camera != null)
            {
                _defaultCameraPosition = _camera.transform.position;
                _defaultCameraRotation = _camera.transform.rotation;
                SyncAnglesFromCamera();
            }
        }

        public void EnableDesktopOverview(Vector3 center)
        {
            if (_camera == null) return;
            _overviewEnabled = true;
            _overviewCenter = center;
            _orbitPivot = center;
            _originalOrthographic = _camera.orthographic;
            _originalOrthographicSize = _camera.orthographicSize;
            _originalLocalPosition = _camera.transform.localPosition;
            _originalLocalRotation = _camera.transform.localRotation;
            _facePlaneMode = true;
            UpdateDesktopView();
        }

        public void SetAgentManager(AgentManager manager) => _agentManager = manager;

        public void SetOrthographic(bool enabled)
        {
            if (!IsDesktop || IsDraggingObject || _camera == null || _camera.orthographic == enabled) return;
            _orthographicView = enabled;
            _camera.orthographic = enabled;
            float distance = Mathf.Max(0.1f, Vector3.Dot(_orbitPivot - _camera.transform.position, _camera.transform.forward));
            float halfFov = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            if (enabled)
            {
                _orbitPivot = _camera.transform.position + _camera.transform.forward * distance;
                _camera.orthographicSize = Mathf.Clamp(distance * Mathf.Tan(halfFov), 0.2f, 30f);
                SetFpsCursorLock(false);
            }
            else
                _camera.transform.position = _orbitPivot - _camera.transform.forward * (_camera.orthographicSize / Mathf.Tan(halfFov));
        }

        public void SetFacePlaneMode(bool enabled)
        {
            if (!IsDesktop || IsDraggingObject) return;
            _facePlaneMode = enabled;
            _hoverFace = default;
        }

        private void UpdateDesktopView()
        {
            // A headset can briefly lose its display while asleep. Keep the XR camera
            // assigned to tracking for this scene instead of applying the desktop overview.
            _xrViewSeen |= IsXrViewActive;
            if (!_overviewEnabled || _camera == null || _desktopViewApplied == IsDesktop) return;
            _desktopViewApplied = IsDesktop;
            if (_desktopViewApplied)
                CenterCamera();
            else
            {
                ReleasePointerInteractions();
                if (_fpsCursorLockMode) SetFpsCursorLock(false);
                _camera.orthographic = _originalOrthographic;
                _camera.orthographicSize = _originalOrthographicSize;
                _camera.transform.SetLocalPositionAndRotation(_originalLocalPosition, _originalLocalRotation);
            }
        }

        private void SyncAnglesFromCamera()
        {
            if (_camera == null) return;
            Vector3 euler = _camera.transform.eulerAngles;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
            _yaw = euler.y;
            _anglesInitialized = true;
        }

        public void CenterCamera()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null || !IsDesktop || IsDraggingObject) return;

            if (_overviewEnabled)
            {
                _orbitPivot = _overviewCenter;
                _camera.orthographic = _orthographicView;
                _camera.orthographicSize = 2.5f;
                Quaternion angle = Quaternion.Euler(35.264f, 45f, 0f);
                float distance = _camera.orthographic ? 8f : 2.5f / Mathf.Tan(_camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
                _camera.transform.SetPositionAndRotation(_orbitPivot - angle * Vector3.forward * distance, angle);
            }
            else
                _camera.transform.SetPositionAndRotation(_defaultCameraPosition, _defaultCameraRotation);
            _currentMoveVelocity = Vector3.zero;
            SyncAnglesFromCamera();
        }

        public void ToggleFpsCursorLock()
        {
            if (IsDesktop && !IsOrthographic && !IsDraggingObject) SetFpsCursorLock(!_fpsCursorLockMode);
        }

        public void SetFpsCursorLock(bool locked)
        {
            _fpsCursorLockMode = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void OnDisable()
        {
            ReleasePointerInteractions();
            if (_fpsCursorLockMode)
            {
                SetFpsCursorLock(false);
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) ReleasePointerInteractions();
        }

        private void OnDestroy()
        {
            if (_dragCue != null) Destroy(_dragCue);
            if (_dragCueMesh != null) Destroy(_dragCueMesh);
        }

        private void ReleasePointerInteractions()
        {
            if (_grabbedAgent != null) _grabbedAgent.EndUserGrab();
            _grabbedAgent = null;
            _grabbedReference = null;
            _grabbedBoundaryHandle = null;
            _hoverFace = default;
            _capturedFace = default;
            if (_dragCue != null) _dragCue.SetActive(false);
            ReleaseLeftClick();
            ReleaseRightClick();
            if (_hoveredAgent != null) _hoveredAgent.Visuals?.SetHighlight(false);
            _hoveredAgent = null;
        }

        private void ReleaseLeftClick()
        {
            if (_leftPressedAgentId < 0) return;
            _bus?.EmitAgentControlClick(_leftPressedAgentId, 0, 0);
            _bus?.EmitAgentTouch(_leftPressedAgentId, 0, -1);
            _leftPressedAgentId = -1;
        }

        private void ReleaseRightClick()
        {
            if (_rightPressedAgentId < 0) return;
            _bus?.EmitAgentControlClick(_rightPressedAgentId, 1, 0);
            _rightPressedAgentId = -1;
        }

        private void Update()
        {
            if (_camera == null) _camera = Camera.main;
            UpdateDesktopView();
            if (_calibrationReference != null) _calibrationReference.SetDesktopRings(IsDesktop);
            if (_camera == null || !IsDesktop) return;

            var mouse = Mouse.current;
            var kb = Keyboard.current;

            if (kb != null)
            {
                if (kb[Key.F].wasPressedThisFrame)
                {
                    ToggleFpsCursorLock();
                }
                if (kb[Key.Escape].wasPressedThisFrame && _fpsCursorLockMode)
                {
                    SetFpsCursorLock(false);
                }
                if (kb[Key.C].wasPressedThisFrame)
                {
                    CenterCamera();
                }
            }

            // 1. Always-active WASD / Arrow / EQ keyboard translation (does NOT require holding RMB!)
            if (_capturedFace.Target == null) HandleKeyboardMovement(kb);

            if (mouse == null)
            {
                ReleasePointerInteractions();
                return;
            }

            // 2. Mouse Look (RMB hold OR FPS lock mode), MMB Pan, and Scroll Zoom
            if (_capturedFace.Target == null) HandleMouseCameraControl(mouse, kb);

            // 3. Hover detection, LMB 3D Plane/Ray Grab, and LMB/RMB /agents/control clicks
            HandleMouseAgentInteraction(mouse, kb);
        }

        private void LateUpdate()
        {
            UpdateDragCue(Mouse.current != null && Mouse.current.leftButton.isPressed);
        }

        private void UpdateDragCue(bool leftPressed)
        {
            Transform target = null;
            float size = 0f;
            DragFace face = default;
            if (_camera != null && _camera.isActiveAndEnabled && IsDesktop && _facePlaneMode)
            {
                face = leftPressed && IsDraggingObject ? _capturedFace : _hoverFace;
                if (face.Target != null && face.Target.gameObject.activeInHierarchy) target = face.Target;
            }
            else if (leftPressed && _camera != null && _camera.isActiveAndEnabled && IsDesktop)
            {
                if (_grabbedAgent != null && _grabbedAgent.IsGrabbedByUser && _grabbedAgent.gameObject.activeInHierarchy &&
                    _grabbedAgent.ShapeType != AgentShapeType.Cube)
                {
                    target = _grabbedAgent.transform;
                    size = _grabbedAgent.BaseScaleMeters * 1.12f;
                    // A camera-aligned cube encloses the pyramid even when its widest corners face the camera.
                    if (_grabbedAgent.ShapeType == AgentShapeType.Pyramid) size *= Mathf.Sqrt(3f);
                }
                else if (_grabbedBoundaryHandle != null && _grabbedBoundaryHandle.isActiveAndEnabled &&
                    _grabbedBoundaryHandle.Shape != BoundaryHandleShape.Cube)
                {
                    target = _grabbedBoundaryHandle.transform;
                    size = _grabbedBoundaryHandle.SizeMeters;
                }
            }

            if (target == null)
            {
                if (_dragCue != null) _dragCue.SetActive(false);
                return;
            }

            if (_dragCue == null) CreateDragCue();
            if (_dragCue == null) return;
            Quaternion rotation = face.Target != null
                ? Quaternion.LookRotation(-face.Normal, face.Up)
                : Quaternion.LookRotation(-_dragPlane.normal, _camera.transform.up);
            _dragCue.transform.SetPositionAndRotation(target.position, rotation);
            _dragCue.transform.localScale = Vector3.one * (face.Target != null ? face.Size : size);
            _cueFaceColor.SetColor("_BaseColor", new Color(0.2f, 0.9f, 1f, face.Target != null && !leftPressed ? 0.20f : 0.34f));
            _dragCueRenderer.SetPropertyBlock(_cueFaceColor, 1);
            _cueArrowColor.SetColor("_BaseColor", face.Target != null ? new Color(0.7f, 1f, 1f, 0.72f) : Color.clear);
            _dragCueRenderer.SetPropertyBlock(_cueArrowColor, 2);
            _dragCue.SetActive(true);
        }

        private void CreateDragCue()
        {
            var material = Resources.Load<Material>("Mat_SpatialMAS_BoundaryTranslucent");
            if (material == null) return;

            _dragCue = new GameObject("Desktop Drag Cue");
            _dragCue.transform.SetParent(transform, false);
            _dragCueMesh = new Mesh
            {
                name = "Desktop Drag Cue Mesh",
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                    new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
                    new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
                    new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f)
                },
                subMeshCount = 3
            };
            var vertices = new List<Vector3>(_dragCueMesh.vertices);
            var arrows = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                Vector3 direction = Quaternion.AngleAxis(90f * i, Vector3.forward) * Vector3.up;
                Vector3 side = Vector3.Cross(direction, Vector3.forward);
                Vector3 origin = Vector3.back * 0.502f;
                int start = vertices.Count;
                vertices.Add(origin + direction * 0.28f);
                vertices.Add(origin + direction * 0.17f - side * 0.052f);
                vertices.Add(origin + direction * 0.17f + side * 0.052f);
                vertices.Add(origin + direction * 0.18f - side * 0.014f);
                vertices.Add(origin + direction * 0.04f - side * 0.014f);
                vertices.Add(origin + direction * 0.04f + side * 0.014f);
                vertices.Add(origin + direction * 0.18f + side * 0.014f);
                arrows.AddRange(new[] { start, start + 2, start + 1, start + 3, start + 5, start + 4, start + 3, start + 6, start + 5 });
            }
            _dragCueMesh.SetVertices(vertices);
            _dragCueMesh.SetTriangles(new[] { 4, 6, 5, 4, 7, 6, 0, 4, 5, 0, 5, 1,
                3, 2, 6, 3, 6, 7, 1, 5, 6, 1, 6, 2, 0, 3, 7, 0, 7, 4 }, 0);
            _dragCueMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 1);
            _dragCueMesh.SetTriangles(arrows, 2);
            _dragCueMesh.RecalculateNormals();
            _dragCue.AddComponent<MeshFilter>().sharedMesh = _dragCueMesh;
            _dragCueRenderer = _dragCue.AddComponent<MeshRenderer>();
            _dragCueRenderer.sharedMaterials = new[] { material, material, material };
            _dragCueRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _dragCueRenderer.receiveShadows = false;
            var colors = new MaterialPropertyBlock();
            colors.SetColor("_BaseColor", new Color(0.15f, 0.75f, 1f, 0.13f));
            _dragCueRenderer.SetPropertyBlock(colors, 0);
            _cueFaceColor = new MaterialPropertyBlock();
            _cueArrowColor = new MaterialPropertyBlock();
        }

        private void HandleKeyboardMovement(Keyboard kb)
        {
            if (kb == null) return;

            Vector3 inputDir = Vector3.zero;
            if (kb[Key.W].isPressed || kb[Key.UpArrow].isPressed) inputDir += _camera.transform.forward;
            if (kb[Key.S].isPressed || kb[Key.DownArrow].isPressed) inputDir -= _camera.transform.forward;
            if (kb[Key.D].isPressed || kb[Key.RightArrow].isPressed) inputDir += _camera.transform.right;
            if (kb[Key.A].isPressed || kb[Key.LeftArrow].isPressed) inputDir -= _camera.transform.right;
            if (kb[Key.E].isPressed || kb[Key.PageUp].isPressed) inputDir += Vector3.up;
            if (kb[Key.Q].isPressed || kb[Key.PageDown].isPressed) inputDir -= Vector3.up;

            if (inputDir.sqrMagnitude > 1f)
            {
                inputDir.Normalize();
            }

            float speedMultiplier = 1.0f;
            if (kb[Key.LeftShift].isPressed || kb[Key.RightShift].isPressed) speedMultiplier = 2.4f;
            else if (kb[Key.LeftCtrl].isPressed || kb[Key.RightCtrl].isPressed) speedMultiplier = 0.35f;

            Vector3 targetVelocity = inputDir * (_moveSpeed * speedMultiplier);
            float dt = Time.unscaledDeltaTime;
            _currentMoveVelocity = Vector3.Lerp(_currentMoveVelocity, targetVelocity, Mathf.Clamp01(dt * 16.0f));

            if (_currentMoveVelocity.sqrMagnitude > 0.00001f)
            {
                Vector3 displacement = _currentMoveVelocity * dt;
                _camera.transform.position += displacement;
                _orbitPivot += displacement;
            }
        }

        private void HandleMouseCameraControl(Mouse mouse, Keyboard kb)
        {
            if (!_anglesInitialized)
            {
                SyncAnglesFromCamera();
            }

            Vector2 delta = mouse.delta.ReadValue();

            // Look rotation: active when holding Right Mouse Button OR when in FPS Cursor Lock mode
            if (mouse.rightButton.isPressed || _fpsCursorLockMode)
            {
                float orbitDistance = Vector3.Distance(_camera.transform.position, _orbitPivot);
                _yaw += delta.x * _lookSensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * _lookSensitivity, -88f, 88f);
                _camera.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
                if (IsOrthographic)
                    _camera.transform.position = _orbitPivot - _camera.transform.forward * orbitDistance;
            }

            // Middle Mouse Button Pan (mirrors HS-ims FirstPersonCamera.cs)
            if (mouse.middleButton.isPressed)
            {
                Vector3 panOffset = (-_camera.transform.right * delta.x - _camera.transform.up * delta.y) * _panSensitivity;
                _camera.transform.position += panOffset;
                _orbitPivot += panOffset;
            }

            // Scroll Wheel Dolly/Zoom when not currently grabbing an agent (and pointer not over HUD)
            if (!IsDraggingObject && !IsMouseOverHud(mouse))
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    float zoomStep = scroll * _scrollZoomSpeed;
                    if (IsOrthographic)
                        _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize * Mathf.Exp(-zoomStep), 0.2f, 30f);
                    else
                        _camera.transform.position += _camera.transform.forward * zoomStep;
                }
            }
        }

        private void HandleMouseAgentInteraction(Mouse mouse, Keyboard kb)
        {
            if (_grabbedAgent != null && (!_grabbedAgent.gameObject.activeInHierarchy || !_grabbedAgent.IsGrabbedByUser))
                _grabbedAgent = null;
            if (!mouse.leftButton.isPressed || !IsDraggingObject) _capturedFace = default;
            _hoverFace = default;
            bool pointerOverHud = !_fpsCursorLockMode && IsMouseOverHud(mouse);
            Vector2 screenPos = _fpsCursorLockMode
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : mouse.position.ReadValue();

            Ray ray = _camera.ScreenPointToRay(screenPos);
            if (HandleEnvironmentInteraction(ray, mouse, kb, pointerOverHud)) return;
            bool forceGrab = kb != null && (kb[Key.LeftAlt].isPressed || kb[Key.RightAlt].isPressed);

            // 1. Update Hovered Agent when not dragging
            if (_grabbedAgent == null)
            {
                AgentEntity hitAgent = null;
                if (!pointerOverHud && TryRaycastAgent(ray, out AgentEntity hoverAgent, out _))
                {
                    hitAgent = hoverAgent;
                    if (_facePlaneMode && (hoverAgent.IsInteractable || forceGrab))
                        TryGetDragFace(ray, hoverAgent.transform, GetAgentProxySize(hoverAgent),
                            hoverAgent.ShapeType == AgentShapeType.Cube, out _hoverFace, out _);
                }

                if (hitAgent != _hoveredAgent)
                {
                    if (_hoveredAgent != null && !_hoveredAgent.IsGrabbedByUser)
                    {
                        _hoveredAgent.Visuals?.SetHighlight(false);
                    }
                    _hoveredAgent = hitAgent;
                    if (_hoveredAgent != null)
                    {
                        _hoveredAgent.Visuals?.SetHighlight(true);
                    }
                }
            }

            // 2. Left Mouse Button Press: Grab interactive agent (or Alt+LMB force-grab any agent) & emit /agents/control 0 1
            if (mouse.leftButton.wasPressedThisFrame && !pointerOverHud && _grabbedAgent == null)
            {
                if (TryRaycastAgent(ray, out AgentEntity agent, out Vector3 hitPoint))
                {
                    if (agent != null)
                    {
                        if (agent.IsInteractable || forceGrab)
                        {
                            if (_facePlaneMode && !TryCaptureFace(ray, agent.transform, GetAgentProxySize(agent),
                                    agent.ShapeType == AgentShapeType.Cube)) return;
                            _grabbedAgent = agent;
                            // Plane depth is measured along the view axis at the original surface hit.
                            // A radial camera-to-center distance changes the plane immediately and makes the agent jump.
                            _grabDepthMeters = Mathf.Max(0.01f, Vector3.Dot(hitPoint - _camera.transform.position, _camera.transform.forward));
                            if (!_facePlaneMode)
                            {
                                _dragPlane = new Plane(-_camera.transform.forward, hitPoint);
                                _grabOffsetWorld = agent.transform.position - hitPoint;
                            }
                            _grabbedAgent.BeginUserGrab(forceGrab);
                        }
                        else
                        {
                            // Non-grabbable agent still emits HS-ims left-click control event (/agents/control <id> 0 1)
                            _bus?.EmitAgentControlClick(agent.AgentId, 0, 1);
                            _bus?.EmitAgentTouch(agent.AgentId, 1, -1);
                            _leftPressedAgentId = agent.AgentId;
                        }
                    }
                }
            }
            if (mouse.leftButton.wasReleasedThisFrame) ReleaseLeftClick();

            // 3. Every emitted press has a release, including long holds, camera drags, and focus loss.
            if (mouse.rightButton.wasPressedThisFrame && !pointerOverHud)
            {
                if (TryRaycastAgent(ray, out AgentEntity rightClickedAgent, out _))
                {
                    if (rightClickedAgent != null)
                    {
                        _rightPressedAgentId = rightClickedAgent.AgentId;
                        _bus?.EmitAgentControlClick(_rightPressedAgentId, 1, 1);
                    }
                }
            }
            if (mouse.rightButton.wasReleasedThisFrame) ReleaseRightClick();

            // 4. Active Agent Dragging (Plane projection + Scroll depth adjustment + [R] 90-degree rotation)
            if (_grabbedAgent != null)
            {
                if (mouse.leftButton.isPressed)
                {
                    if (_capturedFace.Target != null)
                    {
                        if (TryProjectFaceDrag(ray, out Vector3 position)) _grabbedAgent.transform.position = position;
                        return;
                    }
                    float scroll = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.01f)
                    {
                        _grabDepthMeters = Mathf.Clamp(_grabDepthMeters + scroll * 0.0018f, 0.01f, 20.0f);
                    }
                    Vector3 planePoint = _camera.transform.position + _camera.transform.forward * _grabDepthMeters;
                    _dragPlane = new Plane(-_camera.transform.forward, planePoint);

                    if (kb != null && kb[Key.R].wasPressedThisFrame)
                    {
                        _grabbedAgent.transform.Rotate(0f, 90f, 0f, Space.Self);
                    }

                    if (_dragPlane.Raycast(ray, out float enterDist))
                    {
                        _grabbedAgent.transform.position = ray.GetPoint(enterDist) + _grabOffsetWorld;
                    }
                    else
                    {
                        _grabbedAgent.transform.position = ray.GetPoint(_grabDepthMeters) + _grabOffsetWorld;
                    }
                }
                else
                {
                    _grabbedAgent.EndUserGrab();
                    _grabbedAgent = null;
                }
            }
        }

        private static float GetAgentProxySize(AgentEntity agent)
            => agent.ShapeType == AgentShapeType.Pyramid ? agent.BaseScaleMeters * Mathf.Sqrt(3f) : agent.BaseScaleMeters;

        private static bool TryGetDragFace(Ray ray, Transform target, float size, bool isCube, out DragFace face,
            out Vector3 hit, bool exactBounds = false)
        {
            face = default;
            hit = default;
            Quaternion rotation = isCube ? target.rotation : Quaternion.identity;
            if (!isCube && !exactBounds) size *= 1.12f;
            Quaternion inverse = Quaternion.Inverse(rotation);
            var localRay = new Ray(inverse * (ray.origin - target.position), inverse * ray.direction);
            if (!new Bounds(Vector3.zero, Vector3.one * size).IntersectRay(localRay, out float distance)) return false;
            Vector3 localHit = localRay.GetPoint(distance);
            Vector3 absolute = new Vector3(Mathf.Abs(localHit.x), Mathf.Abs(localHit.y), Mathf.Abs(localHit.z));
            Vector3 normal = absolute.x >= absolute.y && absolute.x >= absolute.z ? Vector3.right * Mathf.Sign(localHit.x)
                : absolute.y >= absolute.z ? Vector3.up * Mathf.Sign(localHit.y) : Vector3.forward * Mathf.Sign(localHit.z);
            Vector3 worldNormal = rotation * normal;
            if (Vector3.Dot(ray.direction, worldNormal) >= -0.1f) return false;
            face = new DragFace
            {
                Target = target,
                Size = exactBounds ? size : size * 1.025f,
                Normal = worldNormal,
                Up = rotation * (normal.y != 0f ? Vector3.forward : Vector3.up)
            };
            hit = ray.GetPoint(distance);
            return true;
        }

        private bool TryCaptureFace(Ray ray, Transform target, float size, bool isCube, bool exactBounds = false)
        {
            if (!TryGetDragFace(ray, target, size, isCube, out _capturedFace, out Vector3 hit, exactBounds)) return false;
            _dragPlane = new Plane(_capturedFace.Normal, hit);
            _grabOffsetWorld = target.position - hit;
            return true;
        }

        private bool TryProjectFaceDrag(Ray ray, out Vector3 position)
        {
            position = default;
            if (Vector3.Dot(ray.direction, _dragPlane.normal) >= -0.1f ||
                !_dragPlane.Raycast(ray, out float distance) || distance > _camera.farClipPlane) return false;
            position = ray.GetPoint(distance) + _grabOffsetWorld;
            return true;
        }

        private bool TryRaycastAgent(Ray ray, out AgentEntity agent, out Vector3 point)
        {
            agent = null;
            point = default;
            float nearest = float.PositiveInfinity;
            int count = Physics.RaycastNonAlloc(ray, _agentHits, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var hit = _agentHits[i];
                var candidate = hit.collider.GetComponentInParent<AgentEntity>();
                if (candidate == null || hit.distance >= nearest) continue;
                if (_facePlaneMode && _agentManager != null && candidate.ShapeType == AgentShapeType.Cube) continue;
                nearest = hit.distance;
                agent = candidate;
                point = hit.point;
            }
            if (_facePlaneMode && _agentManager != null)
            {
                foreach (var pair in _agentManager.ActiveAgents)
                {
                    var candidate = pair.Value;
                    if (candidate == null || !candidate.isActiveAndEnabled || candidate.ShapeType != AgentShapeType.Cube ||
                        !TryGetDragFace(ray, candidate.transform, candidate.BaseScaleMeters, true, out _, out Vector3 faceHit)) continue;
                    float distance = Vector3.Distance(ray.origin, faceHit);
                    if (distance >= nearest || distance > 30f) continue;
                    nearest = distance;
                    agent = candidate;
                    point = faceHit;
                }
            }
            // Arena boundary triggers and communication visuals must not block picking agents inside them.
            return agent != null;
        }

        private bool HandleEnvironmentInteraction(Ray ray, Mouse mouse, Keyboard keyboard, bool pointerOverHud)
        {
            if (_grabbedAgent != null) return false;
            if (_grabbedReference == null && _grabbedBoundaryHandle == null &&
                (mouse.leftButton.wasPressedThisFrame || (_facePlaneMode && !mouse.leftButton.isPressed)) && !pointerOverHud)
            {
                SpatialCalibrationReference nearestReference = null;
                BoundaryHandle nearestHandle = null;
                Vector3 hitPoint = default;
                float distance = float.PositiveInfinity;
                int count = Physics.RaycastNonAlloc(ray, _agentHits, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
                for (int i = 0; i < count; i++)
                {
                    var hit = _agentHits[i];
                    if (hit.distance >= distance) continue;
                    var candidate = hit.collider.GetComponentInParent<SpatialCalibrationReference>();
                    if (candidate != null && candidate.RotateMode) continue;
                    var handle = hit.collider.GetComponentInParent<BoundaryHandle>();
                    if ((candidate == null || !candidate.CanManipulate) && (handle == null || !handle.isActiveAndEnabled)) continue;
                    nearestReference = candidate;
                    nearestHandle = handle;
                    distance = hit.distance;
                    hitPoint = hit.point;
                }
                Vector3 ringAxis = Vector3.zero, ringStart = Vector3.zero;
                if (_calibrationReference != null && _calibrationReference.TryPickRing(ray,
                        out Vector3 axis, out Vector3 radial, out float ringDistance) && ringDistance < distance)
                {
                    nearestReference = _calibrationReference;
                    nearestHandle = null;
                    ringAxis = axis;
                    ringStart = radial;
                    distance = ringDistance;
                    hitPoint = ray.GetPoint(distance);
                }
                if ((nearestReference != null || nearestHandle != null) &&
                    (!TryRaycastAgent(ray, out _, out Vector3 agentPoint) || Vector3.Distance(ray.origin, agentPoint) >= distance))
                {
                    if (!mouse.leftButton.wasPressedThisFrame)
                    {
                        if (nearestHandle != null)
                            TryGetDragFace(ray, nearestHandle.transform, nearestHandle.SizeMeters,
                                nearestHandle.Shape == BoundaryHandleShape.Cube, out _hoverFace, out _,
                                exactBounds: nearestHandle.Shape == BoundaryHandleShape.Diamond);
                        return true;
                    }
                    if (_facePlaneMode && nearestHandle != null &&
                        !TryCaptureFace(ray, nearestHandle.transform, nearestHandle.SizeMeters,
                            nearestHandle.Shape == BoundaryHandleShape.Cube,
                            exactBounds: nearestHandle.Shape == BoundaryHandleShape.Diamond)) return true;
                    _grabbedReference = nearestReference;
                    if (nearestReference != null)
                    {
                        _referenceManipulationRevision = nearestReference.ManipulationRevision;
                        _ringAxis = ringAxis;
                        _ringStart = ringStart;
                        _ringRotation = nearestReference.Snapshot.Rotation;
                    }
                    _grabbedBoundaryHandle = nearestHandle;
                    _grabDepthMeters = Mathf.Max(0.01f, Vector3.Dot(hitPoint - _camera.transform.position, _camera.transform.forward));
                    if (_capturedFace.Target == null)
                        _grabOffsetWorld = (nearestHandle != null ? nearestHandle.transform.position : nearestReference.Snapshot.Position) - hitPoint;
                    if (_hoveredAgent != null) _hoveredAgent.Visuals?.SetHighlight(false);
                    _hoveredAgent = null;
                }
            }
            if (_grabbedReference == null && _grabbedBoundaryHandle == null) return false;
            if (!mouse.leftButton.isPressed || (_grabbedReference != null && (!_grabbedReference.CanManipulate ||
                _referenceManipulationRevision != _grabbedReference.ManipulationRevision)) ||
                (_grabbedBoundaryHandle != null && !_grabbedBoundaryHandle.isActiveAndEnabled))
            {
                _grabbedReference = null;
                _grabbedBoundaryHandle = null;
                return true;
            }
            if (_grabbedReference != null && _grabbedReference.RotateMode)
            {
                Vector3 center = _grabbedReference.Snapshot.Position;
                if (new Plane(_ringAxis, center).Raycast(ray, out float ringHit))
                {
                    Vector3 radial = ray.GetPoint(ringHit) - center;
                    if (radial.sqrMagnitude > 0.0001f)
                    {
                        float degrees = Vector3.SignedAngle(_ringStart, radial, _ringAxis);
                        _grabbedReference.TrySetPose(center, Quaternion.AngleAxis(degrees, _ringAxis) * _ringRotation);
                    }
                }
                return true;
            }
            if (_grabbedBoundaryHandle != null && _capturedFace.Target != null)
            {
                if (TryProjectFaceDrag(ray, out Vector3 facePosition)) _grabbedBoundaryHandle.MoveByUser(facePosition);
                return true;
            }
            float scroll = mouse.scroll.ReadValue().y;
            _grabDepthMeters = Mathf.Clamp(_grabDepthMeters + scroll * 0.0018f, 0.01f, 20f);
            _dragPlane = new Plane(-_camera.transform.forward, _camera.transform.position + _camera.transform.forward * _grabDepthMeters);
            if (!_dragPlane.Raycast(ray, out float enter)) return true;
            Vector3 position = ray.GetPoint(enter) + _grabOffsetWorld;
            if (_grabbedBoundaryHandle != null)
                _grabbedBoundaryHandle.MoveByUser(position);
            else
                _grabbedReference.TrySetPose(position, _grabbedReference.Snapshot.Rotation);
            return true;
        }

        private bool IsMouseOverHud(Mouse mouse)
        {
            if (IgnoredScreenGuiRect.width <= 0f || IgnoredScreenGuiRect.height <= 0f)
                return false;

            Vector2 mousePos = mouse.position.ReadValue();
            // Convert bottom-left InputSystem mouse coordinates to top-left GUI coordinates
            Vector2 guiPos = new Vector2(mousePos.x, Screen.height - mousePos.y);
            return IgnoredScreenGuiRect.Contains(guiPos);
        }
    }
}
