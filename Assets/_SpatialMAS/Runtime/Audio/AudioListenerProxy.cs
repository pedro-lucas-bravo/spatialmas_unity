using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Audio
{
    /// <summary>
    /// Manages the 3D AudioListener pose across XR headsets (Meta Quest 3, Apple Vision Pro, Generic OpenXR)
    /// and Desktop. By default, locks 1:1 to the user's physical head (MainCamera) so turning one's head
    /// rotates the 3D soundfield naturally. Can be detached via OSC (/listener/position or /xr/listener/lock_to_head 0).
    /// </summary>
    public class AudioListenerProxy : MonoBehaviour
    {
        [SerializeField] private bool _lockedToHead = true;
        [SerializeField] private Renderer _proxyVisualRenderer;

        private Transform _headTransform;
        private SimulationCommandBus _bus;
        private AudioListener _listener;

        public bool LockedToHead
        {
            get => _lockedToHead;
            set
            {
                _lockedToHead = value;
                UpdateVisualVisibility();
            }
        }

        public void Initialize(SimulationCommandBus bus, Transform headCameraTransform, bool lockToHeadDefault = true)
        {
            _bus = bus;
            _headTransform = headCameraTransform;
            _lockedToHead = lockToHeadDefault;

            // OSC listener overrides must move the component that actually receives audio.
            // Older generated scenes put it on Main Camera, so transfer ownership at runtime.
            _listener = GetComponent<AudioListener>();
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Include))
                if (listener != _listener) listener.enabled = false;
            if (_listener == null) _listener = gameObject.AddComponent<AudioListener>();
            _listener.enabled = true;
            if (_lockedToHead && _headTransform != null)
                transform.SetPositionAndRotation(_headTransform.position, _headTransform.rotation);

            if (_bus != null)
            {
                _bus.OnSetListenerPositionUnity += HandleSetPosition;
                _bus.OnSetListenerRotationUnity += HandleSetRotation;
                _bus.OnSetListenerLockedToHead += HandleSetLockedToHead;
            }

            UpdateVisualVisibility();
        }

        private void OnDestroy()
        {
            if (_bus != null)
            {
                _bus.OnSetListenerPositionUnity -= HandleSetPosition;
                _bus.OnSetListenerRotationUnity -= HandleSetRotation;
                _bus.OnSetListenerLockedToHead -= HandleSetLockedToHead;
            }
        }

        private void LateUpdate()
        {
            if (_lockedToHead)
            {
                if (_headTransform == null && Camera.main != null)
                {
                    _headTransform = Camera.main.transform;
                }

                if (_headTransform != null)
                {
                    transform.SetPositionAndRotation(_headTransform.position, _headTransform.rotation);
                }
            }
        }

        private void HandleSetPosition(Vector3 posUnity)
        {
            // Receiving an explicit /listener/position from an external controller detaches from head if desired
            if (!_lockedToHead)
            {
                transform.position = posUnity;
            }
        }

        private void HandleSetRotation(Quaternion rotUnity)
        {
            if (!_lockedToHead)
            {
                transform.rotation = rotUnity;
            }
        }

        private void HandleSetLockedToHead(bool locked)
        {
            LockedToHead = locked;
        }

        private void UpdateVisualVisibility()
        {
            if (_proxyVisualRenderer != null)
            {
                // Hide the proxy head mesh when locked to the user's physical head so it never occludes the camera
                _proxyVisualRenderer.enabled = !_lockedToHead;
            }
        }
    }
}
