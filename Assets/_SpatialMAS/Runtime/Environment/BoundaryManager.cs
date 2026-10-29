using System.Collections.Generic;
using SpatialMAS.Agents;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Environment
{
    public class BoundaryInstance
    {
        public int Id;
        public AgentShapeType Shape;
        public GameObject GameObject;
        public Renderer Renderer;
        public MaterialPropertyBlock Mpb;
        public Color Color = new Color(0.2f, 0.75f, 1.0f, 0.14f);
        public Vector3 CenterUnity;
        public float SizeMeters; // Radius for Sphere, half-extent for Cube
        public Vector3 HalfExtentsMeters;
        public Quaternion RotationUnity = Quaternion.identity;
        public readonly HashSet<int> AgentsInside = new HashSet<int>();
    }

    /// <summary>
    /// Manages translucent spherical and cubic arena boundaries (/boundaries/add, /boundary/add/id, etc.)
    /// and emits both /boundaries/Touched and /boundary/event/id whenever an agent crosses a boundary surface.
    /// </summary>
    public class BoundaryManager : MonoBehaviour
    {
        [SerializeField] private Material _boundaryMaterial;

        private readonly Dictionary<int, BoundaryInstance> _boundaries = new Dictionary<int, BoundaryInstance>();
        private SimulationCommandBus _bus;
        private AgentManager _agentManager;

        public IReadOnlyDictionary<int, BoundaryInstance> ActiveBoundaries => _boundaries;

        public void Initialize(SimulationCommandBus bus, AgentManager agentManager, Material boundaryMat = null)
        {
            _bus = bus;
            _agentManager = agentManager;
            if (boundaryMat != null) _boundaryMaterial = boundaryMat;
            EnsureMaterial();

            if (_bus != null)
            {
                _bus.OnAddBoundaryUnity += HandleAddBoundary;
                _bus.OnAddBoxBoundaryUnity += HandleAddBoxBoundary;
                _bus.OnUpdateBoundaryUnity += HandleUpdateBoundary;
                _bus.OnSetBoundaryDimensionsUnity += HandleSetBoundaryDimensions;
                _bus.OnSpatialFrameChanged += HandleSpatialFrameChanged;
                _bus.OnSetBoundaryVisuals += HandleSetBoundaryVisuals;
                _bus.OnRemoveBoundary += HandleRemoveBoundary;
            }
        }

        private void OnDestroy()
        {
            if (_bus != null)
            {
                _bus.OnAddBoundaryUnity -= HandleAddBoundary;
                _bus.OnAddBoxBoundaryUnity -= HandleAddBoxBoundary;
                _bus.OnUpdateBoundaryUnity -= HandleUpdateBoundary;
                _bus.OnSetBoundaryDimensionsUnity -= HandleSetBoundaryDimensions;
                _bus.OnSpatialFrameChanged -= HandleSpatialFrameChanged;
                _bus.OnSetBoundaryVisuals -= HandleSetBoundaryVisuals;
                _bus.OnRemoveBoundary -= HandleRemoveBoundary;
            }
        }

        private void EnsureMaterial()
        {
            if (_boundaryMaterial != null) return;
            _boundaryMaterial = Resources.Load<Material>("Mat_SpatialMAS_BoundaryTranslucent");
            if (_boundaryMaterial != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _boundaryMaterial = new Material(shader)
            {
                name = "SpatialMAS_BoundaryTranslucent"
            };
            _boundaryMaterial.SetFloat("_Surface", 1f);
            _boundaryMaterial.SetFloat("_Blend", 0f);
            _boundaryMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _boundaryMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _boundaryMaterial.SetInt("_ZWrite", 0);
            _boundaryMaterial.renderQueue = 3000;
            _boundaryMaterial.color = new Color(0.2f, 0.75f, 1.0f, 0.14f);
            _boundaryMaterial.SetColor("_BaseColor", new Color(0.2f, 0.75f, 1.0f, 0.14f));
        }

        private void HandleAddBoundary(int id, AgentShapeType shape, Vector3 posUnity, float sizeMeters)
        {
            float size = Mathf.Max(0.01f, sizeMeters);
            CreateBoundary(id, shape, posUnity, size, Vector3.one * size);
        }

        private void HandleAddBoxBoundary(int id, Vector3 posUnity, Vector3 fullDimensionsMeters)
        {
            Vector3 half = new Vector3(Mathf.Max(0.005f, fullDimensionsMeters.x * 0.5f),
                Mathf.Max(0.005f, fullDimensionsMeters.y * 0.5f), Mathf.Max(0.005f, fullDimensionsMeters.z * 0.5f));
            CreateBoundary(id, AgentShapeType.Cube, posUnity, half.x, half);
        }

        private void CreateBoundary(int id, AgentShapeType shape, Vector3 posUnity, float sizeMeters, Vector3 halfExtents)
        {
            if (_boundaries.TryGetValue(id, out BoundaryInstance existing))
            {
                if (existing.GameObject != null) Destroy(existing.GameObject);
                _boundaries.Remove(id);
            }

            PrimitiveType prim = shape == AgentShapeType.Cube ? PrimitiveType.Cube : PrimitiveType.Sphere;
            GameObject go = GameObject.CreatePrimitive(prim);
            go.name = $"Boundary_{id}_{prim}";
            go.transform.SetParent(transform, false);

            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = false;
                Destroy(col); // We evaluate exact mathematical boundary crossings in FixedUpdate
            }

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = _boundaryMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var instance = new BoundaryInstance
            {
                Id = id,
                Shape = shape,
                GameObject = go,
                Renderer = renderer,
                Mpb = new MaterialPropertyBlock(),
                CenterUnity = posUnity,
                SizeMeters = sizeMeters,
                HalfExtentsMeters = halfExtents
            };

            // Pre-populate AgentsInside so spawning a boundary around existing agents doesn't fire false exit/enter events
            if (_agentManager != null)
            {
                foreach (var kvp in _agentManager.ActiveAgents)
                {
                    if (kvp.Value != null && IsPositionInsideBoundary(kvp.Value.transform.position, instance))
                    {
                        instance.AgentsInside.Add(kvp.Key);
                    }
                }
            }

            ApplyBoundaryTransform(instance);
            _boundaries[id] = instance;
        }

        private void HandleUpdateBoundary(int id, Vector3 posUnity, float sizeMeters)
        {
            if (_boundaries.TryGetValue(id, out BoundaryInstance instance))
            {
                if (!float.IsNaN(posUnity.x)) instance.CenterUnity = posUnity;
                if (sizeMeters > 0f)
                {
                    instance.SizeMeters = Mathf.Max(0.01f, sizeMeters);
                    instance.HalfExtentsMeters = Vector3.one * instance.SizeMeters;
                }
                ApplyBoundaryTransform(instance);
            }
        }

        private void HandleSetBoundaryDimensions(int id, Vector3 dimensions)
        {
            if (!_boundaries.TryGetValue(id, out BoundaryInstance instance)) return;
            if (instance.Shape == AgentShapeType.Cube)
            {
                Vector3 half = instance.HalfExtentsMeters;
                if (!float.IsNaN(dimensions.x)) half.x = Mathf.Max(0.005f, dimensions.x * 0.5f);
                if (!float.IsNaN(dimensions.y)) half.y = Mathf.Max(0.005f, dimensions.y * 0.5f);
                if (!float.IsNaN(dimensions.z)) half.z = Mathf.Max(0.005f, dimensions.z * 0.5f);
                instance.HalfExtentsMeters = half;
                instance.SizeMeters = half.x;
            }
            else instance.SizeMeters = Mathf.Max(0.01f, dimensions.x);
            ApplyBoundaryTransform(instance);
        }

        private void HandleSpatialFrameChanged(SpatialFrameChange change)
        {
            foreach (BoundaryInstance instance in _boundaries.Values)
            {
                instance.CenterUnity = change.TransformPosition(instance.CenterUnity);
                instance.RotationUnity = change.TransformRotation(instance.RotationUnity);
                instance.SizeMeters *= change.ScaleRatio;
                instance.HalfExtentsMeters *= change.ScaleRatio;
                ApplyBoundaryTransform(instance);
            }
        }

        private void HandleSetBoundaryVisuals(int id, Color color, float alpha)
        {
            if (_boundaries.TryGetValue(id, out BoundaryInstance instance))
            {
                if (color.r >= 0f)
                {
                    instance.Color.r = color.r;
                    instance.Color.g = color.g;
                    instance.Color.b = color.b;
                }
                if (alpha >= 0f)
                {
                    instance.Color.a = Mathf.Clamp01(alpha);
                }
                if (instance.Renderer != null)
                {
                    instance.Renderer.GetPropertyBlock(instance.Mpb);
                    instance.Mpb.SetColor("_BaseColor", instance.Color);
                    instance.Mpb.SetColor("_Color", instance.Color);
                    instance.Renderer.SetPropertyBlock(instance.Mpb);
                }
            }
        }

        private void HandleRemoveBoundary(int id)
        {
            if (id < 0)
            {
                foreach (var kvp in _boundaries)
                {
                    if (kvp.Value.GameObject != null) Destroy(kvp.Value.GameObject);
                }
                _boundaries.Clear();
                return;
            }

            if (_boundaries.TryGetValue(id, out BoundaryInstance inst))
            {
                if (inst.GameObject != null) Destroy(inst.GameObject);
                _boundaries.Remove(id);
            }
        }

        private static void ApplyBoundaryTransform(BoundaryInstance instance)
        {
            if (instance.GameObject == null) return;
            instance.GameObject.transform.position = instance.CenterUnity;
            instance.GameObject.transform.rotation = instance.RotationUnity;
            instance.GameObject.transform.localScale = instance.Shape == AgentShapeType.Cube
                ? instance.HalfExtentsMeters * 2f : Vector3.one * (instance.SizeMeters * 2f);
        }

        private void FixedUpdate()
        {
            if (_boundaries.Count == 0 || _agentManager == null || _agentManager.ActiveCount == 0)
                return;

            foreach (var bKvp in _boundaries)
            {
                BoundaryInstance boundary = bKvp.Value;
                foreach (var aKvp in _agentManager.ActiveAgents)
                {
                    AgentEntity agent = aKvp.Value;
                    if (agent == null) continue;

                    bool isInside = IsPositionInsideBoundary(agent.transform.position, boundary);
                    bool wasInside = boundary.AgentsInside.Contains(agent.AgentId);

                    if (isInside != wasInside)
                    {
                        if (isInside) boundary.AgentsInside.Add(agent.AgentId);
                        else boundary.AgentsInside.Remove(agent.AgentId);

                        _bus?.EmitBoundaryTouched(agent.AgentId, boundary.Id);
                        _bus?.EmitBoundaryTransition(agent.AgentId, boundary.Id, isInside ? 0 : 1);
                    }
                }
            }
        }

        public static bool IsPositionInsideBoundary(Vector3 pos, BoundaryInstance boundary)
        {
            Vector3 delta = Quaternion.Inverse(boundary.RotationUnity) * (pos - boundary.CenterUnity);
            if (boundary.Shape == AgentShapeType.Cube)
            {
                Vector3 half = boundary.HalfExtentsMeters == Vector3.zero
                    ? Vector3.one * boundary.SizeMeters : boundary.HalfExtentsMeters;
                return Mathf.Abs(delta.x) <= half.x &&
                       Mathf.Abs(delta.y) <= half.y &&
                       Mathf.Abs(delta.z) <= half.z;
            }
            else
            {
                return delta.sqrMagnitude <= (boundary.SizeMeters * boundary.SizeMeters);
            }
        }
    }
}
