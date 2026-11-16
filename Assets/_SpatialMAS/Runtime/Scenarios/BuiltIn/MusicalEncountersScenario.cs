using System;
using SpatialMAS.Audio;
using SpatialMAS.Core;
using SpatialMAS.Environment;
using UnityEngine;

namespace SpatialMAS.Scenarios.BuiltIn
{
    /// <summary>HS-IMS Musical Encounters with a translation handle for the XR arena.</summary>
    public class MusicalEncountersScenario : IScenarioModule
    {
        public int ScenarioId => 4;
        public string DisplayName => "4. Musical Encounters (SysMus25)";
        public string ShortDescription => "Moving agents sustain radial pitches while neighbors are inside their detection spheres.";

        internal const int BoundaryId = 1;
        private const int DefaultSphereCount = 5;
        private const int MaxSphereCount = 120;
        private const int AgentCapacity = MaxSphereCount + 2;
        private const float DetectionRadiusRatio = 0.3f;
        private static readonly int[] s_CubeIds = { 5, 6 };
        private static readonly int[][] s_Scales =
        {
            new[] { 0, 2, 4, 5, 6, 9 },
            new[] { 0, 2, 4, 5, 7, 9, 11 },
            new[] { 0, 2, 4, 7, 9 },
            new[] { 0, 7, 2, 9, 4, 11, 6, 1, 8, 3, 10, 5 }
        };

        private ScenarioContext _ctx;
        private Vector3 _center;
        private float _boundaryRadius;
        private float _sphereDiameter;
        private int _sphereCount = DefaultSphereCount;
        private float _speed = 0.65f;
        private ScenarioLayout _lastLayout;
        private bool _hasLayout;
        private System.Random _rng;

        private readonly Vector3[] _positions = new Vector3[AgentCapacity];
        private readonly Vector3[] _sphereOffsets = new Vector3[AgentCapacity];
        private readonly Vector3[] _velocities = new Vector3[AgentCapacity];
        private readonly int[][] _pitchClasses = new int[AgentCapacity][];
        private readonly int[] _octaves = new int[AgentCapacity];
        private readonly int[] _notes = new int[AgentCapacity];
        private int MusicalAgentCount => _sphereCount + s_CubeIds.Length;

        public void OnScenarioStart(ScenarioContext context)
        {
            _ctx = context;
            if (_ctx.Bus == null) return;

            bool room = context.Layout == ScenarioLayout.RoomScale;
            if (!_hasLayout || _lastLayout != context.Layout)
                _speed = room ? 1.1f : 0.65f;
            _lastLayout = context.Layout;
            _hasLayout = true;
            _rng = new System.Random(404);
            _center = context.ScenarioCenter;
            _boundaryRadius = room ? 2.5f : 1f;
            _sphereDiameter = room ? 0.14f : 0.07f;

            _ctx.Bus.AddBoundaryUnity(BoundaryId, AgentShapeType.Sphere, _center, _boundaryRadius);
            _ctx.Bus.SetBoundaryVisuals(BoundaryId, Color.green, 0.10f);
            AddSpheres(0, _sphereCount);
            _ctx.Bus.AddAgents(AgentShapeType.Cube, AgentMovementMode.Teleport, _sphereDiameter * 1.3f,
                "127.0.0.1", 6012, s_CubeIds);
            _ctx.Bus.ConfigureBoundaryHandle(BoundaryId, BoundaryHandleShape.Diamond,
                room ? 0.176f : 0.104f, new Color(0.25f, 1f, 0.15f));

            foreach (int id in s_CubeIds) InitializeAgent(id, false);

            _ctx.Bus.OnBoundaryGeometryChanged += HandleBoundaryGeometryChanged;
            _ctx.Bus.OnSetAgentMute += HandleMute;
        }

        // Keep the original cube IDs stable as spheres are added and removed.
        private static int SphereId(int index) => index < DefaultSphereCount ? index : index + s_CubeIds.Length;

        private int MusicalId(int index)
            => index < _sphereCount ? SphereId(index) : s_CubeIds[index - _sphereCount];

        private static bool IsSphere(int id) => id != s_CubeIds[0] && id != s_CubeIds[1];

        private void AddSpheres(int first, int count)
        {
            if (count == 0) return;
            var ids = new int[count];
            for (int i = 0; i < count; i++) ids[i] = SphereId(first + i);
            _ctx.Bus.AddAgents(AgentShapeType.Sphere, AgentMovementMode.Teleport, _sphereDiameter, "127.0.0.1", 6012, ids);
            foreach (int id in ids) InitializeAgent(id, true);
        }

        private void InitializeAgent(int i, bool autonomous)
        {
            Vector3 offset = RandomPointInUnitSphere();
            Vector3 initialOffset = offset * (_boundaryRadius * 0.95f);
            if (autonomous) _sphereOffsets[i] = initialOffset;
            _positions[i] = _center + initialOffset;
            _velocities[i] = autonomous ? offset.normalized * (_speed * (0.5f + (float)_rng.NextDouble())) : Vector3.zero;
            _pitchClasses[i] = s_Scales[_rng.Next(s_Scales.Length)];
            _octaves[i] = _rng.Next(1, 4);
            _notes[i] = -1;

            _ctx.Bus.SetAgentPositionUnity(i, _positions[i]);
            _ctx.Bus.SetAgentColor(i, autonomous ? Color.cyan : Color.red);
            _ctx.Bus.SetAgentInteractable(i, !autonomous);
            SetRadiusVisual(i, false);
            _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_OscillatorWaveform,
                (float)(autonomous ? SynthWaveformType.Sine : SynthWaveformType.Square));
            _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_AttackPrimary, 0.01f);
            _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_DecayPrimary, 0.1f);
            _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_SustainPrimary, 0.8f);
            _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_ReleasePrimary, 1f);
            _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_GainPrimary, autonomous ? 0.5f : 0.2f);
        }

        public void OnScenarioTick(float deltaTime)
        {
            if (_ctx.Bus == null) return;

            float dt = Mathf.Clamp(deltaTime, 0.001f, 0.05f);
            for (int index = 0; index < _sphereCount; index++)
            {
                int i = SphereId(index);
                Vector3 offset = _sphereOffsets[i];
                if (offset.sqrMagnitude >= _boundaryRadius * _boundaryRadius)
                {
                    Vector3 outward = offset.normalized;
                    Vector3 direction = RandomPointInUnitSphere().normalized;
                    if (Vector3.Dot(outward, direction) > 0f) direction = -direction;
                    _velocities[i] = direction * _velocities[i].magnitude;
                    offset = outward * (_boundaryRadius * 0.999f);
                }

                _sphereOffsets[i] = offset + _velocities[i] * dt;
                ApplySpherePosition(i);
            }

            foreach (int i in s_CubeIds)
            {
                if (_ctx.AgentManager != null && _ctx.AgentManager.TryGetAgent(i, out var cube) && cube != null)
                {
                    Vector3 position = cube.transform.position;
                    _velocities[i] = (position - _positions[i]) / dt;
                    _positions[i] = position;
                }
            }

            float radius = _boundaryRadius * DetectionRadiusRatio;
            for (int index = 0; index < MusicalAgentCount; index++)
            {
                int i = MusicalId(index);
                bool nearby = false;
                for (int other = 0; other < MusicalAgentCount; other++)
                {
                    int j = MusicalId(other);
                    if (i == j || (_positions[i] - _positions[j]).sqrMagnitude >= radius * radius) continue;
                    nearby = true;
                    break;
                }

                UpdateNote(i, nearby);
            }
        }

        private void ApplySpherePosition(int id)
        {
            _positions[id] = _center + _sphereOffsets[id];
            _ctx.Bus.SetAgentPositionUnity(id, _positions[id]);
        }

        private void UpdateNote(int id, bool nearby)
        {
            int note = -1;
            if (nearby)
            {
                int[] scale = _pitchClasses[id];
                int count = scale.Length * _octaves[id];
                float radialFraction = Mathf.Clamp01(Vector3.Distance(_positions[id], _center) / _boundaryRadius);
                int index = Mathf.Min((int)(radialFraction * count), count - 1);
                note = 48 + scale[index % scale.Length] + 12 * (index / scale.Length);
            }

            int previousNote = _notes[id];
            if (previousNote == note) return;
            if (previousNote >= 0)
                _ctx.Bus.SetAgentSynthNote(id, 0, previousNote, 0);
            if (note >= 0)
            {
                float speedForFullReverb = IsSphere(id) ? 2f : 5f;
                _ctx.Bus.SetAgentSynthParam(id, NativeDspSynthesizer.CC_ReverbPrimary,
                    Mathf.Clamp01(_velocities[id].magnitude / speedForFullReverb));
                _ctx.Bus.SetAgentSynthNote(id, 1, note, 127);
            }

            if ((previousNote >= 0) != nearby) SetRadiusVisual(id, nearby);
            _notes[id] = note;
        }

        private void SetRadiusVisual(int id, bool nearby)
        {
            Color color = IsSphere(id)
                ? (nearby ? Color.blue : Color.cyan)
                : (nearby ? Color.yellow : Color.red);
            _ctx.Bus.SetAgentRadiusVisual(id, _boundaryRadius * DetectionRadiusRatio, 0.10f, color);
        }

        private void HandleBoundaryGeometryChanged(int id, Vector3 center, float radius)
        {
            if (id != BoundaryId) return;
            if (!_center.Equals(center))
            {
                _center = center;
                for (int i = 0; i < _sphereCount; i++) ApplySpherePosition(SphereId(i));
            }

            if (_boundaryRadius == radius) return;
            _boundaryRadius = radius;
            for (int i = 0; i < MusicalAgentCount; i++)
            {
                int agentId = MusicalId(i);
                SetRadiusVisual(agentId, _notes[agentId] >= 0);
            }
        }

        private void HandleMute(int id, bool muted)
        {
            // Scenario pause flushes the synth. Re-evaluate held notes on resume.
            if (id != -1 || !muted) return;
            for (int i = 0; i < MusicalAgentCount; i++) UpdateNote(MusicalId(i), false);
        }

        private Vector3 RandomPointInUnitSphere()
        {
            Vector3 point;
            do
            {
                point = new Vector3((float)_rng.NextDouble(), (float)_rng.NextDouble(), (float)_rng.NextDouble());
                point = point * 2f - Vector3.one;
            } while (point.sqrMagnitude > 1f || point.sqrMagnitude < 0.000001f);
            return point;
        }

        public void OnScenarioStop()
        {
            if (_ctx.Bus == null) return;
            _ctx.Bus.OnBoundaryGeometryChanged -= HandleBoundaryGeometryChanged;
            _ctx.Bus.OnSetAgentMute -= HandleMute;
            _ctx.Bus.SetAgentMute(-1, true);
            _ctx.Bus.RemoveBoundaryHandle(BoundaryId);
            _ctx = default;
        }

        public void SetParameter(string paramName, float value)
        {
            if (!float.IsFinite(value)) return;
            if (string.Equals(paramName, "speed", StringComparison.OrdinalIgnoreCase))
                SetSpeed(Mathf.Clamp(value, 0.1f, 3f));
            else if (string.Equals(paramName, "agentCount", StringComparison.OrdinalIgnoreCase))
                SetSphereCount(Mathf.RoundToInt(Mathf.Clamp(value, 0f, MaxSphereCount)));
        }

        private void SetSphereCount(int count)
        {
            int previousCount = _sphereCount;
            _sphereCount = count;
            if (_ctx.Bus == null || count == previousCount) return;

            if (count > previousCount)
            {
                AddSpheres(previousCount, count - previousCount);
                return;
            }

            var ids = new int[previousCount - count];
            for (int i = 0; i < ids.Length; i++) ids[i] = SphereId(count + i);
            _ctx.Bus.RemoveAgents(ids);
        }

        public void ApplyPreset(int presetIndex)
        {
            SetSpeed(presetIndex == 0 ? 0.55f : (presetIndex == 1 ? 1.15f : 0.35f));
        }

        private void SetSpeed(float speed)
        {
            float ratio = speed / _speed;
            _speed = speed;
            for (int i = 0; i < _sphereCount; i++) _velocities[SphereId(i)] *= ratio;
        }
    }
}
