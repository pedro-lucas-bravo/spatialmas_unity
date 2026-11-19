using System;
using System.Collections.Generic;
using SpatialMAS.Agents;
using SpatialMAS.Core;
using SpatialMAS.Environment;
using SpatialMAS.Scenarios.BuiltIn;
using SpatialMAS.XR;
using UnityEngine;

namespace SpatialMAS.Scenarios
{
    /// <summary>
    /// Manages the registry and lifecycle of Built-in No-Code Scenarios and modular Logic Packs.
    /// Allows researchers to switch, pause/resume, or reset scenarios with 1 click in XR/Desktop or via OSC (/scenario/load).
    /// </summary>
    public class ScenarioManager : MonoBehaviour
    {
        private readonly List<IScenarioModule> _scenarios = new List<IScenarioModule>();
        private ScenarioContext _context;
        private IScenarioModule _activeScenario;
        private bool _isPaused;
        private BoundaryManager _boundaries;
        private EnvironmentArrowManager _arrows;
        public event Action<string, string> WorldActionExecuted;

        [Serializable]
        private struct WorldAction
        {
            public string source;
            public int fromScenario, toScenario;
            public int agentsRemoved, boundariesRemoved, arrowsRemoved;
        }

        public IReadOnlyList<IScenarioModule> RegisteredScenarios => _scenarios;
        public IScenarioModule ActiveScenario => _activeScenario;
        public int ActiveScenarioId => _activeScenario != null ? _activeScenario.ScenarioId : 0;
        public string ActiveScenarioName => _activeScenario != null ? _activeScenario.DisplayName : "None";
        public bool IsPaused => _isPaused;
        public Vector3 NextScenarioCenter => _context.ScenarioCenter;
        public ScenarioLayout NextLayout => _context.Layout;
        public ScenarioLayout ActiveLayout { get; private set; }
        public Vector3 ActiveScenarioCenter { get; private set; }

        public event Action<IScenarioModule> OnScenarioChanged;
        public event Action<int> PresetApplied;
        public event Action<string, float> ParameterApplied;

        public void Initialize(
            SimulationCommandBus bus,
            Vector3 scenarioCenter,
            AgentManager agentManager,
            XRUserEmbodimentBridge embodiment,
            int initialScenarioId = 0,
            ScenarioLayout layout = ScenarioLayout.Compact,
            BoundaryManager boundaries = null, EnvironmentArrowManager arrows = null)
        {
            _boundaries = boundaries;
            _arrows = arrows;
            _context = new ScenarioContext
            {
                Bus = bus,
                ScenarioCenter = scenarioCenter,
                Layout = layout,
                AgentManager = agentManager,
                Embodiment = embodiment
            };

            _scenarios.Clear();
            RegisterScenario(new ExternalOscScenario());
            RegisterScenario(new CrowdAvoidanceScenario());
            RegisterScenario(new SwarmalatorsScenario());
            RegisterScenario(new AttractorsRepellersScenario());
            RegisterScenario(new MusicalEncountersScenario());
            RegisterScenario(new SelfAssemblyScenario());

            if (bus != null)
            {
                bus.OnLoadScenario += LoadScenarioById;
                bus.OnSetScenarioParam += SetActiveScenarioParameter;
                bus.OnSetScenarioCenter += SetNextScenarioCenter;
                bus.OnSetScenarioLayout += SetNextLayout;
                bus.OnBoundaryGeometryChanged += HandleBoundaryGeometryChanged;
            }

            LoadScenario(initialScenarioId, null);
        }

        private void OnDestroy()
        {
            if (_context.Bus != null)
            {
                _context.Bus.OnLoadScenario -= LoadScenarioById;
                _context.Bus.OnSetScenarioParam -= SetActiveScenarioParameter;
                _context.Bus.OnSetScenarioCenter -= SetNextScenarioCenter;
                _context.Bus.OnSetScenarioLayout -= SetNextLayout;
                _context.Bus.OnBoundaryGeometryChanged -= HandleBoundaryGeometryChanged;
            }
            _activeScenario?.OnScenarioStop();
        }

        public void RegisterScenario(IScenarioModule module)
        {
            if (module == null) return;
            _scenarios.Add(module);
        }

        private void HandleBoundaryGeometryChanged(int id, Vector3 center, float size)
        {
            if (_activeScenario is MusicalEncountersScenario && id == MusicalEncountersScenario.BoundaryId)
                ActiveScenarioCenter = center;
        }

        public void LoadScenarioById(int scenarioId) => LoadScenario(scenarioId, "local");

        private void LoadScenarioById(int scenarioId, string source) => LoadScenario(scenarioId, source);

        private void LoadScenario(int scenarioId, string source)
        {
            IScenarioModule target = null;
            for (int i = 0; i < _scenarios.Count; i++)
            {
                if (_scenarios[i].ScenarioId == scenarioId)
                {
                    target = _scenarios[i];
                    break;
                }
            }

            if (target == null) return;

            bool switched = _activeScenario != null && ActiveScenarioId != scenarioId;
            var action = DescribeCleanup(source, scenarioId);
            _isPaused = false;
            _activeScenario?.OnScenarioStop();
            ClearWorld();

            ActiveLayout = _context.Layout;
            ActiveScenarioCenter = _context.ScenarioCenter;
            _activeScenario = target;
            _activeScenario.OnScenarioStart(_context);
            OnScenarioChanged?.Invoke(_activeScenario);
            if (switched && source != null)
                WorldActionExecuted?.Invoke("scenario_switch", JsonUtility.ToJson(action));
            Debug.Log($"[ScenarioManager] Activated Scenario [{_activeScenario.ScenarioId}]: {_activeScenario.DisplayName}");
        }

        public void ResetActiveScenario() => LoadScenario(ActiveScenarioId, null);

        public void ClearAll(string source)
        {
            var action = DescribeCleanup(source, ActiveScenarioId);
            ClearWorld();
            WorldActionExecuted?.Invoke("clear_all", JsonUtility.ToJson(action));
        }

        private WorldAction DescribeCleanup(string source, int destination) => new WorldAction
        {
            source = source,
            fromScenario = ActiveScenarioId,
            toScenario = destination,
            agentsRemoved = _context.AgentManager?.ActiveCount ?? -1,
            boundariesRemoved = _boundaries?.ActiveBoundaries.Count ?? -1,
            arrowsRemoved = _arrows?.ActiveArrows.Count ?? -1
        };

        private void ClearWorld()
        {
            _context.Bus?.FlushAgentSynth(-1);
            _context.Bus?.RemoveAgents(null);
            _context.Bus?.RemoveBoundary(-1);
            _context.Bus?.RemoveArrow(-1);
        }

        public void TogglePause()
        {
            SetPaused(!_isPaused);
        }

        public void SetPaused(bool paused)
        {
            _isPaused = paused;
            if (_context.Bus != null)
            {
                _context.Bus.SetAgentMute(-1, _isPaused);
                if (_isPaused)
                {
                    _context.Bus.FlushAgentSynth(-1);
                }
            }
        }

        public void SetActiveScenarioParameter(string paramName, float value)
        {
            if (_activeScenario == null) return;
            _activeScenario.SetParameter(paramName, value);
            ParameterApplied?.Invoke(paramName, value);
        }

        public void ApplyActiveScenarioPreset(int presetIndex)
        {
            if (_activeScenario == null) return;
            _activeScenario.ApplyPreset(presetIndex);
            PresetApplied?.Invoke(presetIndex);
        }

        public void SetNextScenarioCenter(Vector3 centerMeters)
        {
            if (!float.IsNaN(centerMeters.x) && !float.IsInfinity(centerMeters.x) &&
                !float.IsNaN(centerMeters.y) && !float.IsInfinity(centerMeters.y) &&
                !float.IsNaN(centerMeters.z) && !float.IsInfinity(centerMeters.z))
                _context.ScenarioCenter = centerMeters;
        }

        public void SetNextLayout(ScenarioLayout layout)
        {
            if (layout == ScenarioLayout.Compact || layout == ScenarioLayout.RoomScale)
                _context.Layout = layout;
        }

        private void Update()
        {
            if (_isPaused) return;
            _activeScenario?.OnScenarioTick(Time.deltaTime);
        }
    }
}
