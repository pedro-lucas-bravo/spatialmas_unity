namespace SpatialMAS.Audio
{
    public enum SynthWaveformType
    {
        Sine = 0,
        Square = 1,
        Sawtooth = 2,
        Triangle = 3
    }

    /// <summary>
    /// Hardware-independent interface for per-agent spatial sound synthesis.
    /// Implemented by default via NativeDspSynthesizer (zero-dependency C# DSP for Quest 3, Apple Vision Pro,
    /// Windows, macOS, and Linux) and extensible for optional CsoundUnity / FMOD adapters.
    /// </summary>
    public interface IAgentAudioSynth
    {
        bool IsMuted { get; set; }
        SynthWaveformType Waveform { get; set; }
        float FrequencyHz { get; set; }
        float Gain { get; set; }
        float AttackSeconds { get; set; }
        float DecaySeconds { get; set; }
        float SustainLevel { get; set; }
        float ReleaseSeconds { get; set; }
        float FilterCutoffHz { get; set; }
        float FilterResonanceQ { get; set; }
        float ReverbMix { get; set; }

        void SetNote(int noteAction, int midiNote, int velocity = 100);
        void SetParameter(int ccId, float value);
        void SynthesizeBlock(float[] data, int channels, int sampleRate);
    }
}
