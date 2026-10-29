using SpatialMAS.Audio;
using SpatialMAS.Core;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SpatialMAS.Agents
{
    /// <summary>
    /// Represents a single spatial agent in the simulation arena.
    /// Encapsulates shape mesh switching, movement interpolation (Teleport, Follow, Velocity),
    /// 3D DSP audio synthesis, optional influence radius sphere (/agents/radius/id),
    /// optional floating 3D ID label, and multiplatform XR Grab / Touch interaction.
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
        private Vector3 _smoothDampVelocity;
        private float _explicitSpeedMetersPerSec = -1f;
        private Vector3 _directionVector = Vector3.forward;
        private Vector3 _velocityVector = Vector3.zero;
        private Vector3 _lastReportedUserGrabPosition;

        // Optional child visuals (/agents/radius/id and Show Agent IDs)
        private GameObject _radiusSphereGo;
        private Renderer _radiusSphereRenderer;
        private MaterialPropertyBlock _radiusMpb;
        private TextMesh _idLabelMesh;

        private static Mesh s_SphereMesh;
        private static Mesh s_CubeMesh;

        public static void WarmupPrimitiveMeshes()
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
            ProceduralMeshFactory.GetOrCreatePyramidMesh();
            ProceduralMeshFactory.GetOrCreateArrowMesh();
        }

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            Visuals = GetComponent<AgentVisualController>();
            Synth = GetComponent<NativeDspSynthesizer>();
            AudioBank = GetComponent<SpatialAudioBankPlayer>();
            _collider = GetComponent<Collider>();
            _rigidbody = GetComponent<Rigidbody>();
            _grabInteractable = GetComponent<XRGrabInteractable>();

            WarmupPrimitiveMeshes();
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
            transform.rotation = Quaternion.identity;
            _targetPosition = transform.position;
            _smoothDampVelocity = Vector3.zero;
            _explicitSpeedMetersPerSec = -1f;
            _directionVector = Vector3.forward;
            _velocityVector = Vector3.zero;

            if (_radiusSphereGo != null) _radiusSphereGo.SetActive(false);
            if (_idLabelMesh != null) _idLabelMesh.text = id.ToString();

            ApplyShapeMeshAndCollider(shape);
            SetInteractable(shape == AgentShapeType.Cube); // Cubes are interactive by default in HS-ims examples
            Visuals.SetColor(new Color(0.15f, 0.85f, 1.0f, 1.0f));
        }

        private void ApplyShapeMeshAndCollider(AgentShapeType shape)
        {
            WarmupPrimitiveMeshes();
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
                sc.radius = 0.55f; // Slightly generous hit radius for comfortable XR & mouse picking
                sc.isTrigger = true;
                _collider = sc;
            }
        }

        private void EnsureXRIInteractableComponents()
        {
            if (_rigidbody == null)
            {
                _rigidbody = gameObject.GetComponent<Rigidbody>();
                if (_rigidbody == null) _rigidbody = gameObject.AddComponent<Rigidbody>();
            }
            _rigidbody.useGravity = false;
            _rigidbody.isKinematic = true;

            if (_grabInteractable == null)
            {
                _grabInteractable = gameObject.GetComponent<XRGrabInteractable>();
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
                }

            }

            // Prefabs can already contain an XRGrabInteractable when Awake caches it.
            // Always attach our lifecycle listeners, including when reusing a pooled agent.
            _grabInteractable.selectEntered.RemoveListener(OnXRSelectEntered);
            _grabInteractable.selectExited.RemoveListener(OnXRSelectExited);
            _grabInteractable.hoverEntered.RemoveListener(OnXRHoverEntered);
            _grabInteractable.hoverExited.RemoveListener(OnXRHoverExited);

            _grabInteractable.selectEntered.AddListener(OnXRSelectEntered);
            _grabInteractable.selectExited.AddListener(OnXRSelectExited);
            _grabInteractable.hoverEntered.AddListener(OnXRHoverEntered);
            _grabInteractable.hoverExited.AddListener(OnXRHoverExited);
        }

        public void SetInteractable(bool interactable)
        {
            IsInteractable = interactable;
            if (interactable)
            {
                EnsureXRIInteractableComponents();
                if (_grabInteractable != null) _grabInteractable.enabled = true;
            }
            else
            {
                if (_grabInteractable != null) _grabInteractable.enabled = false;
            }
        }

        public void SetTargetPosition(Vector3 posUnity)
        {
            _targetPosition = posUnity;
            if (!IsGrabbedByUser && (MovementMode == AgentMovementMode.Teleport || MovementMode == AgentMovementMode.Velocity))
            {
                transform.position = posUnity;
            }
        }

        public void PlaceImmediately(Vector3 position)
        {
            transform.position = position;
            _targetPosition = position;
            _smoothDampVelocity = Vector3.zero;
        }

        public void ApplySpatialFrameChange(SpatialFrameChange change)
        {
            transform.position = change.TransformPosition(transform.position);
            transform.rotation = change.TransformRotation(transform.rotation);
            BaseScaleMeters *= change.ScaleRatio;
            transform.localScale *= change.ScaleRatio;
            _targetPosition = change.TransformPosition(_targetPosition);
            _lastReportedUserGrabPosition = change.TransformPosition(_lastReportedUserGrabPosition);
            _velocityVector = change.TransformVector(_velocityVector);
            _smoothDampVelocity = change.TransformVector(_smoothDampVelocity);
            _directionVector = change.TransformDirection(_directionVector);
            if (_explicitSpeedMetersPerSec >= 0f) _explicitSpeedMetersPerSec *= change.ScaleRatio;
        }

        public void SetSpeedMetersPerSec(float speedMetersPerSec)
        {
            _explicitSpeedMetersPerSec = Mathf.Max(0f, speedMetersPerSec);
            if (MovementMode == AgentMovementMode.Velocity)
            {
                _velocityVector = _directionVector * _explicitSpeedMetersPerSec;
            }
        }

        public void SetDirectionUnity(Vector3 dirUnity)
        {
            if (dirUnity.sqrMagnitude > 0.00001f)
            {
                _directionVector = dirUnity.normalized;
            }
            if (MovementMode == AgentMovementMode.Velocity && _explicitSpeedMetersPerSec >= 0f)
            {
                _velocityVector = _directionVector * _explicitSpeedMetersPerSec;
            }
        }

        public void SetVelocityUnity(Vector3 velUnity)
        {
            _velocityVector = velUnity;
            _explicitSpeedMetersPerSec = velUnity.magnitude;
            if (_explicitSpeedMetersPerSec > 0.0001f)
            {
                _directionVector = velUnity / _explicitSpeedMetersPerSec;
            }
        }

        public void SetRadiusVisual(float radiusMeters, float alpha, Color color, Material translucentMaterial)
        {
            if (radiusMeters <= 0.001f || alpha <= 0.001f)
            {
                if (_radiusSphereGo != null) _radiusSphereGo.SetActive(false);
                return;
            }

            if (_radiusSphereGo == null)
            {
                _radiusSphereGo = new GameObject("RadiusVisualizer");
                _radiusSphereGo.transform.SetParent(transform, false);
                var mf = _radiusSphereGo.AddComponent<MeshFilter>();
                WarmupPrimitiveMeshes();
                mf.sharedMesh = s_SphereMesh;
                _radiusSphereRenderer = _radiusSphereGo.AddComponent<MeshRenderer>();
                _radiusSphereRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (translucentMaterial != null)
                {
                    _radiusSphereRenderer.sharedMaterial = translucentMaterial;
                }
                _radiusMpb = new MaterialPropertyBlock();
            }

            _radiusSphereGo.SetActive(true);
            float localDiameter = (radiusMeters * 2.0f) / Mathf.Max(0.005f, BaseScaleMeters);
            _radiusSphereGo.transform.localScale = Vector3.one * localDiameter;

            if (_radiusSphereRenderer != null)
            {
                Color c = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
                _radiusSphereRenderer.GetPropertyBlock(_radiusMpb);
                _radiusMpb.SetColor("_BaseColor", c);
                _radiusMpb.SetColor("_Color", c);
                _radiusSphereRenderer.SetPropertyBlock(_radiusMpb);
            }
        }

        public void SetShowIdLabel(bool show)
        {
            if (!show)
            {
                if (_idLabelMesh != null) _idLabelMesh.gameObject.SetActive(false);
                return;
            }

            if (_idLabelMesh == null)
            {
                var labelGo = new GameObject("AgentIdLabel");
                labelGo.transform.SetParent(transform, false);
                labelGo.transform.localPosition = new Vector3(0f, 0.85f, 0f);
                labelGo.transform.localScale = Vector3.one * 0.18f;
                _idLabelMesh = labelGo.AddComponent<TextMesh>();
                _idLabelMesh.anchor = TextAnchor.LowerCenter;
                _idLabelMesh.alignment = TextAlignment.Center;
                _idLabelMesh.fontSize = 36;
                _idLabelMesh.color = Color.white;
            }

            _idLabelMesh.text = AgentId.ToString();
            _idLabelMesh.gameObject.SetActive(true);
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
            else if (MovementMode == AgentMovementMode.Velocity)
            {
                transform.position += _velocityVector * Time.deltaTime;
                _targetPosition = transform.position;
            }
            else if (MovementMode == AgentMovementMode.DirectVelocity)
            {
                if (_explicitSpeedMetersPerSec >= 0f)
                {
                    transform.position = Vector3.MoveTowards(transform.position, _targetPosition, _explicitSpeedMetersPerSec * Time.deltaTime);
                }
                else
                {
                    transform.position = Vector3.SmoothDamp(transform.position, _targetPosition, ref _smoothDampVelocity, 0.04f, 25f, Time.deltaTime);
                }
            }

            if (_idLabelMesh != null && _idLabelMesh.gameObject.activeSelf && Camera.main != null)
            {
                _idLabelMesh.transform.rotation = Camera.main.transform.rotation;
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

        public void BeginUserGrab(bool forceAllowIfNonInteractable = false)
        {
            if (!IsInteractable && !forceAllowIfNonInteractable) return;
            IsGrabbedByUser = true;
            _lastReportedUserGrabPosition = transform.position;
            Visuals?.SetHighlight(true);
            _bus?.EmitAgentTouch(AgentId, 1, -1);
            _bus?.EmitAgentControlClick(AgentId, 0, 1);
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
            _bus?.EmitAgentControlClick(AgentId, 0, 0);
        }

        public void ResetForPool()
        {
            IsGrabbedByUser = false;
            if (_radiusSphereGo != null) _radiusSphereGo.SetActive(false);
            if (_idLabelMesh != null) _idLabelMesh.gameObject.SetActive(false);
            if (Synth != null)
            {
                Synth.Flush();
                Synth.Gain = 0f;
                Synth.IsMuted = false;
            }
            gameObject.SetActive(false);
        }
    }
}
