using SpatialMAS.Audio;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Scenarios.BuiltIn
{
    /// <summary>
    /// Scenario 3: Orbital Attractors & Repellers (Local Communication & Emergent Orbits).
    /// Clean-room C# port of attractors_repellers/AgentBehaviour.py & MusicalAgent.py.
    /// Spherical agents orbit around user-grabbable Cyan Attractor Cubes and are deflected by Red Repeller Cubes,
    /// broadcasting their orbital angles via simulated expanding Local Communication wavefronts.
    /// </summary>
    public class AttractorsRepellersScenario : IScenarioModule, ISpatialFrameAwareScenario
    {
        public int ScenarioId => 3;
        public string DisplayName => "3. Orbital Attractors & Repellers";
        public string ShortDescription => "Autonomous spheres orbit grabbable Cyan Attractors, deflect from Red Repellers, and sync via LocalComm wavefronts.";

        private ScenarioContext _ctx;
        private int _orbiterCount = 12;
        private int _attractorCount = 2;
        private int _repellerCount = 2;
        private float _angularSpeed = 1.6f;

        private float[] _theta;
        private float[] _phi;
        private float[] _omegaTheta;
        private float[] _omegaPhi;
        private float[] _orbitRadius;
        private float[] _commTimers;
        private Vector3[] _attractorPositions;
        private Vector3[] _repellerPositions;
        private bool[,] _repellerContacts;
        private Quaternion _orbitRotation = Quaternion.identity;
        private float _repellerRadiusMeters;
        private float _localCommRangeMeters;

        public void OnScenarioStart(ScenarioContext context)
        {
            _ctx = context;
            SpawnScenario();
            if (_ctx.Bus != null)
            {
                _ctx.Bus.OnLocalCommDeliveredUnity += HandleLocalCommDelivered;
            }
        }

        private void SpawnScenario()
        {
            if (_ctx.Bus == null) return;

            Vector3 origin = _ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.OriginOffset : new Vector3(0f, 1.2f, 0.8f);
            float scale = _ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.WorldScale : 0.2f;
            float sphereSize = Mathf.Clamp(0.28f * scale, 0.04f, 0.14f);
            float cubeSize = sphereSize * 1.45f;
            _orbitRotation = _ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.OriginRotation : Quaternion.identity;
            _repellerRadiusMeters = 1.25f * scale;
            _localCommRangeMeters = 1.8f * scale;

            _ctx.Bus.InstantiateAgents(AgentShapeType.Sphere, AgentMovementMode.Teleport, _orbiterCount, sphereSize, "127.0.0.1", 6012);

            int totalCubes = _attractorCount + _repellerCount;
            int[] cubeIds = new int[totalCubes];
            for (int i = 0; i < totalCubes; i++) cubeIds[i] = _orbiterCount + i;
            _ctx.Bus.AddAgents(AgentShapeType.Cube, AgentMovementMode.Teleport, cubeSize, "127.0.0.1", 6012, cubeIds);

            _attractorPositions = new Vector3[_attractorCount];
            for (int i = 0; i < _attractorCount; i++)
            {
                int id = _orbiterCount + i;
                float xOff = (i == 0 ? -1.8f : 1.8f) * scale;
                _attractorPositions[i] = origin + _orbitRotation * new Vector3(xOff, 0f, 0f);
                _ctx.Bus.SetAgentPositionUnity(id, _attractorPositions[i]);
                _ctx.Bus.SetAgentColor(id, new Color(0.0f, 0.95f, 1.0f, 1.0f)); // Cyan Attractor
                _ctx.Bus.SetAgentInteractable(id, true);
            }

            _repellerPositions = new Vector3[_repellerCount];
            for (int i = 0; i < _repellerCount; i++)
            {
                int id = _orbiterCount + _attractorCount + i;
                float zOff = (i == 0 ? -1.6f : 1.6f) * scale;
                _repellerPositions[i] = origin + _orbitRotation * new Vector3(0f, 0.15f * scale, zOff);
                _ctx.Bus.SetAgentPositionUnity(id, _repellerPositions[i]);
                _ctx.Bus.SetAgentColor(id, new Color(1.0f, 0.18f, 0.22f, 1.0f)); // Red Repeller
                _ctx.Bus.SetAgentInteractable(id, true);
            }

            _theta = new float[_orbiterCount];
            _phi = new float[_orbiterCount];
            _omegaTheta = new float[_orbiterCount];
            _omegaPhi = new float[_orbiterCount];
            _orbitRadius = new float[_orbiterCount];
            _commTimers = new float[_orbiterCount];
            _repellerContacts = new bool[_orbiterCount, _repellerCount];

            var rng = new System.Random(303);
            for (int i = 0; i < _orbiterCount; i++)
            {
                _theta[i] = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                _phi[i] = (float)(rng.NextDouble() * Mathf.PI);
                _omegaTheta[i] = ((i % 2 == 0) ? 1f : -1f) * _angularSpeed * (0.8f + 0.4f * (float)rng.NextDouble());
                _omegaPhi[i] = _angularSpeed * (0.6f + 0.5f * (float)rng.NextDouble());
                _orbitRadius[i] = (1.1f + 0.7f * (float)rng.NextDouble()) * scale;
                _commTimers[i] = 0.5f + (float)rng.NextDouble() * 1.8f;

                _ctx.Bus.SetAgentColor(i, new Color(0.95f, 0.85f, 0.25f, 1.0f));
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_OscillatorWaveform, (float)SynthWaveformType.Sawtooth);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_FilterCutoffHz, 2200f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_GainPrimary, 0.10f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_FrequencyHz, 220f + i * 24f);
            }
        }

        public void OnScenarioTick(float deltaTime)
        {
            if (_ctx.Bus == null || _theta == null) return;

            float dt = Mathf.Clamp(deltaTime, 0.001f, 0.05f);

            // Read live positions of user-grabbable Attractor & Repeller cubes
            for (int i = 0; i < _attractorCount; i++)
            {
                int id = _orbiterCount + i;
                if (_ctx.AgentManager != null && _ctx.AgentManager.TryGetAgent(id, out var ent) && ent != null)
                {
                    _attractorPositions[i] = ent.transform.position;
                }
            }
            for (int i = 0; i < _repellerCount; i++)
            {
                int id = _orbiterCount + _attractorCount + i;
                if (_ctx.AgentManager != null && _ctx.AgentManager.TryGetAgent(id, out var ent) && ent != null)
                {
                    _repellerPositions[i] = ent.transform.position;
                }
            }

            for (int i = 0; i < _orbiterCount; i++)
            {
                // Choose nearest attractor
                Vector3 currentPos = Vector3.zero;
                if (_ctx.AgentManager != null && _ctx.AgentManager.TryGetAgent(i, out var orbEnt) && orbEnt != null)
                {
                    currentPos = orbEnt.transform.position;
                }

                Vector3 targetAttractor = _attractorPositions[i % _attractorCount];
                float bestDist = float.MaxValue;
                for (int a = 0; a < _attractorCount; a++)
                {
                    float d = Vector3.Distance(currentPos, _attractorPositions[a]);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        targetAttractor = _attractorPositions[a];
                    }
                }

                // Reverse on entry, then wait until the orbiter has left a slightly
                // larger radius before allowing another reversal.
                bool enteredRepeller = false;
                for (int r = 0; r < _repellerCount; r++)
                {
                    float dRep = Vector3.Distance(currentPos, _repellerPositions[r]);
                    if (!_repellerContacts[i, r] && dRep < _repellerRadiusMeters)
                    {
                        _repellerContacts[i, r] = true;
                        enteredRepeller = true;
                    }
                    else if (_repellerContacts[i, r] && dRep > _repellerRadiusMeters * 1.1f)
                        _repellerContacts[i, r] = false;
                }
                if (enteredRepeller)
                {
                    _omegaTheta[i] = -_omegaTheta[i];
                    _omegaPhi[i] = -_omegaPhi[i];
                }

                _theta[i] = Mathf.Repeat(_theta[i] + _omegaTheta[i] * dt, Mathf.PI * 2.0f);
                _phi[i] = Mathf.Repeat(_phi[i] + _omegaPhi[i] * dt, Mathf.PI * 2.0f);

                float rad = _orbitRadius[i];
                Vector3 sphericalOffset = new Vector3(
                    rad * Mathf.Sin(_phi[i]) * Mathf.Cos(_theta[i]),
                    rad * Mathf.Cos(_phi[i]),
                    rad * Mathf.Sin(_phi[i]) * Mathf.Sin(_theta[i]));

                Vector3 nextPos = targetAttractor + _orbitRotation * sphericalOffset;
                _ctx.Bus.SetAgentPositionUnity(i, nextPos);

                // Continuous pitch & filter modulation from orbital angles
                float normAngle = _theta[i] / (Mathf.PI * 2.0f);
                float freq = Mathf.Lerp(196f, 587.33f, normAngle);
                float cutoff = Mathf.Lerp(800f, 5200f, (Mathf.Sin(_phi[i]) + 1f) * 0.5f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_FrequencyHz, freq);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_FilterCutoffHz, cutoff);

                // Periodically broadcast local communication wavefront
                _commTimers[i] -= dt;
                if (_commTimers[i] <= 0f)
                {
                    _commTimers[i] = 2.2f + (i % 3) * 0.6f;
                    _ctx.Bus.TriggerLocalComm("orbit_sync", i, _localCommRangeMeters, 0.35f, new object[] { _theta[i], _phi[i] });
                }
            }
        }

        private void HandleLocalCommDelivered(string msgType, int senderId, int receiverId, float distMeters, object[] extraArgs)
        {
            if (msgType != "orbit_sync" || extraArgs == null || extraArgs.Length < 2) return;
            if (receiverId < 0 || receiverId >= _orbiterCount) return;

            if (extraArgs[0] is float senderTheta)
            {
                // Nudge receiver's orbital phase toward sender's phase
                _theta[receiverId] = Mathf.LerpAngle(_theta[receiverId] * Mathf.Rad2Deg, senderTheta * Mathf.Rad2Deg, 0.25f) * Mathf.Deg2Rad;
            }
        }

        public void OnScenarioStop()
        {
            if (_ctx.Bus != null)
            {
                _ctx.Bus.OnLocalCommDeliveredUnity -= HandleLocalCommDelivered;
                _ctx.Bus.SetAgentMute(-1, true);
            }
        }

        public void SetParameter(string paramName, float value)
        {
            if (string.IsNullOrEmpty(paramName)) return;
            if (paramName.Equals("speed", System.StringComparison.OrdinalIgnoreCase))
            {
                SetAngularSpeed(Mathf.Clamp(value, 0.2f, 5.0f));
            }
        }

        public void ApplyPreset(int presetIndex)
        {
            SetAngularSpeed(presetIndex == 0 ? 1.2f : (presetIndex == 1 ? 2.4f : 0.7f));
        }

        private void SetAngularSpeed(float speed)
        {
            float ratio = speed / _angularSpeed;
            _angularSpeed = speed;
            if (_omegaTheta == null) return;
            for (int i = 0; i < _omegaTheta.Length; i++)
            {
                _omegaTheta[i] *= ratio;
                _omegaPhi[i] *= ratio;
            }
        }

        public void ApplySpatialFrameChange(SpatialFrameChange change)
        {
            if (_theta == null) return;
            for (int i = 0; i < _orbitRadius.Length; i++) _orbitRadius[i] *= change.ScaleRatio;
            for (int i = 0; i < _attractorPositions.Length; i++)
                _attractorPositions[i] = change.TransformPosition(_attractorPositions[i]);
            for (int i = 0; i < _repellerPositions.Length; i++)
                _repellerPositions[i] = change.TransformPosition(_repellerPositions[i]);
            _orbitRotation = change.TransformRotation(_orbitRotation);
            _repellerRadiusMeters *= change.ScaleRatio;
            _localCommRangeMeters *= change.ScaleRatio;
        }
    }
}
