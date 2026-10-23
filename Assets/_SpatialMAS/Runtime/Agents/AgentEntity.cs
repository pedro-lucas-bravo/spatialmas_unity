using SpatialMAS.Audio;
using SpatialMAS.Core;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SpatialMAS.Agents
{
    /// <summary>
    /// Represents a single spatial agent in the simulation arena.
    /// Encapsulates shape mesh switching, movement interpolation, 3D DSP audio synthesis,
    /// and multiplatform XR Grab / Touch interaction (Meta Quest Hands, Vision Pro Gaze-Pinch, Desktop Mouse).
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(AgentVisualController))]
    [RequireComponent(typeof(NativeDspSynthesizer))]
    [RequireComponent(typeof(SpatialAudioBankPlayer))]
    public class AgentEntity : MonoBehaviour
    {
        public int AgentId { get; private set; } = -1;
        public AgentShapeType ShapeType { get; private set; } = AgentShapeType.Sphere;
        public AgentMovementMode MovementMode { get; private set; } = AgentMovementMode.Teleport;
        public float BaseScaleMeters { get; private set; } = 0.08f;
        public bool IsInteractable { get; private set; }
        public bool IsGrabbedByUser { get; private set; }
        public bool SnapRotation90DegreesOnRelease { get; set; }

        public AgentVisualController Visuals { get; private set; }
        public IAgentAudioSynth Synth { get; private set; }
        public SpatialAudioBankPlayer AudioBank { get; private set; }

        private SimulationCommandBus _bus;
        private MeshFilter _meshFilter;
        private Collider _collider;
        private Rigidbody _rigidbody;
        private XRGrabInteractable _grabInteractable;

        private Vector3 _targetPosition;
        private Vector3 _velocity;
        private Vector3 _lastReportedUserGrabPosition;

        private static Mesh s_SphereMesh;
        private static Mesh s_CubeMesh;

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            Visuals = GetComponent<AgentVisualController>();
            Synth = GetComponent<NativeDspSynthesizer>();
            AudioBank = GetComponent<SpatialAudioBankPlayer>();
            _collider = GetComponent<Collider>();
            _rigidbody = GetComponent<Rigidbody>();
            _grabInteractable = GetComponent<XRGrabInteractable>();

            CacheUnityPrimitiveMeshesIfNeeded();
        }

        private static void CacheUnityPrimitiveMeshesIfNeeded()
        {
            if (s_SphereMesh == null)
            {
                var tempSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s_SphereMesh = tempSphere.GetComponent<MeshFilter>().sharedMesh;
                if (Application.isPlaying) Destroy(tempSphere);
                else DestroyImmediate(tempSphere);
            }
            if (s_CubeMesh == null)
            {
                var tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s_CubeMesh = tempCube.GetComponent<MeshFilter>().sharedMesh;
                if (Application.isPlaying) Destroy(tempCube);
                else DestroyImmediate(tempCube);
            }
        }

        public void Configure(
            int id,
            AgentShapeType shape,
            AgentMovementMode movMode,
            float scaleMeters,
            SimulationCommandBus bus)
        {
            AgentId = id;
            ShapeType = shape;
            MovementMode = movMode;
            BaseScaleMeters = Mathf.Max(0.005f, scaleMeters);
            _bus = bus;
            SnapRotation90DegreesOnRelease = false;

            name = $"Agent_{id}_{shape}";
            transform.localScale = Vector3.one * BaseScaleMeters;
            _targetPosition = transform.position;
            _velocity = Vector3.zero;

            ApplyShapeMeshAndCollider(shape);
            EnsurePhysicsAndXRI();
            SetInteractable(shape == AgentShapeType.Cube); // Cubes are interactive by default in HS-ims examples
            Visuals.SetColor(new Color(0.15f, 0.85f, 1.0f, 1.0f));
        }

        private void ApplyShapeMeshAndCollider(AgentShapeType shape)
        {
            CacheUnityPrimitiveMeshesIfNeeded();
            if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();

            switch (shape)
            {
                case AgentShapeType.Cube:
                    _meshFilter.sharedMesh = s_CubeMesh;
                    break;
                case AgentShapeType.Pyramid:
                    _meshFilter.sharedMesh = ProceduralMeshFactory.GetOrCreatePyramidMesh();
                    break;
                case AgentShapeType.Sphere:
                default:
                    _meshFilter.sharedMesh = s_SphereMesh;
                    break;
            }

            if (_collider == null)
            {
                var sc = gameObject.AddComponent<SphereCollider>();
                sc.radius = 0.5f;
                sc.isTrigger = true;
                _collider = sc;
            }
        }

        private void EnsurePhysicsAndXRI()
        {
            if (_rigidbody == null)
            {
                _rigidbody = gameObject.AddComponent<Rigidbody>();
            }
            _rigidbody.useGravity = false;
            _rigidbody.isKinematic = true;

            if (_grabInteractable == null)
            {
                _grabInteractable = gameObject.AddComponent<XRGrabInteractable>();
                _grabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;
                _grabInteractable.throwOnDetach = false;
                _grabInteractable.useDynamicAttach = true;
                _grabInteractable.smoothPosition = true;
                _grabInteractable.smoothPositionAmount = 16.0f;
                _grabInteractable.smoothRotation = true;
                _grabInteractable.smoothRotationAmount = 16.0f;

                _grabInteractable.selectEntered.AddListener(OnXRSelectEntered);
                _grabInteractable.selectExited.AddListener(OnXRSelectExited);
                _grabInteractable.hoverEntered.AddListener(OnXRHoverEntered);
                _grabInteractable.hoverExited.AddListener(OnXRHoverExited);
            }
        }

        public void SetInteractable(bool interactable)
        {
            IsInteractable = interactable;
            if (_grabInteractable != null)
            {
                _grabInteractable.enabled = interactable;
            }
        }

        public void SetTargetPosition(Vector3 posUnity)
        {
            _targetPosition = posUnity;
            if (!IsGrabbedByUser && MovementMode == AgentMovementMode.Teleport)
            {
                transform.position = posUnity;
            }
        }

        private void Update()
        {
            if (IsGrabbedByUser)
            {
                if (Vector3.SqrMagnitude(transform.position - _lastReportedUserGrabPosition) > 0.000004f)
                {
                    _lastReportedUserGrabPosition = transform.position;
                    _targetPosition = transform.position;
                    _bus?.EmitAgentMovedByUserUnity(AgentId, transform.position);
                }
            }
            else if (MovementMode == AgentMovementMode.DirectVelocity)
            {
                transform.position = Vector3.SmoothDamp(transform.position, _targetPosition, ref _velocity, 0.04f, 25f, Time.deltaTime);
            }
        }

        // --- XR Hand / Gaze-Pinch / Desktop Mouse Grab Lifecycle ---
        private void OnXRSelectEntered(SelectEnterEventArgs args)
        {
            BeginUserGrab();
        }

        private void OnXRSelectExited(SelectExitEventArgs args)
        {
            EndUserGrab();
        }

        private void OnXRHoverEntered(HoverEnterEventArgs args)
        {
            Visuals?.SetHighlight(true);
            _bus?.EmitAgentTouch(AgentId, 1, -1);
        }

        private void OnXRHoverExited(HoverExitEventArgs args)
        {
            if (!IsGrabbedByUser)
            {
                Visuals?.SetHighlight(false);
            }
            _bus?.EmitAgentTouch(AgentId, 0, -1);
        }

        public void BeginUserGrab()
        {
            if (!IsInteractable) return;
            IsGrabbedByUser = true;
            _lastReportedUserGrabPosition = transform.position;
            Visuals?.SetHighlight(true);
            _bus?.EmitAgentTouch(AgentId, 1, -1);
        }

        public void EndUserGrab()
        {
            if (!IsGrabbedByUser) return;
            IsGrabbedByUser = false;
            _targetPosition = transform.position;

            if (SnapRotation90DegreesOnRelease)
            {
                Vector3 euler = transform.eulerAngles;
                euler.x = Mathf.Round(euler.x / 90f) * 90f;
                euler.y = Mathf.Round(euler.y / 90f) * 90f;
                euler.z = Mathf.Round(euler.z / 90f) * 90f;
                transform.eulerAngles = euler;
            }

            Visuals?.SetHighlight(false);
            _bus?.EmitAgentMovedByUserUnity(AgentId, transform.position);
            _bus?.EmitAgentTouch(AgentId, 0, -1);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other == null) return;
            var otherAgent = other.GetComponent<AgentEntity>();
            if (otherAgent != null && otherAgent.AgentId != AgentId)
            {
                _bus?.EmitAgentTouch(AgentId, 1, otherAgent.AgentId);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null) return;
            var otherAgent = other.GetComponent<AgentEntity>();
            if (otherAgent != null && otherAgent.AgentId != AgentId)
            {
                _bus?.EmitAgentTouch(AgentId, 0, otherAgent.AgentId);
            }
        }

        public void ResetForPool()
        {
            IsGrabbedByUser = false;
            if (Synth != null)
            {
                Synth.SetNote(0, 60, 0);
                Synth.Gain = 0f;
                Synth.IsMuted = false;
            }
            transform.SetParent(null, false);
            gameObject.SetActive(false);
        }
    }
}
