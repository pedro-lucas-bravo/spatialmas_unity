using System.Collections.Generic;
using SpatialMAS.Audio;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Agents
{
    /// <summary>
    /// High-performance pooled Agent Manager.
    /// Handles instantiation, addition, removal, batch transform updates, color updates,
    /// and synth/audio routing for up to hundreds of simultaneous spatial agents.
    /// </summary>
    public class AgentManager : MonoBehaviour
    {
        [SerializeField] private Material _defaultAgentMaterial;

        private readonly Dictionary<int, AgentEntity> _activeAgents = new Dictionary<int, AgentEntity>(256);
        private readonly List<int> _orderedAgentIds = new List<int>(256);
        private readonly Stack<AgentEntity> _agentPool = new Stack<AgentEntity>(256);

        private SimulationCommandBus _bus;
        private Transform _agentsRoot;

        public IReadOnlyDictionary<int, AgentEntity> ActiveAgents => _activeAgents;
        public IReadOnlyList<int> OrderedAgentIds => _orderedAgentIds;
        public int ActiveCount => _activeAgents.Count;

        public void Initialize(SimulationCommandBus bus, Material defaultMaterial = null)
        {
            _bus = bus;
            if (defaultMaterial != null) _defaultAgentMaterial = defaultMaterial;

            if (_agentsRoot == null)
            {
                var rootGo = new GameObject("AgentsContainer");
                rootGo.transform.SetParent(transform, false);
                _agentsRoot = rootGo.transform;
            }

            EnsureDefaultMaterial();

            if (_bus != null)
            {
                _bus.OnInstantiateAgents += HandleInstantiateAgents;
                _bus.OnAddAgents += HandleAddAgents;
                _bus.OnRemoveAgents += HandleRemoveAgents;
                _bus.OnSetAgentPositionUnity += HandleSetAgentPosition;
                _bus.OnSetBatchAgentPositionsUnity += HandleSetBatchAgentPositions;
                _bus.OnSetAgentColor += HandleSetAgentColor;
                _bus.OnSetBatchAgentColors += HandleSetBatchAgentColors;
                _bus.OnSetAgentInteractable += HandleSetAgentInteractable;
                _bus.OnSetAgentMute += HandleSetAgentMute;
                _bus.OnSetAgentSynthNote += HandleSetAgentSynthNote;
                _bus.OnSetAgentSynthParam += HandleSetAgentSynthParam;
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
                _bus.OnSetAgentColor -= HandleSetAgentColor;
                _bus.OnSetBatchAgentColors -= HandleSetBatchAgentColors;
                _bus.OnSetAgentInteractable -= HandleSetAgentInteractable;
                _bus.OnSetAgentMute -= HandleSetAgentMute;
                _bus.OnSetAgentSynthNote -= HandleSetAgentSynthNote;
                _bus.OnSetAgentSynthParam -= HandleSetAgentSynthParam;
                _bus.OnSetAgentAudioBank -= HandleSetAgentAudioBank;
            }
        }

        private void EnsureDefaultMaterial()
        {
            if (_defaultAgentMaterial != null) return;
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) urpLit = Shader.Find("Standard");
            _defaultAgentMaterial = new Material(urpLit)
            {
                name = "SpatialMAS_AgentLit_Instanced",
                enableInstancing = true
            };
            _defaultAgentMaterial.SetFloat("_Smoothness", 0.8f);
            _defaultAgentMaterial.EnableKeyword("_EMISSION");
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
                return existing;
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
            _activeAgents[id] = entity;
            _orderedAgentIds.Add(id);
            return entity;
        }

        private void HandleInstantiateAgents(AgentShapeType shape, AgentMovementMode movMode, int count, float scaleMeters, string ip, int port)
        {
            ClearAllAgents();
            for (int i = 0; i < count; i++)
            {
                SpawnOrRentAgent(i, shape, movMode, scaleMeters);
            }
        }

        private void HandleAddAgents(AgentShapeType shape, AgentMovementMode movMode, float scaleMeters, string ip, int port, int[] ids)
        {
            if (ids == null) return;
            for (int i = 0; i < ids.Length; i++)
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

        private void HandleSetAgentInteractable(int id, bool interactable)
        {
            if (_activeAgents.TryGetValue(id, out AgentEntity entity))
            {
                entity.SetInteractable(interactable);
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

        private void HandleSetAgentAudioBank(int id, int clipIndex, int playAction, float gain)
        {
            if (_activeAgents.TryGetValue(id, out AgentEntity entity))
            {
                entity.AudioBank.TriggerBankClip(clipIndex, playAction, gain);
            }
        }
    }
}
