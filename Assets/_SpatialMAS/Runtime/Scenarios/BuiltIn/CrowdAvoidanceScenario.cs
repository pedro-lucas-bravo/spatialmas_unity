using SpatialMAS.Audio;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Scenarios.BuiltIn
{
    /// <summary>
    /// Scenario 1: Crowd Navigation & Human Avoidance (Grant Proposal Flagship Scenario).
    /// Autonomous agents flock smoothly within an arena while physically dispersing around the XR participant's
    /// head and hands using Helbing's Social Force Model, emitting proximity-mapped spatial audio cues.
    /// </summary>
    public class CrowdAvoidanceScenario : IScenarioModule
    {
        public int ScenarioId => 1;
        public string DisplayName => "1. Crowd Navigation & Human Avoidance";
        public string ShortDescription => "Boids flocking + Helbing Social Force avoidance around the physical XR user with spatial proximity sonification.";

        private ScenarioContext _ctx;
        private int _agentCount = 32;
        private float _arenaRadiusMeters = 1.4f;
        private float _cruiseSpeed = 0.65f;
        private float _personalSpaceRadius = 0.65f;
        private float _humanRepulsionStrength = 2.2f;

        private Vector3[] _positions;
        private Vector3[] _velocities;
        private float[] _pulseTimers;

        private static readonly int[] s_DMinor9ChordNotes = { 50, 57, 60, 62, 64, 65, 69, 72, 74, 76 };

        public void OnScenarioStart(ScenarioContext context)
        {
            _ctx = context;
            RebuildCrowd();
        }

        private void RebuildCrowd()
        {
            if (_ctx.Bus == null) return;

            Vector3 center = _ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.OriginOffset : new Vector3(0f, 1.2f, 0.8f);
            float scale = _ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.WorldScale : 0.2f;
            _arenaRadiusMeters = Mathf.Clamp(6.5f * scale, 0.6f, 4.0f);
            float agentScale = Mathf.Clamp(0.32f * scale, 0.04f, 0.18f);

            _ctx.Bus.AddBoundaryUnity(1, AgentShapeType.Sphere, center, _arenaRadiusMeters);
            _ctx.Bus.InstantiateAgents(AgentShapeType.Pyramid, AgentMovementMode.Teleport, _agentCount, agentScale, "127.0.0.1", 6012);

            _positions = new Vector3[_agentCount];
            _velocities = new Vector3[_agentCount];
            _pulseTimers = new float[_agentCount];

            var rng = new System.Random(42);
            for (int i = 0; i < _agentCount; i++)
            {
                Vector3 offset = RandomInsideSphere(rng) * (_arenaRadiusMeters * 0.75f);
                offset.y *= 0.35f; // Flattened crowd corridor layer
                _positions[i] = center + offset;
                _velocities[i] = RandomInsideSphere(rng).normalized * _cruiseSpeed;
                _pulseTimers[i] = (float)rng.NextDouble() * 1.5f;

                _ctx.Bus.SetAgentPositionUnity(i, _positions[i]);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_OscillatorWaveform, (float)SynthWaveformType.Triangle);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_AttackPrimary, 0.02f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_DecayPrimary, 0.12f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_SustainPrimary, 0.25f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_ReleasePrimary, 0.18f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_GainPrimary, 0.12f);
            }
        }

        public void OnScenarioTick(float deltaTime)
        {
            if (_ctx.Bus == null || _positions == null || _positions.Length != _agentCount) return;

            float dt = Mathf.Clamp(deltaTime, 0.001f, 0.05f);
            Vector3 center = _ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.OriginOffset : new Vector3(0f, 1.2f, 0.8f);
            Vector3 userHead = _ctx.Embodiment != null ? _ctx.Embodiment.HeadPositionWorld : center;

            StepBoidsAndHumanAvoidance(
                _positions,
                _velocities,
                center,
                _arenaRadiusMeters,
                userHead,
                _personalSpaceRadius,
                _humanRepulsionStrength,
                _cruiseSpeed,
                dt);

            for (int i = 0; i < _agentCount; i++)
            {
                _ctx.Bus.SetAgentPositionUnity(i, _positions[i]);

                // Orient pyramid agent along velocity
                if (_ctx.AgentManager != null && _ctx.AgentManager.TryGetAgent(i, out var entity) && entity != null)
                {
                    if (_velocities[i].sqrMagnitude > 0.001f)
                    {
                        entity.transform.rotation = Quaternion.LookRotation(_velocities[i].normalized, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
                    }
                }

                // Proximity to human drives color (Teal -> Coral) and pulse rate
                float distToHuman = Vector3.Distance(_positions[i], userHead);
                float proximity01 = 1.0f - Mathf.Clamp01(distToHuman / Mathf.Max(0.2f, _personalSpaceRadius * 1.6f));

                Color calmTeal = new Color(0.10f, 0.82f, 0.88f, 1.0f);
                Color alertCoral = new Color(1.0f, 0.32f, 0.18f, 1.0f);
                _ctx.Bus.SetAgentColor(i, Color.Lerp(calmTeal, alertCoral, proximity01));

                _pulseTimers[i] -= dt * (1.0f + proximity01 * 4.0f);
                if (_pulseTimers[i] <= 0f)
                {
                    _pulseTimers[i] = Random.Range(0.6f, 1.4f);
                    int noteIdx = Mathf.Clamp(Mathf.RoundToInt(proximity01 * (s_DMinor9ChordNotes.Length - 1)) + (i % 3), 0, s_DMinor9ChordNotes.Length - 1);
                    int midiNote = s_DMinor9ChordNotes[noteIdx];
                    float cutoff = Mathf.Lerp(900f, 6500f, proximity01);
                    _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_FilterCutoffHz, cutoff);
                    _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_GainPrimary, Mathf.Lerp(0.06f, 0.25f, proximity01));
                    _ctx.Bus.SetAgentSynthNote(i, 1, midiNote, Mathf.RoundToInt(Mathf.Lerp(55f, 115f, proximity01)));
                }
            }
        }

        /// <summary>
        /// Pure mathematical step function (callable from EditMode unit tests).
        /// </summary>
        public static void StepBoidsAndHumanAvoidance(
            Vector3[] positions,
            Vector3[] velocities,
            Vector3 arenaCenter,
            float arenaRadius,
            Vector3 humanPosition,
            float personalSpaceRadius,
            float humanRepulsionStrength,
            float targetSpeed,
            float dt)
        {
            int n = positions.Length;
            float neighborRadius = arenaRadius * 0.35f;
            float sepRadius = arenaRadius * 0.14f;

            for (int i = 0; i < n; i++)
            {
                Vector3 pos = positions[i];
                Vector3 vel = velocities[i];

                Vector3 sep = Vector3.zero;
                Vector3 align = Vector3.zero;
                Vector3 coh = Vector3.zero;
                int count = 0;

                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    Vector3 diff = positions[j] - pos;
                    float d = diff.magnitude;
                    if (d < neighborRadius && d > 0.0001f)
                    {
                        coh += positions[j];
                        align += velocities[j];
                        count++;
                        if (d < sepRadius)
                        {
                            sep -= (diff / d) * ((sepRadius - d) / sepRadius);
                        }
                    }
                }

                Vector3 accel = sep * 2.2f;
                if (count > 0)
                {
                    coh = ((coh / count) - pos).normalized * targetSpeed;
                    align = (align / count).normalized * targetSpeed;
                    accel += (coh - vel) * 0.8f + (align - vel) * 1.1f;
                }

                // Boundary containment force
                Vector3 toCenter = arenaCenter - pos;
                float distFromCenter = toCenter.magnitude;
                if (distFromCenter > arenaRadius * 0.85f)
                {
                    accel += (toCenter / distFromCenter) * ((distFromCenter - arenaRadius * 0.85f) * 6.0f);
                }

                // Helbing Social Force Human Repulsion
                Vector3 fromHuman = pos - humanPosition;
                float distHuman = fromHuman.magnitude;
                if (distHuman < personalSpaceRadius * 1.5f && distHuman > 0.0001f)
                {
                    float falloff = Mathf.Exp((personalSpaceRadius - distHuman) / Mathf.Max(0.08f, personalSpaceRadius * 0.35f));
                    accel += (fromHuman / distHuman) * (humanRepulsionStrength * falloff);
                }

                vel += accel * dt;
                float speed = vel.magnitude;
                if (speed > targetSpeed * 2.2f) vel = (vel / speed) * (targetSpeed * 2.2f);
                else if (speed < targetSpeed * 0.4f && speed > 0.0001f) vel = (vel / speed) * targetSpeed;

                velocities[i] = vel;
                positions[i] = pos + vel * dt;
            }
        }

        private static Vector3 RandomInsideSphere(System.Random rng)
        {
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            float y = (float)(rng.NextDouble() * 2.0 - 1.0);
            float z = (float)(rng.NextDouble() * 2.0 - 1.0);
            Vector3 v = new Vector3(x, y, z);
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }

        public void OnScenarioStop()
        {
            if (_ctx.Bus != null)
            {
                _ctx.Bus.SetAgentMute(-1, true);
            }
        }

        public void SetParameter(string paramName, float value)
        {
            if (string.IsNullOrEmpty(paramName)) return;
            switch (paramName.ToLowerInvariant())
            {
                case "speed": _cruiseSpeed = Mathf.Clamp(value, 0.1f, 3.0f); break;
                case "personalspace": _personalSpaceRadius = Mathf.Clamp(value, 0.2f, 2.5f); break;
                case "repulsion": _humanRepulsionStrength = Mathf.Clamp(value, 0f, 10.0f); break;
                case "agentcount":
                    int newCount = Mathf.Clamp(Mathf.RoundToInt(value), 4, 120);
                    if (newCount != _agentCount)
                    {
                        _agentCount = newCount;
                        RebuildCrowd();
                    }
                    break;
            }
        }

        public void ApplyPreset(int presetIndex)
        {
            switch (presetIndex)
            {
                case 0: _personalSpaceRadius = 0.55f; _humanRepulsionStrength = 1.8f; _cruiseSpeed = 0.55f; break;
                case 1: _personalSpaceRadius = 0.95f; _humanRepulsionStrength = 3.5f; _cruiseSpeed = 0.85f; break;
                case 2: _personalSpaceRadius = 0.35f; _humanRepulsionStrength = 0.8f; _cruiseSpeed = 0.40f; break;
            }
        }
    }
}
