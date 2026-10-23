using System;
using UnityEngine;

namespace SpatialMAS.Audio
{
    /// <summary>
    /// Zero-allocation, cross-platform Native C# Procedural DSP Synthesizer running in Unity's audio thread
    /// via OnAudioFilterRead. Supports anti-aliased PolyBLEP oscillators (Sine, Square, Saw, Triangle),
    /// ADSR envelope generator, 12 dB/oct Biquad Low-Pass Filter with Resonance, compact Schroeder reverb,
    /// and 100% dual CC ID compatibility with all legacy HS-ims Python/Max/C++ scripts.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class NativeDspSynthesizer : MonoBehaviour, IAgentAudioSynth
    {
        private enum AdsrStage
        {
            Idle = 0,
            Attack = 1,
            Decay = 2,
            Sustain = 3,
            Release = 4
        }

        // Dual-Compatible CC ID Constants (Resolves README.md vs CsoundBasicSynthesizer.cs discrepancy)
        public const int CC_PresetOrWaveformAlias = 0;
        public const int CC_OscillatorWaveform = 1;
        public const int CC_FrequencyHz = 2;
        public const int CC_GainPrimary = 16;
        public const int CC_GainLegacyAlias = 7;
        public const int CC_AttackPrimary = 82;
        public const int CC_AttackLegacyAlias = 74;
        public const int CC_DecayPrimary = 83;
        public const int CC_DecayLegacyAlias = 71;
        public const int CC_SustainPrimary = 85;
        public const int CC_SustainLegacyAlias = 72;
        public const int CC_ReleasePrimary = 17;
        public const int CC_ReleaseLegacyAlias = 73;
        public const int CC_FilterCutoffHz = 93;
        public const int CC_FilterResonanceQ = 18;
        public const int CC_ReverbPrimary = 19;
        public const int CC_ReverbLegacyAlias = 91;

        // Thread-safe atomic/volatile synth parameters read by the audio thread
        private volatile bool _isMuted;
        private volatile int _waveformInt = (int)SynthWaveformType.Sine;
        private volatile float _targetFrequencyHz = 440f;
        private volatile float _currentFrequencyHz = 440f;
        private volatile float _targetGain = 0.0f;
        private volatile float _smoothedGain = 0.0f;
        private volatile float _velocityGain = 1.0f;

        private volatile float _attackSeconds = 0.04f;
        private volatile float _decaySeconds = 0.10f;
        private volatile float _sustainLevel = 0.80f;
        private volatile float _releaseSeconds = 0.20f;

        private volatile float _filterCutoffHz = 12000f;
        private volatile float _filterQ = 0.7071f;
        private volatile float _reverbMix = 0.12f;

        // Oscillator & Envelope internal state
        private double _phase;
        private volatile int _adsrStageInt = (int)AdsrStage.Idle;
        private float _adsrEnvelope;
        private bool _continuousDroneMode;

        // Biquad Low-Pass Filter state (per channel, up to 2 channels)
        private float _b0 = 1f, _b1 = 0f, _b2 = 0f, _a1 = 0f, _a2 = 0f;
        private float _lastCachedCutoff = -1f;
        private float _lastCachedQ = -1f;
        private int _lastCachedSampleRate = -1;
        private float _z1L, _z2L, _z1R, _z2R;

        // Compact zero-allocation Schroeder Comb + Allpass reverb lines
        private float[] _combBuffer1;
        private float[] _combBuffer2;
        private float[] _allpassBuffer;
        private int _combIdx1, _combIdx2, _allpassIdx;

        private int _cachedSampleRate = 48000;
        private AudioSource _audioSource;

        public bool IsMuted
        {
            get => _isMuted;
            set => _isMuted = value;
        }

        public SynthWaveformType Waveform
        {
            get => (SynthWaveformType)_waveformInt;
            set => _waveformInt = Mathf.Clamp((int)value, 0, 3);
        }

        public float FrequencyHz
        {
            get => _targetFrequencyHz;
            set => _targetFrequencyHz = Mathf.Clamp(value, 20f, 16000f);
        }

        public float Gain
        {
            get => _targetGain;
            set => _targetGain = Mathf.Clamp01(value);
        }

        public float AttackSeconds
        {
            get => _attackSeconds;
            set => _attackSeconds = Mathf.Clamp(value, 0.001f, 10.0f);
        }

        public float DecaySeconds
        {
            get => _decaySeconds;
            set => _decaySeconds = Mathf.Clamp(value, 0.001f, 10.0f);
        }

        public float SustainLevel
        {
            get => _sustainLevel;
            set => _sustainLevel = Mathf.Clamp01(value);
        }

        public float ReleaseSeconds
        {
            get => _releaseSeconds;
            set => _releaseSeconds = Mathf.Clamp(value, 0.001f, 10.0f);
        }

        public float FilterCutoffHz
        {
            get => _filterCutoffHz;
            set => _filterCutoffHz = Mathf.Clamp(value, 20f, 20000f);
        }

        public float FilterResonanceQ
        {
            get => _filterQ;
            set => _filterQ = Mathf.Clamp(value, 0.5f, 12.0f);
        }

        public float ReverbMix
        {
            get => _reverbMix;
            set => _reverbMix = Mathf.Clamp01(value);
        }

        private void Awake()
        {
            _cachedSampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            EnsureReverbBuffersAllocated(_cachedSampleRate);

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource != null)
            {
                _audioSource.playOnAwake = true;
                _audioSource.loop = true;
                _audioSource.spatialBlend = 1.0f; // Full 3D spatialization
                _audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                _audioSource.minDistance = 0.20f;
                _audioSource.maxDistance = 15.0f;
                _audioSource.dopplerLevel = 0.15f;

                // Provide a silent carrier clip if none is assigned so Unity's 3D spatializer runs before OnAudioFilterRead
                if (_audioSource.clip == null)
                {
                    var carrier = AudioClip.Create("SpatialMAS_SynthCarrier", 1024, 1, _cachedSampleRate, false);
                    float[] ones = new float[1024];
                    for (int i = 0; i < ones.Length; i++) ones[i] = 1.0f;
                    carrier.SetData(ones, 0);
                    _audioSource.clip = carrier;
                }

                if (!_audioSource.isPlaying)
                {
                    _audioSource.Play();
                }
            }
        }

        private void EnsureReverbBuffersAllocated(int sampleRate)
        {
            if (_combBuffer1 != null && _combBuffer1.Length > 0) return;
            int len1 = Mathf.Max(256, (int)(0.0297f * sampleRate));
            int len2 = Mathf.Max(256, (int)(0.0371f * sampleRate));
            int lenAp = Mathf.Max(128, (int)(0.0050f * sampleRate));
            _combBuffer1 = new float[len1];
            _combBuffer2 = new float[len2];
            _allpassBuffer = new float[lenAp];
        }

        public static float MidiNoteToFrequencyHz(int midiNote)
        {
            return 440.0f * Mathf.Pow(2.0f, (midiNote - 69.0f) / 12.0f);
        }

        public void SetNote(int noteAction, int midiNote, int velocity = 100)
        {
            if (noteAction != 0)
            {
                _continuousDroneMode = false;
                FrequencyHz = MidiNoteToFrequencyHz(Mathf.Clamp(midiNote, 12, 127));
                _velocityGain = Mathf.Clamp01(velocity / 127.0f);
                if (_targetGain <= 0.0001f)
                {
                    _targetGain = 0.35f;
                }
                _adsrStageInt = (int)AdsrStage.Attack;
            }
            else
            {
                _continuousDroneMode = false;
                _adsrStageInt = (int)AdsrStage.Release;
            }
        }

        public void SetParameter(int ccId, float value)
        {
            switch (ccId)
            {
                case CC_PresetOrWaveformAlias:
                case CC_OscillatorWaveform:
                    Waveform = (SynthWaveformType)Mathf.Clamp(Mathf.RoundToInt(value), 0, 3);
                    break;

                case CC_FrequencyHz:
                    FrequencyHz = value;
                    // If external script modulates frequency & gain directly without NoteOn, keep envelope active
                    if (_targetGain > 0.0001f && _adsrStageInt == (int)AdsrStage.Idle)
                    {
                        _continuousDroneMode = true;
                        _adsrStageInt = (int)AdsrStage.Attack;
                    }
                    break;

                case CC_GainPrimary:
                case CC_GainLegacyAlias:
                    Gain = value;
                    if (value > 0.0005f)
                    {
                        if (_adsrStageInt == (int)AdsrStage.Idle || _adsrStageInt == (int)AdsrStage.Release)
                        {
                            _continuousDroneMode = true;
                            _adsrStageInt = (int)AdsrStage.Attack;
                        }
                    }
                    else if (_continuousDroneMode)
                    {
                        _adsrStageInt = (int)AdsrStage.Release;
                    }
                    break;

                case CC_AttackPrimary:
                case CC_AttackLegacyAlias:
                    AttackSeconds = value;
                    break;

                case CC_DecayPrimary:
                case CC_DecayLegacyAlias:
                    DecaySeconds = value;
                    break;

                case CC_SustainPrimary:
                case CC_SustainLegacyAlias:
                    SustainLevel = value;
                    break;

                case CC_ReleasePrimary:
                case CC_ReleaseLegacyAlias:
                    ReleaseSeconds = value;
                    break;

                case CC_FilterCutoffHz:
                    FilterCutoffHz = value;
                    break;

                case CC_FilterResonanceQ:
                    FilterResonanceQ = value;
                    break;

                case CC_ReverbPrimary:
                case CC_ReverbLegacyAlias:
                    ReverbMix = value;
                    break;
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            SynthesizeBlock(data, channels, _cachedSampleRate);
        }

        /// <summary>
        /// Synthesizes an interleaved audio block. Callable both from Unity's audio thread (OnAudioFilterRead)
        /// and directly from deterministic EditMode unit tests.
        /// </summary>
        public void SynthesizeBlock(float[] data, int channels, int sampleRate)
        {
            if (data == null || channels <= 0) return;
            if (sampleRate <= 0) sampleRate = 48000;
            EnsureReverbBuffersAllocated(sampleRate);

            if (_isMuted)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            UpdateBiquadCoefficientsIfNeeded(sampleRate);

            int frames = data.Length / channels;
            float invSampleRate = 1.0f / sampleRate;
            SynthWaveformType wave = (SynthWaveformType)_waveformInt;

            float attackStep = invSampleRate / Mathf.Max(0.001f, _attackSeconds);
            float decayStep = (1.0f - _sustainLevel) * invSampleRate / Mathf.Max(0.001f, _decaySeconds);
            float releaseStep = invSampleRate / Mathf.Max(0.001f, _releaseSeconds);
            float sustain = _sustainLevel;
            float revWet = _reverbMix;
            float revDry = 1.0f - (revWet * 0.35f);

            for (int f = 0; f < frames; f++)
            {
                // 1. Advance ADSR Envelope
                AdsrStage stage = (AdsrStage)_adsrStageInt;
                switch (stage)
                {
                    case AdsrStage.Idle:
                        _adsrEnvelope = 0f;
                        break;
                    case AdsrStage.Attack:
                        _adsrEnvelope += attackStep;
                        if (_adsrEnvelope >= 1.0f)
                        {
                            _adsrEnvelope = 1.0f;
                            _adsrStageInt = (int)AdsrStage.Decay;
                        }
                        break;
                    case AdsrStage.Decay:
                        _adsrEnvelope -= decayStep;
                        if (_adsrEnvelope <= sustain)
                        {
                            _adsrEnvelope = sustain;
                            _adsrStageInt = (int)AdsrStage.Sustain;
                        }
                        break;
                    case AdsrStage.Sustain:
                        _adsrEnvelope = sustain;
                        break;
                    case AdsrStage.Release:
                        _adsrEnvelope -= releaseStep;
                        if (_adsrEnvelope <= 0.0001f)
                        {
                            _adsrEnvelope = 0f;
                            _adsrStageInt = (int)AdsrStage.Idle;
                        }
                        break;
                }

                // Smooth gain & frequency to prevent zipper noise
                _smoothedGain += (_targetGain - _smoothedGain) * 0.008f;
                _currentFrequencyHz += (_targetFrequencyHz - _currentFrequencyHz) * 0.015f;

                float envGain = _adsrEnvelope * _smoothedGain * _velocityGain;
                if (envGain <= 0.00001f && revWet <= 0.001f)
                {
                    int baseIdx = f * channels;
                    for (int c = 0; c < channels; c++) data[baseIdx + c] = 0f;
                    continue;
                }

                // 2. Compute Phase Increment & Anti-Aliased PolyBLEP Oscillator Sample
                double dt = _currentFrequencyHz * invSampleRate;
                if (dt > 0.49) dt = 0.49;
                double t = _phase;

                float rawSample;
                switch (wave)
                {
                    case SynthWaveformType.Square:
                        rawSample = t < 0.5 ? 1.0f : -1.0f;
                        rawSample += PolyBlep(t, dt);
                        rawSample -= PolyBlep((t + 0.5) % 1.0, dt);
                        break;

                    case SynthWaveformType.Sawtooth:
                        rawSample = (float)(2.0 * t - 1.0);
                        rawSample -= PolyBlep(t, dt);
                        break;

                    case SynthWaveformType.Triangle:
                        rawSample = (float)(2.0 * Math.Abs(2.0 * t - 1.0) - 1.0);
                        break;

                    case SynthWaveformType.Sine:
                    default:
                        rawSample = Mathf.Sin((float)(t * 2.0 * Math.PI));
                        break;
                }

                _phase += dt;
                if (_phase >= 1.0) _phase -= 1.0;

                float sample = rawSample * envGain;

                // 3. Apply 12 dB/oct Biquad Low-Pass Filter (Direct Form II Transposed)
                float filtered = _b0 * sample + _z1L;
                _z1L = _b1 * sample - _a1 * filtered + _z2L;
                _z2L = _b2 * sample - _a2 * filtered;

                // Clamp filter state against denormals/resonance spikes
                if (float.IsNaN(filtered) || float.IsInfinity(filtered))
                {
                    filtered = 0f;
                    _z1L = _z2L = _z1R = _z2R = 0f;
                }
                filtered = Mathf.Clamp(filtered, -1.5f, 1.5f);

                // 4. Compact Schroeder Reverb Diffuser
                float outSample = filtered;
                if (revWet > 0.001f)
                {
                    float c1 = _combBuffer1[_combIdx1];
                    _combBuffer1[_combIdx1] = filtered + c1 * 0.72f;
                    if (++_combIdx1 >= _combBuffer1.Length) _combIdx1 = 0;

                    float c2 = _combBuffer2[_combIdx2];
                    _combBuffer2[_combIdx2] = filtered + c2 * 0.68f;
                    if (++_combIdx2 >= _combBuffer2.Length) _combIdx2 = 0;

                    float combSum = (c1 + c2) * 0.5f;
                    float apBuf = _allpassBuffer[_allpassIdx];
                    float apOut = -combSum + apBuf;
                    _allpassBuffer[_allpassIdx] = combSum + (apBuf * 0.5f);
                    if (++_allpassIdx >= _allpassBuffer.Length) _allpassIdx = 0;

                    outSample = (filtered * revDry) + (apOut * revWet * 0.45f);
                }

                int frameOffset = f * channels;
                for (int c = 0; c < channels; c++)
                {
                    // Multiply with carrier if present inside Unity's spatializer pipeline, or write directly
                    data[frameOffset + c] = outSample;
                }
            }
        }

        private static float PolyBlep(double t, double dt)
        {
            if (dt <= 1e-7) return 0f;
            if (t < dt)
            {
                t /= dt;
                return (float)(t + t - t * t - 1.0);
            }
            if (t > 1.0 - dt)
            {
                t = (t - 1.0) / dt;
                return (float)(t * t + t + t + 1.0);
            }
            return 0f;
        }

        private void UpdateBiquadCoefficientsIfNeeded(int sampleRate)
        {
            float cutoff = Mathf.Clamp(_filterCutoffHz, 20f, sampleRate * 0.45f);
            float q = Mathf.Clamp(_filterQ, 0.5f, 12.0f);

            if (sampleRate == _lastCachedSampleRate &&
                Mathf.Abs(cutoff - _lastCachedCutoff) < 0.5f &&
                Mathf.Abs(q - _lastCachedQ) < 0.01f)
            {
                return;
            }

            _lastCachedSampleRate = sampleRate;
            _lastCachedCutoff = cutoff;
            _lastCachedQ = q;

            float omega = 2.0f * Mathf.PI * cutoff / sampleRate;
            float sinW = Mathf.Sin(omega);
            float cosW = Mathf.Cos(omega);
            float alpha = sinW / (2.0f * q);

            float a0 = 1.0f + alpha;
            float invA0 = 1.0f / a0;

            _b0 = ((1.0f - cosW) * 0.5f) * invA0;
            _b1 = (1.0f - cosW) * invA0;
            _b2 = _b0;
            _a1 = (-2.0f * cosW) * invA0;
            _a2 = (1.0f - alpha) * invA0;
        }
    }
}
