using System.Collections.Generic;
using SpatialMAS.Agents;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.LocalComm
{
    /// <summary>
    /// Pooled Expanding Wavefront Local Communication Simulator (/agents/localcomm).
    /// Replaces legacy PacketTransporter.cs (which called Instantiate/Destroy every broadcast)
    /// with a zero-allocation object pool and distance-wavefront evaluator.
    /// </summary>
    public class LocalCommManager : MonoBehaviour
    {
        private class ActiveWavefront
        {
            public string MessageType;
            public int SenderId;
            public Vector3 OriginUnity;
            public float MaxRangeMeters;
            public float DurationSeconds;
            public float ElapsedSeconds;
            public object[] ExtraArgs;
            public GameObject VisualSphere;
            public readonly HashSet<int> NotifiedReceivers = new HashSet<int>();
        }

        [SerializeField] private bool _renderWavefrontSpheres = true;
        private readonly List<ActiveWavefront> _activeWavefronts = new List<ActiveWavefront>(64);
        private readonly Stack<ActiveWavefront> _wavefrontPool = new Stack<ActiveWavefront>(64);

        private SimulationCommandBus _bus;
        private AgentManager _agentManager;
        private Material _wavefrontMaterial;

        public bool RenderWavefrontSpheres
        {
            get => _renderWavefrontSpheres;
            set => _renderWavefrontSpheres = value;
        }

        public void Initialize(SimulationCommandBus bus, AgentManager agentManager, Material wavefrontMat = null)
        {
            _bus = bus;
            _agentManager = agentManager;
            _wavefrontMaterial = wavefrontMat;

            if (_wavefrontMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _wavefrontMaterial = new Material(shader)
                {
                    name = "SpatialMAS_WavefrontMat"
                };
                _wavefrontMaterial.SetFloat("_Surface", 1f);
                _wavefrontMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _wavefrontMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _wavefrontMaterial.SetInt("_ZWrite", 0);
                _wavefrontMaterial.renderQueue = 3005;
                _wavefrontMaterial.color = new Color(0.1f, 1.0f, 0.65f, 0.18f);
                _wavefrontMaterial.SetColor("_BaseColor", new Color(0.1f, 1.0f, 0.65f, 0.18f));
            }

            if (_bus != null)
            {
                _bus.OnTriggerLocalComm += HandleTriggerLocalComm;
            }
        }

        private void OnDestroy()
        {
            if (_bus != null)
            {
                _bus.OnTriggerLocalComm -= HandleTriggerLocalComm;
            }
        }

        private void HandleTriggerLocalComm(string msgType, int senderId, float rangeMeters, float latencySec, object[] extraArgs)
        {
            if (_agentManager == null || !_agentManager.TryGetAgent(senderId, out AgentEntity sender) || sender == null)
                return;

            ActiveWavefront wf = _wavefrontPool.Count > 0 ? _wavefrontPool.Pop() : CreatePooledWavefront();
            wf.MessageType = msgType;
            wf.SenderId = senderId;
            wf.OriginUnity = sender.transform.position;
            wf.MaxRangeMeters = Mathf.Max(0.01f, rangeMeters);
            wf.DurationSeconds = Mathf.Max(0.005f, latencySec);
            wf.ElapsedSeconds = 0f;
            wf.ExtraArgs = extraArgs;
            wf.NotifiedReceivers.Clear();

            if (wf.VisualSphere != null)
            {
                wf.VisualSphere.transform.position = wf.OriginUnity;
                wf.VisualSphere.transform.localScale = Vector3.zero;
                wf.VisualSphere.SetActive(_renderWavefrontSpheres);
            }

            _activeWavefronts.Add(wf);
        }

        private ActiveWavefront CreatePooledWavefront()
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "LocalCommWavefront";
            sphere.transform.SetParent(transform, false);
            var col = sphere.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = _wavefrontMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            sphere.SetActive(false);

            return new ActiveWavefront { VisualSphere = sphere };
        }

        private void Update()
        {
            if (_activeWavefronts.Count == 0 || _agentManager == null) return;

            float dt = Time.deltaTime;
            for (int i = _activeWavefronts.Count - 1; i >= 0; i--)
            {
                ActiveWavefront wf = _activeWavefronts[i];
                wf.ElapsedSeconds += dt;
                float progress = Mathf.Clamp01(wf.ElapsedSeconds / wf.DurationSeconds);
                float currentRadius = progress * wf.MaxRangeMeters;
                float currentRadiusSqr = currentRadius * currentRadius;

                if (wf.VisualSphere != null && _renderWavefrontSpheres)
                {
                    float diameter = currentRadius * 2.0f;
                    wf.VisualSphere.transform.localScale = new Vector3(diameter, diameter, diameter);
                }

                // Check which agents have been reached by the expanding spherical wavefront
                foreach (var kvp in _agentManager.ActiveAgents)
                {
                    int receiverId = kvp.Key;
                    if (receiverId == wf.SenderId || wf.NotifiedReceivers.Contains(receiverId))
                        continue;

                    AgentEntity receiver = kvp.Value;
                    if (receiver == null) continue;

                    float distSqr = (receiver.transform.position - wf.OriginUnity).sqrMagnitude;
                    if (distSqr <= currentRadiusSqr)
                    {
                        wf.NotifiedReceivers.Add(receiverId);
                        float distMeters = Mathf.Sqrt(distSqr);
                        _bus?.EmitLocalCommDeliveredUnity(wf.MessageType, wf.SenderId, receiverId, distMeters, wf.ExtraArgs);
                    }
                }

                if (wf.ElapsedSeconds >= wf.DurationSeconds)
                {
                    if (wf.VisualSphere != null) wf.VisualSphere.SetActive(false);
                    _activeWavefronts.RemoveAt(i);
                    _wavefrontPool.Push(wf);
                }
            }
        }
    }
}
