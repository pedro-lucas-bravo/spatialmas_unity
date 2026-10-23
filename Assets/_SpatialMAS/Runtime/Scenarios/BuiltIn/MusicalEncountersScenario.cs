using SpatialMAS.Audio;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Scenarios.BuiltIn
{
    /// <summary>
    /// Scenario 4: Musical Encounters (SysMus25 Workshop Scenario).
    /// Clean-room C# port of sysmus25_workshop/HS_systems/musical_encounters.
    /// Autonomous spherical agents bounce inside a translucent spherical boundary; whenever two agents
    /// (or an interactive user-held cube / user hand) cross detection radii, an "encounter" triggers
    /// a quantized musical scale note based on radial distance from the boundary center.
    /// </summary>
    public class MusicalEncountersScenario : IScenarioModule
    {
        public int ScenarioId => 4;
        public string DisplayName => "4. Musical Encounters (SysMus25)";
        public string ShortDescription => "Elastic boundary reflection + pairwise proximity encounters triggering radial-quantized musical chords.";

        private ScenarioContext _ctx;
        private int _sphereCount = 14;
        private int _interactiveCubeCount = 2;
        private float _boundaryRadiusMeters = 1.2f;
        private float _speed = 0.65f;

        private Vector3[] _positions;
        private Vector3[] _velocities;
        private float[] _cooldowns;
        private float[] _noteOffTimers;

        private static readonly int[] s_CMajorScale = { 48, 50, 52, 53, 55, 57, 59, 60, 62, 64, 65, 67, 69, 71, 72, 74, 76, 79, 84 };

        public void OnScenarioStart(ScenarioContext context)
        {
            _ctx = context;
            SpawnEncountersArena();
        }

        private void SpawnEncountersArena()
        {
            if (_ctx.Bus == null) return;

            Vector3 center = _ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.OriginOffset : new Vector3(0f, 1.2f, 0.8f);
            float scale = _ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.WorldScale : 0.2f;
            _boundaryRadiusMeters = Mathf.Clamp(5.0f * scale, 0.5f, 3.5f);
            float agentSize = Mathf.Clamp(0.35f * scale, 0.04f, 0.15f);

            _ctx.Bus.AddBoundaryUnity(1, AgentShapeType.Sphere, center, _boundaryRadiusMeters);
            _ctx.Bus.InstantiateAgents(AgentShapeType.Sphere, AgentMovementMode.Teleport, _sphereCount, agentSize, "127.0.0.1", 6012);

            int total = _sphereCount + _interactiveCubeCount;
            if (_interactiveCubeCount > 0)
            {
                int[] cubeIds = new int[_interactiveCubeCount];
                for (int i = 0; i < _interactiveCubeCount; i++) cubeIds[i] = _sphereCount + i;
                _ctx.Bus.AddAgents(AgentShapeType.Cube, AgentMovementMode.Teleport, agentSize * 1.3f, "127.0.0.1", 6012, cubeIds);
                for (int i = 0; i < cubeIds.Length; i++)
                {
                    _ctx.Bus.SetAgentInteractable(cubeIds[i], true);
                    _ctx.Bus.SetAgentColor(cubeIds[i], new Color(1.0f, 0.82f, 0.15f, 1.0f));
                }
            }

            _positions = new Vector3[total];
            _velocities = new Vector3[total];
            _cooldowns = new float[total];
            _noteOffTimers = new float[total];

            var rng = new System.Random(404);
            for (int i = 0; i < total; i++)
            {
                Vector3 dir = new Vector3(
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0)).normalized;

                _positions[i] = center + dir * (_boundaryRadiusMeters * 0.45f * (float)rng.NextDouble());
                _velocities[i] = dir * (_speed * (0.7f + 0.6f * (float)rng.NextDouble()));
                _ctx.Bus.SetAgentPositionUnity(i, _positions[i]);

                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_OscillatorWaveform, (float)(i % 2 == 0 ? SynthWaveformType.Sine : SynthWaveformType.Triangle));
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_AttackPrimary, 0.015f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_DecayPrimary, 0.18f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_SustainPrimary, 0.20f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_ReleasePrimary, 0.28f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_ReverbPrimary, 0.28f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_GainPrimary, 0.22f);
            }
        }

        public void OnScenarioTick(float deltaTime)
        {
            if (_ctx.Bus == null || _positions == null) return;

            float dt = Mathf.Clamp(deltaTime, 0.001f, 0.05f);
            Vector3 center = _ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.OriginOffset : new Vector3(0f, 1.2f, 0.8f);
            int total = _positions.Length;
            float encounterRadius = _boundaryRadiusMeters * 0.20f;

            // Update positions & elastic spherical boundary reflection
            for (int i = 0; i < total; i++)
            {
                if (_cooldowns[i] > 0f) _cooldowns[i] -= dt;
                if (_noteOffTimers[i] > 0f)
                {
                    _noteOffTimers[i] -= dt;
                    if (_noteOffTimers[i] <= 0f)
                    {
                        _ctx.Bus.SetAgentSynthNote(i, 0, 60, 0);
                        Color idleCol = i >= _sphereCount
                            ? new Color(1.0f, 0.82f, 0.15f, 1.0f)
                            : new Color(0.25f, 0.65f, 1.0f, 1.0f);
                        _ctx.Bus.SetAgentColor(i, idleCol);
                    }
                }

                // Interactive cubes follow user hand if grabbed
                if (i >= _sphereCount && _ctx.AgentManager != null && _ctx.AgentManager.TryGetAgent(i, out var cubeEnt) && cubeEnt != null)
                {
                    _positions[i] = cubeEnt.transform.position;
                    continue;
                }

                _positions[i] += _velocities[i] * dt;
                Vector3 offset = _positions[i] - center;
                float dist = offset.magnitude;
                if (dist > _boundaryRadiusMeters * 0.92f && dist > 0.0001f)
                {
                    Vector3 normal = offset / dist;
                    _velocities[i] = Vector3.Reflect(_velocities[i], normal);
                    _positions[i] = center + normal * (_boundaryRadiusMeters * 0.91f);
                }

                _ctx.Bus.SetAgentPositionUnity(i, _positions[i]);
            }

            // Detect pairwise encounters
            for (int i = 0; i < total; i++)
            {
                for (int j = i + 1; j < total; j++)
                {
                    float d = Vector3.Distance(_positions[i], _positions[j]);
                    if (d <= encounterRadius)
                    {
                        TriggerEncounterIfReady(i, center);
                        TriggerEncounterIfReady(j, center);
                    }
                }
            }
        }

        private void TriggerEncounterIfReady(int id, Vector3 arenaCenter)
        {
            if (_cooldowns[id] > 0f) return;
            _cooldowns[id] = 0.32f;
            _noteOffTimers[id] = 0.22f;

            float normDist = Mathf.Clamp01(Vector3.Distance(_positions[id], arenaCenter) / Mathf.Max(0.1f, _boundaryRadiusMeters));
            int scaleIdx = Mathf.Clamp(Mathf.RoundToInt((1.0f - normDist) * (s_CMajorScale.Length - 1)), 0, s_CMajorScale.Length - 1);
            int midiNote = s_CMajorScale[scaleIdx];

            _ctx.Bus.SetAgentColor(id, new Color(1.0f, 0.98f, 0.75f, 1.0f)); // Flash warm gold/white on encounter
            _ctx.Bus.SetAgentSynthNote(id, 1, midiNote, 105);
        }

        public void OnScenarioStop()
        {
            _ctx.Bus?.SetAgentMute(-1, true);
        }

        public void SetParameter(string paramName, float value)
        {
            if (string.IsNullOrEmpty(paramName)) return;
            if (paramName.Equals("speed", System.StringComparison.OrdinalIgnoreCase))
            {
                _speed = Mathf.Clamp(value, 0.1f, 3.0f);
            }
        }

        public void ApplyPreset(int presetIndex)
        {
            _speed = presetIndex == 0 ? 0.55f : (presetIndex == 1 ? 1.15f : 0.35f);
        }
    }
}
