using SpatialMAS.Agents;
using SpatialMAS.Core;
using UnityEngine;
using UnityEngine.InputSystem;

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
        private AgentEntity _hoveredAgent;
        private AgentEntity _grabbedAgent;
        private AgentEntity _rightClickedAgent;
        private float _rightClickDownTime;
        private Vector2 _rightClickDownPos;

        private Plane _dragPlane;
        private Vector3 _grabOffsetWorld;
        private float _grabDistanceMeters;

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
        public bool IsFpsCursorLocked => _fpsCursorLockMode;

        public void Initialize(Camera cam, SimulationCommandBus bus = null)
        {
            _camera = cam != null ? cam : Camera.main;
            _bus = bus;
            if (_camera != null)
            {
                _defaultCameraPosition = _camera.transform.position;
                _defaultCameraRotation = _camera.transform.rotation;
                SyncAnglesFromCamera();
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
            if (_camera == null) return;

            _camera.transform.SetPositionAndRotation(_defaultCameraPosition, _defaultCameraRotation);
            _currentMoveVelocity = Vector3.zero;
            SyncAnglesFromCamera();
        }

        public void ToggleFpsCursorLock()
        {
            SetFpsCursorLock(!_fpsCursorLockMode);
        }

        public void SetFpsCursorLock(bool locked)
        {
            _fpsCursorLockMode = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void OnDisable()
        {
            if (_fpsCursorLockMode)
            {
                SetFpsCursorLock(false);
            }
        }

        private void Update()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

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
            HandleKeyboardMovement(kb);

            if (mouse == null) return;

            // 2. Mouse Look (RMB hold OR FPS lock mode), MMB Pan, and Scroll Zoom
            HandleMouseCameraControl(mouse, kb);

            // 3. Hover detection, LMB 3D Plane/Ray Grab, and LMB/RMB /agents/control clicks
            HandleMouseAgentInteraction(mouse, kb);
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
                _camera.transform.position += _currentMoveVelocity * dt;
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
                _yaw += delta.x * _lookSensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * _lookSensitivity, -88f, 88f);
                _camera.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            // Middle Mouse Button Pan (mirrors HS-ims FirstPersonCamera.cs)
            if (mouse.middleButton.isPressed)
            {
                Vector3 panOffset = (-_camera.transform.right * delta.x - _camera.transform.up * delta.y) * _panSensitivity;
                _camera.transform.position += panOffset;
            }

            // Scroll Wheel Dolly/Zoom when not currently grabbing an agent (and pointer not over HUD)
            if (_grabbedAgent == null && !IsMouseOverHud(mouse))
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    float zoomStep = scroll * _scrollZoomSpeed;
                    _camera.transform.position += _camera.transform.forward * zoomStep;
                }
            }
        }

        private void HandleMouseAgentInteraction(Mouse mouse, Keyboard kb)
        {
            bool pointerOverHud = !_fpsCursorLockMode && IsMouseOverHud(mouse);
            Vector2 screenPos = _fpsCursorLockMode
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : mouse.position.ReadValue();

            Ray ray = _camera.ScreenPointToRay(screenPos);

            // 1. Update Hovered Agent when not dragging
            if (_grabbedAgent == null)
            {
                AgentEntity hitAgent = null;
                if (!pointerOverHud && Physics.Raycast(ray, out RaycastHit hoverHit, 30.0f))
                {
                    hitAgent = hoverHit.collider.GetComponentInParent<AgentEntity>();
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
                if (Physics.Raycast(ray, out RaycastHit hit, 30.0f))
                {
                    var agent = hit.collider.GetComponentInParent<AgentEntity>();
                    if (agent != null)
                    {
                        bool forceGrab = kb != null && (kb[Key.LeftAlt].isPressed || kb[Key.RightAlt].isPressed);
                        if (agent.IsInteractable || forceGrab)
                        {
                            _grabbedAgent = agent;
                            _grabDistanceMeters = Mathf.Max(0.20f, Vector3.Distance(_camera.transform.position, agent.transform.position));
                            _dragPlane = new Plane(-_camera.transform.forward, hit.point);
                            _grabOffsetWorld = agent.transform.position - hit.point;
                            _grabbedAgent.BeginUserGrab(forceGrab);
                        }
                        else
                        {
                            // Non-grabbable agent still emits HS-ims left-click control event (/agents/control <id> 0 1)
                            _bus?.EmitAgentControlClick(agent.AgentId, 0, 1);
                            _bus?.EmitAgentTouch(agent.AgentId, 1, -1);
                        }
                    }
                }
            }

            // 3. Right Mouse Button Click on Agent: emit HS-ims /agents/control <id> 1 1/0 if quick click (not camera drag)
            if (mouse.rightButton.wasPressedThisFrame && !pointerOverHud)
            {
                _rightClickDownTime = Time.unscaledTime;
                _rightClickDownPos = screenPos;
                if (Physics.Raycast(ray, out RaycastHit rHit, 30.0f))
                {
                    _rightClickedAgent = rHit.collider.GetComponentInParent<AgentEntity>();
                    if (_rightClickedAgent != null)
                    {
                        _bus?.EmitAgentControlClick(_rightClickedAgent.AgentId, 1, 1);
                    }
                }
            }
            if (mouse.rightButton.wasReleasedThisFrame && _rightClickedAgent != null)
            {
                if (Time.unscaledTime - _rightClickDownTime < 0.35f &&
                    Vector2.Distance(screenPos, _rightClickDownPos) < 8.0f)
                {
                    _bus?.EmitAgentControlClick(_rightClickedAgent.AgentId, 1, 0);
                }
                _rightClickedAgent = null;
            }

            // 4. Active Agent Dragging (Plane projection + Scroll depth adjustment + [R] 90-degree rotation)
            if (_grabbedAgent != null)
            {
                if (mouse.leftButton.isPressed)
                {
                    float scroll = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.01f)
                    {
                        _grabDistanceMeters = Mathf.Clamp(_grabDistanceMeters + scroll * 0.0018f, 0.18f, 20.0f);
                        Vector3 newPlaneOrigin = _camera.transform.position + ray.direction * _grabDistanceMeters;
                        _dragPlane = new Plane(-_camera.transform.forward, newPlaneOrigin);
                        _grabOffsetWorld = Vector3.zero;
                    }
                    else
                    {
                        // Keep drag plane normal aligned with camera view direction
                        Vector3 planePoint = _camera.transform.position + _camera.transform.forward * _grabDistanceMeters;
                        _dragPlane = new Plane(-_camera.transform.forward, planePoint);
                    }

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
                        _grabbedAgent.transform.position = ray.GetPoint(_grabDistanceMeters);
                    }
                }
                else
                {
                    _grabbedAgent.EndUserGrab();
                    _grabbedAgent = null;
                }
            }
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
