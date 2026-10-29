using NUnit.Framework;
using SpatialMAS.Audio;
using UnityEngine;

namespace SpatialMAS.Tests.EditMode
{
    public class NativeDspSynthesizerTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void ContinuousTone_StartsRegardlessOfFrequencyGainOrder(bool frequencyFirst)
        {
            var go = new GameObject("ContinuousToneTest");
            try
            {
                var synth = go.AddComponent<NativeDspSynthesizer>();
                synth.ReverbMix = 0f;
                synth.SetParameter(frequencyFirst ? 2 : 16, frequencyFirst ? 440f : 0.5f);
                synth.SetParameter(frequencyFirst ? 16 : 2, frequencyFirst ? 0.5f : 440f);
                var buffer = new float[4096];
                synth.SynthesizeBlock(buffer, 1, 48000);
                Assert.Greater(Rms(buffer), 0.05f);

                synth.SetParameter(16, 0f);
                for (int block = 0; block < 10; block++) synth.SynthesizeBlock(buffer, 1, 48000);
                Assert.Less(Rms(buffer), 0.0001f);
                synth.SetParameter(16, 0.5f);
                synth.SynthesizeBlock(buffer, 1, 48000);
                Assert.Greater(Rms(buffer), 0.05f, "A zero-gain drone must resume when gain increases.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void GainChangeAfterPercussiveNote_DoesNotStartADrone()
        {
            var go = new GameObject("PercussiveToneTest");
            try
            {
                var synth = go.AddComponent<NativeDspSynthesizer>();
                synth.ReverbMix = 0f;
                synth.AttackSeconds = 0.001f;
                synth.DecaySeconds = 0.001f;
                synth.SustainLevel = 0f;
                synth.SetNote(1, 69);
                var buffer = new float[4096];
                for (int block = 0; block < 4; block++) synth.SynthesizeBlock(buffer, 1, 48000);
                synth.SetParameter(16, 0.5f);
                synth.SynthesizeBlock(buffer, 1, 48000);
                Assert.Less(Rms(buffer), 0.0001f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static float Rms(float[] buffer)
        {
            float sum = 0f;
            foreach (float sample in buffer) sum += sample * sample;
            return Mathf.Sqrt(sum / buffer.Length);
        }

        [Test]
        public void DualCcIdCompatibility_AcceptsBothReadmeAndLegacyIds()
        {
            var go = new GameObject("SynthTest");
            try
            {
                var synth = go.AddComponent<NativeDspSynthesizer>();

                // CC 0 and CC 1 both set Waveform
                synth.SetParameter(0, 2f);
                Assert.AreEqual(SynthWaveformType.Sawtooth, synth.Waveform);
                synth.SetParameter(1, 1f);
                Assert.AreEqual(SynthWaveformType.Square, synth.Waveform);

                // Attack: CC 82 (Python/README) and CC 74 (Legacy C# Inspector default)
                synth.SetParameter(82, 0.12f);
                Assert.AreEqual(0.12f, synth.AttackSeconds, 0.0001f);
                synth.SetParameter(74, 0.25f);
                Assert.AreEqual(0.25f, synth.AttackSeconds, 0.0001f);

                // Decay: CC 83 and CC 71
                synth.SetParameter(83, 0.18f);
                Assert.AreEqual(0.18f, synth.DecaySeconds, 0.0001f);
                synth.SetParameter(71, 0.30f);
                Assert.AreEqual(0.30f, synth.DecaySeconds, 0.0001f);

                // Sustain: CC 85 and CC 72
                synth.SetParameter(85, 0.65f);
                Assert.AreEqual(0.65f, synth.SustainLevel, 0.0001f);
                synth.SetParameter(72, 0.45f);
                Assert.AreEqual(0.45f, synth.SustainLevel, 0.0001f);

                // Release: CC 17 and CC 73
                synth.SetParameter(17, 0.40f);
                Assert.AreEqual(0.40f, synth.ReleaseSeconds, 0.0001f);
                synth.SetParameter(73, 0.55f);
                Assert.AreEqual(0.55f, synth.ReleaseSeconds, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SynthesizeBlock_ProducesCleanNonZeroAudioOnNoteOn_AcrossAllWaveforms()
        {
            var go = new GameObject("SynthDspTest");
            try
            {
                var synth = go.AddComponent<NativeDspSynthesizer>();
                synth.AttackSeconds = 0.002f;
                synth.DecaySeconds = 0.05f;
                synth.SustainLevel = 0.8f;
                synth.Gain = 0.5f;

                float[] buffer = new float[1024 * 2];

                for (int w = 0; w <= 3; w++)
                {
                    synth.Waveform = (SynthWaveformType)w;
                    synth.SetNote(1, 69, 110); // A4 = 440 Hz
                    synth.SynthesizeBlock(buffer, 2, 48000);

                    float sumSq = 0f;
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        Assert.IsFalse(float.IsNaN(buffer[i]), $"NaN sample detected on waveform {w}");
                        Assert.IsFalse(float.IsInfinity(buffer[i]), $"Inf sample detected on waveform {w}");
                        sumSq += buffer[i] * buffer[i];
                    }

                    float rms = Mathf.Sqrt(sumSq / buffer.Length);
                    Assert.Greater(rms, 0.01f, $"Expected audible RMS on NoteOn for waveform {(SynthWaveformType)w}");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
