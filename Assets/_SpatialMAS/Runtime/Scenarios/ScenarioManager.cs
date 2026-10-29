using System;
using System.Collections.Generic;
using SpatialMAS.Agents;
using SpatialMAS.Core;
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

        public IReadOnlyList<IScenarioModule> RegisteredScenarios => _scenarios;
        public IScenarioModule ActiveScenario => _activeScenario;
        public int ActiveScenarioId => _activeScenario != null ? _activeScenario.ScenarioId : 0;
        public string ActiveScenarioName => _activeScenario != null ? _activeScenario.DisplayName : "None";
        public bool IsPaused => _isPaused;

        public event Action<IScenarioModule> OnScenarioChanged;

        public void Initialize(
            SimulationCommandBus bus,
            SpatialCoordinateMapper mapper,
            AgentManager agentManager,
            XRUserEmbodimentBridge embodiment,
            int initialScenarioId = 0)
        {
            _context = new ScenarioContext
            {
                Bus = bus,
                CoordinateMapper = mapper,
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
            }

            LoadScenarioById(initialScenarioId);
        }

        private void OnDestroy()
        {
            if (_context.Bus != null)
            {
                _context.Bus.OnLoadScenario -= LoadScenarioById;
                _context.Bus.OnSetScenarioParam -= SetActiveScenarioParameter;
            }
            _activeScenario?.OnScenarioStop();
        }

        public void RegisterScenario(IScenarioModule module)
        {
            if (module == null) return;
            _scenarios.Add(module);
        }

        public void LoadScenarioById(int scenarioId)
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

            if (target == null && _scenarios.Count > 0)
            {
                target = _scenarios[0];
            }

            if (target == null) return;

            _isPaused = false;
            _activeScenario?.OnScenarioStop();

            // Clean world before starting new scenario (unless switching to External OSC when agents are already active)
            if (target.ScenarioId != 0)
            {
                _context.Bus?.RemoveAgents(null);
                _context.Bus?.RemoveBoundary(-1);
                _context.Bus?.RemoveArrow(-1);
            }

            _activeScenario = target;
            _activeScenario.OnScenarioStart(_context);
            OnScenarioChanged?.Invoke(_activeScenario);
            Debug.Log($"[ScenarioManager] Activated Scenario [{_activeScenario.ScenarioId}]: {_activeScenario.DisplayName}");
        }

        public void ResetActiveScenario()
        {
            int id = ActiveScenarioId;
            if (id == 0)
            {
                _context.Bus?.RemoveAgents(null);
                _context.Bus?.RemoveBoundary(-1);
                _context.Bus?.RemoveArrow(-1);
            }
            LoadScenarioById(id);
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
            _activeScenario?.SetParameter(paramName, value);
        }

        public void ApplyActiveScenarioPreset(int presetIndex)
        {
            _activeScenario?.ApplyPreset(presetIndex);
        }

        private void Update()
        {
            if (_isPaused) return;
            _activeScenario?.OnScenarioTick(Time.deltaTime);
        }
    }
}
