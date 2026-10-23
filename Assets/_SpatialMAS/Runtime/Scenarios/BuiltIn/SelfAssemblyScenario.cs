using System.Collections.Generic;
using SpatialMAS.Audio;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Scenarios.BuiltIn
{
    /// <summary>
    /// Scenario 5: Self-Assembly (Autonomous & Interactive 6-Face Musical Cube Structures).
    /// Clean-room architecture of ACSOS 2024 SelfAssemblyMain.cs & SelfAssemblyAgent.cs (.Music.cs, .Sync.cs).
    /// Features:
    /// - Cubic agents with 6 orthogonal face connection slots (+X, -X, +Z, -Z, +Y, -Y).
    /// - 8-state autonomous + interactive state machine (Wandering, WanderingAndDetecting, ToJoin, IndirectToJoin,
    ///   Joined, WanderingWithUser, JoinedWithUser, Frozen).
    /// - 90-degree face snapping and coupled internal phasor (_cyclePhasor) synchronization across connected structures.
    /// - Polyrhythmic face-slot MIDI note sequencing where Attack/Release scale with structure size.
    /// </summary>
    public class SelfAssemblyScenario : IScenarioModule
    {
        public enum AssemblyState
        {
            Wandering = 0,              // Orange
            WanderingAndDetecting = 1,  // Cyan
            ToJoin = 2,                 // Green
            IndirectToJoin = 3,         // Yellow
            Joined = 4,                 // Blue
            WanderingWithUser = 5,      // White
            JoinedWithUser = 6,         // Purple
            Frozen = 7                  // Magenta
        }

        public class CubeNode
        {
            public int Id;
            public AssemblyState State = AssemblyState.Wandering;
            public Vector3 Position;
            public Quaternion Rotation = Quaternion.identity;
            public Vector3 Velocity;
            public float StateTimer;

            // Assembly graph: 6 orthogonal face slots (0:+X, 1:-X, 2:+Z, 3:-Z, 4:+Y, 5:-Y)
            public readonly int[] ConnectedNeighborIds = { -1, -1, -1, -1, -1, -1 };
            public int TargetParentId = -1;
            public int TargetSlotIndex = -1;
            public int RootStructureId;

            // Musical Phasor & Polyrhythmic Sequencer
            public float CyclePhasor; // [0, 1)
            public int RhythmDivision = 1; // 1, 2, 4, or 8
            public int ActiveFaceStepIndex;
            public readonly int[] FaceMidiNotes = new int[6];
            public float NoteGateTimer;
        }

        public int ScenarioId => 5;
        public string DisplayName => "5. Self-Assembly (6-Face Musical Cubes)";
        public string ShortDescription => "Autonomous & hand-assemblable 6-face cubes that snap at 90 degrees and synchronize polyrhythmic musical phasors.";

        private static readonly Vector3[] s_LocalSlotDirections =
        {
            Vector3.right,   // 0: +X
            Vector3.left,    // 1: -X
            Vector3.forward, // 2: +Z
            Vector3.back,    // 3: -Z
            Vector3.up,      // 4: +Y
            Vector3.down     // 5: -Y
        };

        private static readonly int[] s_OppositeSlot = { 1, 0, 3, 2, 5, 4 };
        private static readonly int[] s_MajorPentatonicMidi = { 60, 62, 64, 67, 69, 72, 74, 76, 79, 81, 84 };
        private static readonly int[] s_RhythmDivisions = { 1, 2, 2, 4, 4, 8 };

        private ScenarioContext _ctx;
        private int _cubeCount = 18;
        private float _cubeSizeMeters = 0.085f;
        private float _baseTempoHz = 0.65f;
        private int _maxStructureSize = 10;
        private float _wanderingDuration = 3.5f;
        private float _maxJoinedDuration = 18.0f;

        private CubeNode[] _nodes;

        public IReadOnlyList<CubeNode> Nodes => _nodes;

        public void OnScenarioStart(ScenarioContext context)
        {
            _ctx = context;
            SpawnSelfAssemblyCubes();
        }

        private void SpawnSelfAssemblyCubes()
        {
            if (_ctx.Bus == null) return;

            Vector3 origin = _ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.OriginOffset : new Vector3(0f, 1.2f, 0.8f);
            float scale = _ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.WorldScale : 0.2f;
            _cubeSizeMeters = Mathf.Clamp(0.42f * scale, 0.055f, 0.16f);
            float arenaRadius = Mathf.Clamp(4.5f * scale, 0.55f, 2.5f);

            _ctx.Bus.InstantiateAgents(AgentShapeType.Cube, AgentMovementMode.Teleport, _cubeCount, _cubeSizeMeters, "127.0.0.1", 6012);

            _nodes = new CubeNode[_cubeCount];
            var rng = new System.Random(505);

            for (int i = 0; i < _cubeCount; i++)
            {
                var node = new CubeNode
                {
                    Id = i,
                    RootStructureId = i,
                    State = AssemblyState.Wandering,
                    StateTimer = (float)(rng.NextDouble() * _wanderingDuration),
                    CyclePhasor = (float)rng.NextDouble(),
                    RhythmDivision = s_RhythmDivisions[i % s_RhythmDivisions.Length],
                    Position = origin + new Vector3(
                        (float)(rng.NextDouble() * 2.0 - 1.0) * arenaRadius * 0.7f,
                        (float)(rng.NextDouble() * 2.0 - 1.0) * arenaRadius * 0.45f,
                        (float)(rng.NextDouble() * 2.0 - 1.0) * arenaRadius * 0.7f),
                    Velocity = new Vector3(
                        (float)(rng.NextDouble() * 2.0 - 1.0),
                        (float)(rng.NextDouble() * 2.0 - 1.0),
                        (float)(rng.NextDouble() * 2.0 - 1.0)).normalized * 0.22f
                };

                for (int f = 0; f < 6; f++)
                {
                    node.FaceMidiNotes[f] = s_MajorPentatonicMidi[(i + f * 2) % s_MajorPentatonicMidi.Length];
                }

                _nodes[i] = node;

                _ctx.Bus.SetAgentPositionUnity(i, node.Position);
                _ctx.Bus.SetAgentInteractable(i, true);
                _ctx.Bus.SetAgentColor(i, GetColorForState(node.State));

                if (_ctx.AgentManager != null && _ctx.AgentManager.TryGetAgent(i, out var ent) && ent != null)
                {
                    ent.SnapRotation90DegreesOnRelease = true;
                }

                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_OscillatorWaveform, (float)SynthWaveformType.Triangle);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_FilterCutoffHz, 3200f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_ReverbPrimary, 0.22f);
                _ctx.Bus.SetAgentSynthParam(i, NativeDspSynthesizer.CC_GainPrimary, 0.18f);
            }
        }

        public void OnScenarioTick(float deltaTime)
        {
            if (_ctx.Bus == null || _nodes == null) return;

            float dt = Mathf.Clamp(deltaTime, 0.001f, 0.05f);
            Vector3 origin = _ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.OriginOffset : new Vector3(0f, 1.2f, 0.8f);
            float scale = _ctx.CoordinateMapper != null ? _ctx.CoordinateMapper.WorldScale : 0.2f;
            float arenaRadius = Mathf.Clamp(4.5f * scale, 0.55f, 2.5f);
            float joinDetectRadius = _cubeSizeMeters * 3.2f;

            // 1. Check User Hand / Mouse Grab States
            for (int i = 0; i < _nodes.Length; i++)
            {
                CubeNode node = _nodes[i];
                bool isGrabbed = false;
                if (_ctx.AgentManager != null && _ctx.AgentManager.TryGetAgent(i, out var ent) && ent != null)
                {
                    isGrabbed = ent.IsGrabbedByUser;
                    if (isGrabbed)
                    {
                        node.Position = ent.transform.position;
                        node.Rotation = SnapQuaternion90(ent.transform.rotation);
                    }
                }

                if (isGrabbed)
                {
                    if (HasAnyConnectedNeighbors(node))
                    {
                        node.State = AssemblyState.JoinedWithUser;
                    }
                    else
                    {
                        node.State = AssemblyState.WanderingWithUser;
                    }
                }
                else if (node.State == AssemblyState.WanderingWithUser)
                {
                    node.State = AssemblyState.WanderingAndDetecting;
                    node.StateTimer = _wanderingDuration;
                }
                else if (node.State == AssemblyState.JoinedWithUser)
                {
                    node.State = AssemblyState.Joined;
                    node.StateTimer = _maxJoinedDuration;
                }
            }

            // 2. Step Autonomous State Machine & Assembly Docking
            for (int i = 0; i < _nodes.Length; i++)
            {
                CubeNode node = _nodes[i];

                switch (node.State)
                {
                    case AssemblyState.Wandering:
                        DriftWithinArena(node, origin, arenaRadius, dt);
                        node.StateTimer -= dt;
                        if (node.StateTimer <= 0f)
                        {
                            node.State = AssemblyState.WanderingAndDetecting;
                        }
                        break;

                    case AssemblyState.WanderingAndDetecting:
                        DriftWithinArena(node, origin, arenaRadius, dt);
                        TryFindAssemblyTarget(node, joinDetectRadius);
                        break;

                    case AssemblyState.ToJoin:
                    case AssemblyState.IndirectToJoin:
                        StepDockingToTarget(node, dt);
                        break;

                    case AssemblyState.Joined:
                        // Stay rigidly snapped to parent slot if attached to a parent
                        if (node.TargetParentId >= 0 && node.TargetParentId < _nodes.Length && node.TargetSlotIndex >= 0)
                        {
                            CubeNode parent = _nodes[node.TargetParentId];
                            node.Position = GetSlotWorldPosition(parent.Position, parent.Rotation, node.TargetSlotIndex, _cubeSizeMeters);
                            node.Rotation = parent.Rotation;
                        }
                        node.StateTimer -= dt;
                        if (node.StateTimer <= 0f)
                        {
                            DetachNode(node);
                        }
                        break;
                }

                // 3. Advance Coupled Musical Phasor & Trigger Face-Slot Notes
                StepMusicalPhasor(node, dt);

                // 4. Push Visual & Transform State
                _ctx.Bus.SetAgentPositionUnity(i, node.Position);
                _ctx.Bus.SetAgentColor(i, GetColorForState(node.State));
            }
        }

        private void DriftWithinArena(CubeNode node, Vector3 center, float radius, float dt)
        {
            node.Position += node.Velocity * dt;
            Vector3 offset = node.Position - center;
            if (offset.magnitude > radius)
            {
                node.Velocity = -offset.normalized * 0.24f;
                node.Position = center + offset.normalized * (radius * 0.95f);
            }
        }

        private void TryFindAssemblyTarget(CubeNode seeker, float detectRadius)
        {
            for (int j = 0; j < _nodes.Length; j++)
            {
                if (j == seeker.Id) continue;
                CubeNode candidate = _nodes[j];
                if (candidate.State != AssemblyState.WanderingAndDetecting &&
                    candidate.State != AssemblyState.Joined &&
                    candidate.State != AssemblyState.JoinedWithUser)
                {
                    continue;
                }

                float d = Vector3.Distance(seeker.Position, candidate.Position);
                if (d > detectRadius) continue;

                if (CountStructureSize(candidate.RootStructureId) >= _maxStructureSize)
                    continue;

                int freeSlot = FindNearestFreeSlot(candidate, seeker.Position);
                if (freeSlot >= 0)
                {
                    seeker.TargetParentId = candidate.Id;
                    seeker.TargetSlotIndex = freeSlot;
                    seeker.State = AssemblyState.ToJoin;
                    return;
                }
            }
        }

        private void StepDockingToTarget(CubeNode seeker, float dt)
        {
            if (seeker.TargetParentId < 0 || seeker.TargetParentId >= _nodes.Length || seeker.TargetSlotIndex < 0)
            {
                seeker.State = AssemblyState.Wandering;
                seeker.StateTimer = _wanderingDuration;
                return;
            }

            CubeNode parent = _nodes[seeker.TargetParentId];
            int slot = seeker.TargetSlotIndex;
            if (parent.ConnectedNeighborIds[slot] != -1 && parent.ConnectedNeighborIds[slot] != seeker.Id)
            {
                // Slot was taken by another cube
                seeker.TargetParentId = -1;
                seeker.TargetSlotIndex = -1;
                seeker.State = AssemblyState.WanderingAndDetecting;
                return;
            }

            Vector3 targetPos = GetSlotWorldPosition(parent.Position, parent.Rotation, slot, _cubeSizeMeters);
            seeker.Position = Vector3.MoveTowards(seeker.Position, targetPos, 0.55f * dt);
            seeker.Rotation = Quaternion.Slerp(seeker.Rotation, parent.Rotation, 10f * dt);

            if (Vector3.Distance(seeker.Position, targetPos) <= 0.004f)
            {
                // Snap into Joined state & synchronize phasor!
                seeker.Position = targetPos;
                seeker.Rotation = parent.Rotation;
                parent.ConnectedNeighborIds[slot] = seeker.Id;
                int oppSlot = s_OppositeSlot[slot];
                seeker.ConnectedNeighborIds[oppSlot] = parent.Id;

                seeker.RootStructureId = parent.RootStructureId;
                seeker.CyclePhasor = parent.CyclePhasor; // Synchronize musical cycle phasor!
                seeker.State = AssemblyState.Joined;
                seeker.StateTimer = _maxJoinedDuration;

                if (parent.State == AssemblyState.WanderingAndDetecting)
                {
                    parent.State = AssemblyState.Joined;
                    parent.StateTimer = _maxJoinedDuration;
                }
            }
        }

        private void DetachNode(CubeNode node)
        {
            for (int s = 0; s < 6; s++)
            {
                int nId = node.ConnectedNeighborIds[s];
                if (nId >= 0 && nId < _nodes.Length)
                {
                    CubeNode neighbor = _nodes[nId];
                    for (int ns = 0; ns < 6; ns++)
                    {
                        if (neighbor.ConnectedNeighborIds[ns] == node.Id)
                        {
                            neighbor.ConnectedNeighborIds[ns] = -1;
                        }
                    }
                }
                node.ConnectedNeighborIds[s] = -1;
            }

            node.TargetParentId = -1;
            node.TargetSlotIndex = -1;
            node.RootStructureId = node.Id;
            node.State = AssemblyState.Wandering;
            node.StateTimer = _wanderingDuration;
            node.Velocity = Random.onUnitSphere * 0.28f;
            _ctx.Bus?.SetAgentSynthNote(node.Id, 0, 60, 0);
        }

        private void StepMusicalPhasor(CubeNode node, float dt)
        {
            if (node.NoteGateTimer > 0f)
            {
                node.NoteGateTimer -= dt;
                if (node.NoteGateTimer <= 0f)
                {
                    _ctx.Bus?.SetAgentSynthNote(node.Id, 0, 60, 0);
                }
            }

            // Coupled phasor synchronization: pull toward root structure's phasor when joined
            if (node.RootStructureId >= 0 && node.RootStructureId < _nodes.Length && node.RootStructureId != node.Id)
            {
                node.CyclePhasor = _nodes[node.RootStructureId].CyclePhasor;
            }
            else
            {
                node.CyclePhasor += _baseTempoHz * dt;
                if (node.CyclePhasor >= 1.0f) node.CyclePhasor -= 1.0f;
            }

            if (node.State != AssemblyState.Joined &&
                node.State != AssemblyState.JoinedWithUser &&
                node.State != AssemblyState.Frozen)
            {
                return;
            }

            // Check subdivision trigger
            float subPhase = (node.CyclePhasor * node.RhythmDivision) % 1.0f;
            float prevSubPhase = ((node.CyclePhasor - _baseTempoHz * dt) * node.RhythmDivision) % 1.0f;
            if (prevSubPhase < 0f) prevSubPhase += 1.0f;

            if (subPhase < prevSubPhase)
            {
                // Find next occupied face slot and play its assigned scale MIDI note
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    int face = (node.ActiveFaceStepIndex + attempt) % 6;
                    if (node.ConnectedNeighborIds[face] != -1)
                    {
                        node.ActiveFaceStepIndex = (face + 1) % 6;
                        int structCount = Mathf.Max(1, CountStructureSize(node.RootStructureId));
                        float sizeFactor = Mathf.InverseLerp(2f, 12f, structCount);

                        // Scale ADSR Attack/Release & Gain by structure size (mirrors SelfAssemblyAgent.Music.cs)
                        float attack = Mathf.Lerp(0.015f, 0.18f, sizeFactor);
                        float release = Mathf.Lerp(0.12f, 0.45f, sizeFactor);
                        float gain = 0.28f / Mathf.Sqrt(structCount);

                        _ctx.Bus.SetAgentSynthParam(node.Id, NativeDspSynthesizer.CC_AttackPrimary, attack);
                        _ctx.Bus.SetAgentSynthParam(node.Id, NativeDspSynthesizer.CC_ReleasePrimary, release);
                        _ctx.Bus.SetAgentSynthParam(node.Id, NativeDspSynthesizer.CC_GainPrimary, gain);
                        _ctx.Bus.SetAgentSynthNote(node.Id, 1, node.FaceMidiNotes[face], 98);
                        node.NoteGateTimer = Mathf.Clamp((1.0f / (_baseTempoHz * node.RhythmDivision)) * 0.65f, 0.08f, 0.5f);
                        break;
                    }
                }
            }
        }

        private int CountStructureSize(int rootId)
        {
            int count = 0;
            for (int i = 0; i < _nodes.Length; i++)
            {
                if (_nodes[i].RootStructureId == rootId) count++;
            }
            return Mathf.Max(1, count);
        }

        private static bool HasAnyConnectedNeighbors(CubeNode node)
        {
            for (int i = 0; i < 6; i++)
            {
                if (node.ConnectedNeighborIds[i] != -1) return true;
            }
            return false;
        }

        private int FindNearestFreeSlot(CubeNode target, Vector3 seekerPos)
        {
            int bestSlot = -1;
            float bestDist = float.MaxValue;
            for (int s = 0; s < 6; s++)
            {
                if (target.ConnectedNeighborIds[s] != -1) continue;
                Vector3 slotPos = GetSlotWorldPosition(target.Position, target.Rotation, s, _cubeSizeMeters);
                float d = Vector3.Distance(seekerPos, slotPos);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestSlot = s;
                }
            }
            return bestSlot;
        }

        /// <summary>
        /// Computes the exact world position of one of the 6 orthogonal cube face slots (0..5).
        /// Callable from EditMode unit tests.
        /// </summary>
        public static Vector3 GetSlotWorldPosition(Vector3 cubeCenter, Quaternion cubeRotation, int slotIndex, float cubeSizeMeters)
        {
            int clamped = Mathf.Clamp(slotIndex, 0, 5);
            return cubeCenter + (cubeRotation * s_LocalSlotDirections[clamped]) * cubeSizeMeters;
        }

        public static Quaternion SnapQuaternion90(Quaternion q)
        {
            Vector3 euler = q.eulerAngles;
            euler.x = Mathf.Round(euler.x / 90f) * 90f;
            euler.y = Mathf.Round(euler.y / 90f) * 90f;
            euler.z = Mathf.Round(euler.z / 90f) * 90f;
            return Quaternion.Euler(euler);
        }

        public static Color GetColorForState(AssemblyState state)
        {
            switch (state)
            {
                case AssemblyState.Wandering: return new Color(1.0f, 0.55f, 0.10f, 1.0f);             // Orange
                case AssemblyState.WanderingAndDetecting: return new Color(0.10f, 0.92f, 0.95f, 1.0f); // Cyan
                case AssemblyState.ToJoin: return new Color(0.18f, 0.92f, 0.32f, 1.0f);                // Green
                case AssemblyState.IndirectToJoin: return new Color(0.98f, 0.90f, 0.15f, 1.0f);        // Yellow
                case AssemblyState.Joined: return new Color(0.18f, 0.48f, 1.0f, 1.0f);                 // Blue
                case AssemblyState.WanderingWithUser: return new Color(0.96f, 0.96f, 0.98f, 1.0f);     // White
                case AssemblyState.JoinedWithUser: return new Color(0.68f, 0.25f, 0.98f, 1.0f);        // Purple
                case AssemblyState.Frozen:
                default: return new Color(0.98f, 0.15f, 0.75f, 1.0f);                                  // Magenta
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
                case "tempo": _baseTempoHz = Mathf.Clamp(value, 0.2f, 2.5f); break;
                case "maxsize": _maxStructureSize = Mathf.Clamp(Mathf.RoundToInt(value), 2, 18); break;
            }
        }

        public void ApplyPreset(int presetIndex)
        {
            switch (presetIndex)
            {
                case 0: _baseTempoHz = 0.65f; _maxStructureSize = 8; break;
                case 1: _baseTempoHz = 1.10f; _maxStructureSize = 14; break;
                case 2: _baseTempoHz = 0.40f; _maxStructureSize = 4; break;
            }
        }
    }
}
