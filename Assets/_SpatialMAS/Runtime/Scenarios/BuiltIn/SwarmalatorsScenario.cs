using SpatialMAS.Audio;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Scenarios.BuiltIn
{
    /// <summary>
    /// Scenario 2: 3D Swarmalators (Oscillators that Sync and Swarm).
    /// Clean-room native C# port of Swarmalator.py and MusicalSwarmalator.py with 5 macro-state presets:
    /// 0: Static Sync (J=0.1, K=1.0)
    /// 1: Static Async (J=0.1, K=-1.0)
    /// 2: Static Phase Wave (J=1.0, K=0.0)
    /// 3: Splintered Phase Wave (J=1.0, K=-0.1)
    /// 4: Active Phase Wave (J=1.0, K=-0.75)
    /// </summary>
    public class SwarmalatorsScenario : IScenarioModule
    {
        public int ScenarioId => 2;
        public string DisplayName => "2. 3D Swarmalators (Sync & Swarm)";
        public string ShortDescription => "Coupled 3D spatial swarming (J) and Kuramoto phase synchronization (K) with pentatonic phase sonification.";

        private ScenarioContext _ctx;
        private int _count = 24;
        private int _interactiveCubes = 3;
        private float _paramJ = 1.0f;
        private float _paramK = -0.75f; // Default: Active Phase Wave
        private float _speedMultiplier = 1.2f;

        private Vector3[] _localPositions; // Normalized simulation coordinates (~ -1.5 to +1.5)
        private float[] _phases;           // In radians [0, 2*PI)
        private float[] _naturalFreqs;

        private static readonly int[] s_PentatonicNotes = { 60, 62, 64, 67, 69, 72, 74, 76, 79, 81 };

        public float ParamJ => _paramJ;
        public float ParamK => _paramK;

        public void OnScenarioStart(ScenarioContext context)
        {
            _ctx = context;
            SpawnSwarmalators();
        }

        private void SpawnSwarmalators()
        {
            if (_ctx.Bus == null) return;

            float worldScale = _ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.WorldScale : 0.2f;
            float sphereRadius = Mathf.Clamp(0.28f * worldScale, 0.035f, 0.14f);

            int sphereCount = Mathf.Max(2, _count - _interactiveCubes);
            _ctx.Bus.InstantiateAgents(AgentShapeType.Sphere, AgentMovementMode.Teleport, sphereCount, sphereRadius, "127.0.0.1", 6012);

            if (_interactiveCubes > 0)
            {
                int[] cubeIds = new int[_interactiveCubes];
                for (int i = 0; i < _interactiveCubes; i++) cubeIds[i] = sphereCount + i;
                _ctx.Bus.AddAgents(AgentShapeType.Cube, AgentMovementMode.Teleport, sphereRadius * 1.25f, "127.0.0.1", 6012, cubeIds);
                for (int i = 0; i < cubeIds.Length; i++)
                {
                    _ctx.Bus.SetAgentInteractable(cubeIds[i], true);
                }
            }

            _localPositions = new Vector3[_count];
            _phases = new float[_count];
            _naturalFreqs = new float[_count];

            var rng = new System.Random(101);
            for (int i = 0; i < _count; i++)
            {
                _localPositions[i] = new Vector3(
                    (float)(rng.NextDouble() * 1.6 - 0.8),
                    (float)(rng.NextDouble() * 1.6 - 0.8),
                    (float)(rng.NextDouble() * 1.6 - 0.8));
                _phases[i] = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                _naturalFreqs[i] = 1.2f + (float)(rng.NextDouble() * 0.25 - 0.125);

                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_OscillatorWaveform, (float)SynthWaveformType.Sine);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_AttackPrimary, 0.02f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_DecayPrimary, 0.15f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_SustainPrimary, 0.0f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_ReleasePrimary, 0.18f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_GainPrimary, 0.16f);
            }
        }

        public void OnScenarioTick(float deltaTime)
        {
            if (_ctx.Bus == null || _localPositions == null || _localPositions.Length != _count) return;

            float dt = Mathf.Clamp(deltaTime, 0.001f, 0.04f) * _speedMultiplier;
            Vector3 origin = _ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.OriginOffset : new Vector3(0f, 1.2f, 0.8f);
            float arenaScale = (_ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.WorldScale : 0.2f) * 2.5f;
            Quaternion arenaRotation = _ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.OriginRotation : Quaternion.identity;

            // Sync user-grabbed cubes from world space back into local Swarmalator coordinates
            for (int i = 0; i < _count; i++)
            {
                if (_ctx.AgentManager != null && _ctx.AgentManager.TryGetAgent(i, out var agent) && agent != null && agent.IsGrabbedByUser)
                {
                    _localPositions[i] = Quaternion.Inverse(arenaRotation) * (agent.transform.position - origin) / Mathf.Max(0.0001f, arenaScale);
                }
            }

            float[] prevPhases = new float[_count];
            System.Array.Copy(_phases, prevPhases, _count);

            StepSwarmalators(_localPositions, _phases, _naturalFreqs, _paramJ, _paramK, dt);

            for (int i = 0; i < _count; i++)
            {
                bool isGrabbed = _ctx.AgentManager != null &&
                                 _ctx.AgentManager.TryGetAgent(i, out var ent) &&
                                 ent != null && ent.IsGrabbedByUser;

                if (!isGrabbed)
                {
                    Vector3 worldPos = origin + arenaRotation * (_localPositions[i] * arenaScale);
                    _ctx.Bus.SetAgentPositionUnity(i, worldPos);
                }

                float normPhase = Mathf.Repeat(_phases[i] / (2.0f * Mathf.PI), 1.0f);
                Color phaseColor = Color.HSVToRGB(normPhase, 0.88f, 1.0f);
                _ctx.Bus.SetAgentColor(i, phaseColor);

                // Trigger pentatonic note when oscillator phase wraps past 2*PI -> 0
                if (_phases[i] < prevPhases[i])
                {
                    float radialDist = Mathf.Clamp01(_localPositions[i].magnitude / 1.5f);
                    int noteIdx = Mathf.Clamp(Mathf.RoundToInt((1.0f - radialDist) * (s_PentatonicNotes.Length - 1)), 0, s_PentatonicNotes.Length - 1);
                    _ctx.Bus.SetAgentSynthNote(i, 1, s_PentatonicNotes[noteIdx], 90);
                }
            }
        }

        /// <summary>
        /// Pure mathematical ODE step for 3D Swarmalators (callable from EditMode unit tests).
        /// </summary>
        public static void StepSwarmalators(
            Vector3[] positions,
            float[] phases,
            float[] naturalFreqs,
            float J,
            float K,
            float dt)
        {
            int n = positions.Length;
            if (n <= 1) return;

            Vector3[] dPos = new Vector3[n];
            float[] dTheta = new float[n];
            float invN = 1.0f / (n - 1);

            for (int i = 0; i < n; i++)
            {
                Vector3 posSum = Vector3.zero;
                float thetaSum = 0f;

                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    Vector3 diff = positions[j] - positions[i];
                    float distSqr = diff.sqrMagnitude + 0.015f; // Softening to avoid singularity
                    float dist = Mathf.Sqrt(distSqr);
                    float phaseDiff = phases[j] - phases[i];

                    // Spatial attraction (1 + J*cos(theta_j - theta_i)) - repulsion (1 / dist^2)
                    Vector3 unit = diff / dist;
                    float spatialFactor = (1.0f + J * Mathf.Cos(phaseDiff)) - (1.0f / distSqr);
                    posSum += unit * spatialFactor;

                    // Phase coupling sin(theta_j - theta_i) / dist
                    thetaSum += Mathf.Sin(phaseDiff) / dist;
                }

                dPos[i] = posSum * invN;
                float w0 = (naturalFreqs != null && i < naturalFreqs.Length) ? naturalFreqs[i] : 1.0f;
                dTheta[i] = w0 + (K * invN * thetaSum);
            }

            for (int i = 0; i < n; i++)
            {
                positions[i] += dPos[i] * dt;
                phases[i] = Mathf.Repeat(phases[i] + dTheta[i] * dt, Mathf.PI * 2.0f);
            }
        }

        public void OnScenarioStop()
        {
            _ctx.Bus?.SetAgentMute(-1, true);
        }

        public void SetParameter(string paramName, float value)
        {
            if (string.IsNullOrEmpty(paramName)) return;
            switch (paramName.ToLowerInvariant())
            {
                case "j": _paramJ = Mathf.Clamp(value, -1.5f, 1.5f); break;
                case "k": _paramK = Mathf.Clamp(value, -1.5f, 1.5f); break;
                case "speed": _speedMultiplier = Mathf.Clamp(value, 0.1f, 4.0f); break;
                case "preset": ApplyPreset(Mathf.RoundToInt(value)); break;
            }
        }

        public void ApplyPreset(int presetIndex)
        {
            switch (presetIndex)
            {
                case 0: _paramJ = 0.1f; _paramK = 1.0f; break;   // Static Sync
                case 1: _paramJ = 0.1f; _paramK = -1.0f; break;  // Static Async
                case 2: _paramJ = 1.0f; _paramK = 0.0f; break;   // Static Phase Wave
                case 3: _paramJ = 1.0f; _paramK = -0.1f; break;  // Splintered Phase Wave
                case 4:
                default: _paramJ = 1.0f; _paramK = -0.75f; break; // Active Phase Wave
            }
        }
    }
}
