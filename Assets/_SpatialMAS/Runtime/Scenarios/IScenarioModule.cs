using SpatialMAS.Agents;
using SpatialMAS.Core;
using SpatialMAS.XR;
using UnityEngine;

namespace SpatialMAS.Scenarios
{
    public struct ScenarioContext
    {
        public SimulationCommandBus Bus;
        public Vector3 ScenarioCenter;
        public ScenarioLayout Layout;
        public AgentManager AgentManager;
        public XRUserEmbodimentBridge Embodiment;
    }

    /// <summary>
    /// Standard modular contract for both Built-In No-Code Scenarios and external Logic Pack add-ons.
    /// </summary>
    public interface IScenarioModule
    {
        int ScenarioId { get; }
        string DisplayName { get; }
        string ShortDescription { get; }

        void OnScenarioStart(ScenarioContext context);
        void OnScenarioTick(float deltaTime);
        void OnScenarioStop();
        void SetParameter(string paramName, float value);
        void ApplyPreset(int presetIndex);
    }

}
