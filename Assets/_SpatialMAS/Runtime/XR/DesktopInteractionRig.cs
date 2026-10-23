using SpatialMAS.Agents;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpatialMAS.XR
{
    /// <summary>
    /// Provides zero-setup Desktop Mouse & Keyboard navigation and interactive agent grabbing
    /// when running on Windows, macOS, or Linux without an XR headset attached:
    /// - Hold Right Mouse Button (RMB) + WASDQE: Walk and look through the 3D arena (moving the Camera/Head).
    /// - Left Mouse Button (LMB) Click & Drag on any interactive agent: Grab and move the agent in 3D space.
    /// - Scroll Wheel while grabbing: Push/pull grabbed agent along the camera ray.
    /// - Press 'R' while grabbing: Rotate grabbed cube by 90 degrees (for Self-Assembly face alignment).
    /// </summary>
    public class DesktopInteractionRig : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private float _moveSpeed = 2.2f;
        [SerializeField] private float _lookSensitivity = 0.18f;

        private AgentEntity _grabbedAgent;
        private float _grabDistanceMeters;
        private float _yaw;
        private float _pitch;
        private bool _anglesInitialized;

        public AgentEntity CurrentlyGrabbedAgent => _grabbedAgent;

        public void Initialize(Camera cam)
        {
            _camera = cam != null ? cam : Camera.main;
            if (_camera != null)
            {
                Vector3 euler = _camera.transform.eulerAngles;
                _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
                _yaw = euler.y;
                _anglesInitialized = true;
            }
        }

        private void Update()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse == null) return;

            HandleCameraFlyNavigation(mouse, kb);
            HandleMouseAgentGrab(mouse, kb);
        }

        private void HandleCameraFlyNavigation(Mouse mouse, Keyboard kb)
        {
            if (!mouse.rightButton.isPressed) return;

            if (!_anglesInitialized)
            {
                Vector3 euler = _camera.transform.eulerAngles;
                _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
                _yaw = euler.y;
                _anglesInitialized = true;
            }

            Vector2 delta = mouse.delta.ReadValue();
            _yaw += delta.x * _lookSensitivity;
            _pitch = Mathf.Clamp(_pitch - delta.y * _lookSensitivity, -85f, 85f);
            _camera.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            if (kb == null) return;

            Vector3 move = Vector3.zero;
            if (kb[Key.W].isPressed) move += _camera.transform.forward;
            if (kb[Key.S].isPressed) move -= _camera.transform.forward;
            if (kb[Key.D].isPressed) move += _camera.transform.right;
            if (kb[Key.A].isPressed) move -= _camera.transform.right;
            if (kb[Key.E].isPressed) move += Vector3.up;
            if (kb[Key.Q].isPressed) move -= Vector3.up;

            if (move.sqrMagnitude > 0.001f)
            {
                float speed = _moveSpeed * (kb[Key.LeftShift].isPressed ? 2.2f : 1.0f);
                _camera.transform.position += move.normalized * (speed * Time.unscaledDeltaTime);
            }
        }

        private void HandleMouseAgentGrab(Mouse mouse, Keyboard kb)
        {
            if (mouse.leftButton.wasPressedThisFrame && _grabbedAgent == null)
            {
                Ray ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
                if (Physics.Raycast(ray, out RaycastHit hit, 25.0f))
                {
                    var agent = hit.collider.GetComponentInParent<AgentEntity>();
                    if (agent != null && agent.IsInteractable)
                    {
                        _grabbedAgent = agent;
                        _grabDistanceMeters = Mathf.Max(0.25f, hit.distance);
                        _grabbedAgent.BeginUserGrab();
                    }
                }
            }

            if (_grabbedAgent != null)
            {
                if (mouse.leftButton.isPressed)
                {
                    float scroll = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.01f)
                    {
                        _grabDistanceMeters = Mathf.Clamp(_grabDistanceMeters + scroll * 0.0015f, 0.2f, 15.0f);
                    }

                    if (kb != null && kb[Key.R].wasPressedThisFrame)
                    {
                        _grabbedAgent.transform.Rotate(0f, 90f, 0f, Space.Self);
                    }

                    Ray ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
                    _grabbedAgent.transform.position = ray.GetPoint(_grabDistanceMeters);
                }
                else
                {
                    _grabbedAgent.EndUserGrab();
                    _grabbedAgent = null;
                }
            }
        }
    }
}
