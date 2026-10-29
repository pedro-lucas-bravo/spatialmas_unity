using SpatialMAS.Agents;
using SpatialMAS.Core;
using SpatialMAS.XR;

namespace SpatialMAS.Scenarios
{
    public struct ScenarioContext
    {
        public SimulationCommandBus Bus;
        public SpatialCoordinateMapper CoordinateMapper;
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

    /// <summary>Rebases simulation state when the arena's spatial calibration changes.</summary>
    public interface ISpatialFrameAwareScenario
    {
        void ApplySpatialFrameChange(SpatialFrameChange change);
    }
}
