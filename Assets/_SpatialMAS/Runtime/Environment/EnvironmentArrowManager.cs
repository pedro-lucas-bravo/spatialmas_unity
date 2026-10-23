using System.Collections.Generic;
using SpatialMAS.Agents;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Environment
{
    /// <summary>
    /// Renders 3D directional arrows in the arena (/environment/set_arrow and /environment/remove_arrow).
    /// </summary>
    public class EnvironmentArrowManager : MonoBehaviour
    {
        private readonly Dictionary<int, GameObject> _arrows = new Dictionary<int, GameObject>();
        private SimulationCommandBus _bus;
        private Material _arrowMaterial;

        public void Initialize(SimulationCommandBus bus, Material arrowMaterial = null)
        {
            _bus = bus;
            _arrowMaterial = arrowMaterial;
            if (_arrowMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _arrowMaterial = new Material(shader)
                {
                    name = "SpatialMAS_ArrowMat",
                    color = new Color(1.0f, 0.78f, 0.1f, 1.0f)
                };
                _arrowMaterial.SetColor("_BaseColor", new Color(1.0f, 0.78f, 0.1f, 1.0f));
            }

            if (_bus != null)
            {
                _bus.OnSetArrowUnity += HandleSetArrow;
                _bus.OnRemoveArrow += HandleRemoveArrow;
            }
        }

        private void OnDestroy()
        {
            if (_bus != null)
            {
                _bus.OnSetArrowUnity -= HandleSetArrow;
                _bus.OnRemoveArrow -= HandleRemoveArrow;
            }
        }

        private void HandleSetArrow(int id, Vector3 startUnity, Vector3 endUnity, float widthMeters)
        {
            if (!_arrows.TryGetValue(id, out GameObject go) || go == null)
            {
                go = new GameObject($"EnvironmentArrow_{id}");
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = ProceduralMeshFactory.GetOrCreateArrowMesh();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _arrowMaterial;
                _arrows[id] = go;
            }

            Vector3 dir = endUnity - startUnity;
            float length = dir.magnitude;
            if (length < 0.0001f)
            {
                go.SetActive(false);
                return;
            }

            go.SetActive(true);
            go.transform.position = startUnity;
            go.transform.rotation = Quaternion.LookRotation(dir / length);
            float w = Mathf.Max(0.005f, widthMeters);
            go.transform.localScale = new Vector3(w, w, length);
        }

        private void HandleRemoveArrow(int id)
        {
            if (id < 0)
            {
                foreach (var kvp in _arrows)
                {
                    if (kvp.Value != null) Destroy(kvp.Value);
                }
                _arrows.Clear();
                return;
            }

            if (_arrows.TryGetValue(id, out GameObject go))
            {
                if (go != null) Destroy(go);
                _arrows.Remove(id);
            }
        }
    }
}
