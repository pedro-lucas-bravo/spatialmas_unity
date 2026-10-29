using System.Collections.Generic;
using SpatialMAS.Audio;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Agents
{
    /// <summary>
    /// High-performance pooled Agent Manager.
    /// Handles instantiation, addition, removal, batch transform updates, speed/direction/velocity updates,
    /// color updates, radius visualization, floating ID labels, and synth/audio routing.
    /// </summary>
    public class AgentManager : MonoBehaviour
    {
        public const int MaxSafeAgentsCount = 512;

        [SerializeField] private Material _defaultAgentMaterial;
        [SerializeField] private Material _translucentRadiusMaterial;

        private readonly Dictionary<int, AgentEntity> _activeAgents = new Dictionary<int, AgentEntity>(256);
        private readonly List<int> _orderedAgentIds = new List<int>(256);
        private readonly Stack<AgentEntity> _agentPool = new Stack<AgentEntity>(256);

        private SimulationCommandBus _bus;
        private Transform _agentsRoot;
        private bool _showAgentIds;

        public IReadOnlyDictionary<int, AgentEntity> ActiveAgents => _activeAgents;
        public IReadOnlyList<int> OrderedAgentIds => _orderedAgentIds;
        public int ActiveCount => _activeAgents.Count;

        public bool ShowAgentIds
        {
            get => _showAgentIds;
            set => HandleSetShowAgentIds(value);
        }

        public void Initialize(SimulationCommandBus bus, Material defaultMaterial = null, Material translucentMaterial = null)
        {
            _bus = bus;
            if (defaultMaterial != null) _defaultAgentMaterial = defaultMaterial;
            if (translucentMaterial != null) _translucentRadiusMaterial = translucentMaterial;

            if (_agentsRoot == null)
            {
                Transform existing = transform.Find("AgentsContainer");
                GameObject rootGo = existing != null ? existing.gameObject : new GameObject("AgentsContainer");
                rootGo.transform.SetParent(transform, false);
                _agentsRoot = rootGo.transform;
            }

            EnsureMaterials();
            AgentEntity.WarmupPrimitiveMeshes();

            if (_bus != null)
            {
                _bus.OnInstantiateAgents += HandleInstantiateAgents;
                _bus.OnAddAgents += HandleAddAgents;
                _bus.OnRemoveAgents += HandleRemoveAgents;
                _bus.OnSetAgentPositionUnity += HandleSetAgentPosition;
                _bus.OnSetBatchAgentPositionsUnity += HandleSetBatchAgentPositions;
                _bus.OnSetAgentSpeedMetersPerSec += HandleSetAgentSpeed;
                _bus.OnSetAgentDirectionUnity += HandleSetAgentDirection;
                _bus.OnSetAgentVelocityUnity += HandleSetAgentVelocity;
                _bus.OnSetAgentColor += HandleSetAgentColor;
                _bus.OnSetBatchAgentColors += HandleSetBatchAgentColors;
                _bus.OnSetAgentRadiusVisual += HandleSetAgentRadiusVisual;
                _bus.OnSetAgentInteractable += HandleSetAgentInteractable;
                _bus.OnSetShowAgentIds += HandleSetShowAgentIds;
                _bus.OnSetAgentMute += HandleSetAgentMute;
                _bus.OnSetAgentSynthNote += HandleSetAgentSynthNote;
                _bus.OnSetAgentSynthParam += HandleSetAgentSynthParam;
                _bus.OnFlushAgentSynth += HandleFlushAgentSynth;
                _bus.OnSetAgentAudioBank += HandleSetAgentAudioBank;
            }
        }

        private void OnDestroy()
        {
            if (_bus != null)
            {
                _bus.OnInstantiateAgents -= HandleInstantiateAgents;
                _bus.OnAddAgents -= HandleAddAgents;
                _bus.OnRemoveAgents -= HandleRemoveAgents;
                _bus.OnSetAgentPositionUnity -= HandleSetAgentPosition;
                _bus.OnSetBatchAgentPositionsUnity -= HandleSetBatchAgentPositions;
                _bus.OnSetAgentSpeedMetersPerSec -= HandleSetAgentSpeed;
                _bus.OnSetAgentDirectionUnity -= HandleSetAgentDirection;
                _bus.OnSetAgentVelocityUnity -= HandleSetAgentVelocity;
                _bus.OnSetAgentColor -= HandleSetAgentColor;
                _bus.OnSetBatchAgentColors -= HandleSetBatchAgentColors;
                _bus.OnSetAgentRadiusVisual -= HandleSetAgentRadiusVisual;
                _bus.OnSetAgentInteractable -= HandleSetAgentInteractable;
                _bus.OnSetShowAgentIds -= HandleSetShowAgentIds;
                _bus.OnSetAgentMute -= HandleSetAgentMute;
                _bus.OnSetAgentSynthNote -= HandleSetAgentSynthNote;
                _bus.OnSetAgentSynthParam -= HandleSetAgentSynthParam;
                _bus.OnFlushAgentSynth -= HandleFlushAgentSynth;
                _bus.OnSetAgentAudioBank -= HandleSetAgentAudioBank;
            }
        }

        private void EnsureMaterials()
        {
            if (_defaultAgentMaterial == null)
            {
                _defaultAgentMaterial = Resources.Load<Material>("Mat_SpatialMAS_AgentInstanced");
            }
            if (_defaultAgentMaterial == null)
            {
                Shader urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _defaultAgentMaterial = new Material(urpLit)
                {
                    name = "SpatialMAS_AgentLit_Instanced",
                    enableInstancing = true
                };
                _defaultAgentMaterial.SetFloat("_Smoothness", 0.78f);
            }

            if (_translucentRadiusMaterial == null)
            {
                _translucentRadiusMaterial = Resources.Load<Material>("Mat_SpatialMAS_BoundaryTranslucent");
            }
            if (_translucentRadiusMaterial == null)
            {
                Shader urpLit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _translucentRadiusMaterial = new Material(urpLit)
                {
                    name = "SpatialMAS_RadiusTranslucent"
                };
                _translucentRadiusMaterial.SetFloat("_Surface", 1f);
                _translucentRadiusMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _translucentRadiusMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _translucentRadiusMaterial.SetInt("_ZWrite", 0);
                _translucentRadiusMaterial.renderQueue = 3002;
            }
        }

        public bool TryGetAgent(int id, out AgentEntity entity)
        {
            return _activeAgents.TryGetValue(id, out entity);
        }

        public void ClearAllAgents()
        {
            foreach (var kvp in _activeAgents)
            {
                AgentEntity agent = kvp.Value;
                if (agent != null)
                {
                    agent.ResetForPool();
                    agent.transform.SetParent(_agentsRoot, false);
                    _agentPool.Push(agent);
                }
            }
            _activeAgents.Clear();
            _orderedAgentIds.Clear();
        }

        private AgentEntity SpawnOrRentAgent(int id, AgentShapeType shape, AgentMovementMode movMode, float scaleMeters)
        {
            if (_activeAgents.TryGetValue(id, out AgentEntity existing))
            {
                existing.Configure(id, shape, movMode, scaleMeters, _bus);
                existing.SetShowIdLabel(_showAgentIds);
                return existing;
            }

            if (_activeAgents.Count >= MaxSafeAgentsCount)
            {
                Debug.LogWarning($"[AgentManager] Reached MaxSafeAgentsCount ({MaxSafeAgentsCount}); skipping spawn of Agent {id}.");
                return null;
            }

            AgentEntity entity;
            if (_agentPool.Count > 0)
            {
                entity = _agentPool.Pop();
                entity.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject($"Agent_{id}");
                go.transform.SetParent(_agentsRoot, false);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _defaultAgentMaterial;
                go.AddComponent<AudioSource>();
                go.AddComponent<NativeDspSynthesizer>();
                go.AddComponent<SpatialAudioBankPlayer>();
                go.AddComponent<AgentVisualController>();
                entity = go.AddComponent<AgentEntity>();
            }

            entity.transform.SetParent(_agentsRoot, false);
            entity.Configure(id, shape, movMode, scaleMeters, _bus);
            entity.SetShowIdLabel(_showAgentIds);
            _activeAgents[id] = entity;
            _orderedAgentIds.Add(id);
            return entity;
        }

        private void HandleInstantiateAgents(AgentShapeType shape, AgentMovementMode movMode, int count, float scaleMeters, string ip, int port)
        {
            ClearAllAgents();
            int clampedCount = Mathf.Clamp(count, 0, MaxSafeAgentsCount);
            for (int i = 0; i < clampedCount; i++)
            {
                SpawnOrRentAgent(i, shape, movMode, scaleMeters);
            }
        }

        private void HandleAddAgents(AgentShapeType shape, AgentMovementMode movMode, float scaleMeters, string ip, int port, int[] ids)
        {
            if (ids == null) return;
            int limit = Mathf.Min(ids.Length, MaxSafeAgentsCount);
            for (int i = 0; i < limit; i++)
            {
                SpawnOrRentAgent(ids[i], shape, movMode, scaleMeters);
            }
        }

        private void HandleRemoveAgents(int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                ClearAllAgents();
                return;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                int id = ids[i];
                if (_activeAgents.TryGetValue(id, out AgentEntity entity))
                {
                    _activeAgents.Remove(id);
                    _orderedAgentIds.Remove(id);
                    if (entity != null)
                    {
                        entity.ResetForPool();
                        entity.transform.SetParent(_agentsRoot, false);
                        _agentPool.Push(entity);
                    }
                }
            }
        }

        private void HandleSetAgentPosition(int id, Vector3 posUnity)
        {
            if (_activeAgents.TryGetValue(id, out AgentEntity entity))
            {
                entity.SetTargetPosition(posUnity);
            }
        }

        private void HandleSetBatchAgentPositions(Vector3[] positionsUnity)
        {
            if (positionsUnity == null) return;
            int count = Mathf.Min(positionsUnity.Length, _orderedAgentIds.Count);
            for (int i = 0; i < count; i++)
            {
                int id = _orderedAgentIds[i];
                if (_activeAgents.TryGetValue(id, out AgentEntity entity))
                {
                    entity.SetTargetPosition(positionsUnity[i]);
                }
            }
        }

        private void HandleSetAgentSpeed(int id, float speedMetersPerSec)
        {
            if (_activeAgents.TryGetValue(id, out AgentEntity entity))
            {
                entity.SetSpeedMetersPerSec(speedMetersPerSec);
            }
        }

        private void HandleSetAgentDirection(int id, Vector3 dirUnity)
        {
            if (_activeAgents.TryGetValue(id, out AgentEntity entity))
            {
                entity.SetDirectionUnity(dirUnity);
            }
        }

        private void HandleSetAgentVelocity(int id, Vector3 velUnity)
        {
            if (_activeAgents.TryGetValue(id, out AgentEntity entity))
            {
                entity.SetVelocityUnity(velUnity);
            }
        }

        private void HandleSetAgentColor(int id, Color color)
        {
            if (_activeAgents.TryGetValue(id, out AgentEntity entity))
            {
                entity.Visuals.SetColor(color);
            }
        }

        private void HandleSetBatchAgentColors(Color[] colors)
        {
            if (colors == null) return;
            int count = Mathf.Min(colors.Length, _orderedAgentIds.Count);
            for (int i = 0; i < count; i++)
            {
                int id = _orderedAgentIds[i];
                if (_activeAgents.TryGetValue(id, out AgentEntity entity))
                {
                    entity.Visuals.SetColor(colors[i]);
                }
            }
        }

        private void HandleSetAgentRadiusVisual(int id, float radiusMeters, float alpha, Color color)
        {
            if (_activeAgents.TryGetValue(id, out AgentEntity entity))
            {
                entity.SetRadiusVisual(radiusMeters, alpha, color, _translucentRadiusMaterial);
            }
        }

        private void HandleSetAgentInteractable(int id, bool interactable)
        {
            if (_activeAgents.TryGetValue(id, out AgentEntity entity))
            {
                entity.SetInteractable(interactable);
            }
        }

        private void HandleSetShowAgentIds(bool show)
        {
            _showAgentIds = show;
            foreach (var kvp in _activeAgents)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.SetShowIdLabel(show);
                }
            }
        }

        private void HandleSetAgentMute(int id, bool mute)
        {
            if (id < 0)
            {
                foreach (var kvp in _activeAgents)
                {
                    kvp.Value.Synth.IsMuted = mute;
                }
            }
            else if (_activeAgents.TryGetValue(id, out AgentEntity entity))
            {
                entity.Synth.IsMuted = mute;
            }
        }

        private void HandleSetAgentSynthNote(int id, int noteAction, int midiNote, int velocity)
        {
            if (_activeAgents.TryGetValue(id, out AgentEntity entity))
            {
                entity.Synth.SetNote(noteAction, midiNote, velocity);
            }
        }

        private void HandleSetAgentSynthParam(int id, int ccId, float value)
        {
            if (_activeAgents.TryGetValue(id, out AgentEntity entity))
            {
                entity.Synth.SetParameter(ccId, value);
            }
        }

        private void HandleFlushAgentSynth(int id)
        {
            if (id < 0)
            {
                foreach (var kvp in _activeAgents)
                {
                    kvp.Value.Synth.Flush();
                }
            }
            else if (_activeAgents.TryGetValue(id, out AgentEntity entity))
            {
                entity.Synth.Flush();
            }
        }

        private void HandleSetAgentAudioBank(int id, int clipIndex, int playAction, float gain)
        {
            if (_activeAgents.TryGetValue(id, out AgentEntity entity))
            {
                entity.AudioBank.TriggerBankClip(clipIndex, playAction, gain);
            }
        }
    }
}
