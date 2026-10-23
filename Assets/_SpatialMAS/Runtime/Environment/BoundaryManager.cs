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
        public Vector3 CenterUnity;
        public float SizeMeters; // Radius for Sphere, half-extent for Cube
        public readonly HashSet<int> AgentsInside = new HashSet<int>();
    }

    /// <summary>
    /// Manages translucent spherical and cubic arena boundaries (/boundaries/add, /update, /remove)
    /// and emits /boundaries/Touched whenever an agent crosses a boundary surface.
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
                _bus.OnUpdateBoundaryUnity += HandleUpdateBoundary;
                _bus.OnRemoveBoundary += HandleRemoveBoundary;
            }
        }

        private void OnDestroy()
        {
            if (_bus != null)
            {
                _bus.OnAddBoundaryUnity -= HandleAddBoundary;
                _bus.OnUpdateBoundaryUnity -= HandleUpdateBoundary;
                _bus.OnRemoveBoundary -= HandleRemoveBoundary;
            }
        }

        private void EnsureMaterial()
        {
            if (_boundaryMaterial != null) return;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            _boundaryMaterial = new Material(shader)
            {
                name = "SpatialMAS_BoundaryTranslucent"
            };
            // Configure transparent surface for URP Lit
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
            if (col != null) Destroy(col); // We evaluate exact mathematical boundary crossings in FixedUpdate

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
                CenterUnity = posUnity,
                SizeMeters = Mathf.Max(0.01f, sizeMeters)
            };

            ApplyBoundaryTransform(instance);
            _boundaries[id] = instance;
        }

        private void HandleUpdateBoundary(int id, Vector3 posUnity, float sizeMeters)
        {
            if (_boundaries.TryGetValue(id, out BoundaryInstance instance))
            {
                instance.CenterUnity = posUnity;
                instance.SizeMeters = Mathf.Max(0.01f, sizeMeters);
                ApplyBoundaryTransform(instance);
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
            // In HS-ims, sphere size is radius (so diameter = size * 2), cube size is side/extent
            float diameter = instance.SizeMeters * 2.0f;
            instance.GameObject.transform.localScale = new Vector3(diameter, diameter, diameter);
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
                    }
                }
            }
        }

        public static bool IsPositionInsideBoundary(Vector3 pos, BoundaryInstance boundary)
        {
            Vector3 delta = pos - boundary.CenterUnity;
            if (boundary.Shape == AgentShapeType.Cube)
            {
                float half = boundary.SizeMeters;
                return Mathf.Abs(delta.x) <= half &&
                       Mathf.Abs(delta.y) <= half &&
                       Mathf.Abs(delta.z) <= half;
            }
            else
            {
                return delta.sqrMagnitude <= (boundary.SizeMeters * boundary.SizeMeters);
            }
        }
    }
}
