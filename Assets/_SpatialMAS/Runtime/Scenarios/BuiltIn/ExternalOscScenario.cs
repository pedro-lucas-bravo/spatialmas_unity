namespace SpatialMAS.Scenarios.BuiltIn
{
    /// <summary>
    /// Scenario 0: Pure External OSC Controller Mode (100% HS-ims Compatible).
    /// SpatialMAS acts as a real-time XR/Desktop spatial transceiver driven by external Python, Max, or C++ scripts.
    /// </summary>
    public class ExternalOscScenario : IScenarioModule
    {
        public int ScenarioId => 0;
        public string DisplayName => "0. External OSC Mode (HS-ims)";
        public string ShortDescription => "Driven externally over UDP port 6011 by Python, Max/MSP, or C++ scripts.";

        public void OnScenarioStart(ScenarioContext context)
        {
            // Built-in scenarios mute their agents when stopping. Restore the preserved
            // agents when handing control to the external OSC controller.
            context.Bus?.SetAgentMute(-1, false);
        }
        public void OnScenarioTick(float deltaTime) { }
        public void OnScenarioStop() { }
        public void SetParameter(string paramName, float value) { }
        public void ApplyPreset(int presetIndex) { }
    }
}
