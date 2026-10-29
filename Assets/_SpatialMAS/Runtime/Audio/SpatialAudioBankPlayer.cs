using UnityEngine;

namespace SpatialMAS.Audio
{
    /// <summary>
    /// Handles the /agents/audio/bank OSC endpoint.
    /// Lazily creates its secondary 3D AudioSource and procedural Crystal Bell (index 0) / Percussive Click (index 1)
    /// AudioClips only when a bank clip is triggered, keeping agent instantiation zero-overhead.
    /// </summary>
    public class SpatialAudioBankPlayer : MonoBehaviour
    {
        [SerializeField] private AudioClip[] _soundBankClips;
        private AudioSource _bankSource;

        private static AudioClip s_ProceduralBellClip;
        private static AudioClip s_ProceduralClickClip;

        private void EnsureBankSourceAndClips()
        {
            if (_bankSource == null)
            {
                Transform existing = transform.Find("AudioBankSource");
                GameObject bankGo = existing != null ? existing.gameObject : new GameObject("AudioBankSource");
                bankGo.transform.SetParent(transform, false);
                _bankSource = bankGo.GetComponent<AudioSource>();
                if (_bankSource == null) _bankSource = bankGo.AddComponent<AudioSource>();
                _bankSource.playOnAwake = false;
                _bankSource.spatialBlend = 1.0f;
                _bankSource.rolloffMode = AudioRolloffMode.Logarithmic;
                _bankSource.minDistance = 0.25f;
                _bankSource.maxDistance = 15.0f;
            }

            int sr = 44100;
            if (s_ProceduralBellClip == null)
            {
                int samples = sr / 2; // 500 ms harmonic bell
                float[] data = new float[samples];
                for (int i = 0; i < samples; i++)
                {
                    float t = (float)i / sr;
                    float env = Mathf.Exp(-t * 8.0f);
                    float s = 0.6f * Mathf.Sin(2f * Mathf.PI * 523.25f * t) +
                              0.3f * Mathf.Sin(2f * Mathf.PI * 1046.5f * t) +
                              0.1f * Mathf.Sin(2f * Mathf.PI * 1567.98f * t);
                    data[i] = s * env * 0.5f;
                }
                s_ProceduralBellClip = AudioClip.Create("ProceduralBell_0", samples, 1, sr, false);
                s_ProceduralBellClip.SetData(data, 0);
            }

            if (s_ProceduralClickClip == null)
            {
                int samples = sr / 10; // 100 ms woody marimba click
                float[] data = new float[samples];
                for (int i = 0; i < samples; i++)
                {
                    float t = (float)i / sr;
                    float env = Mathf.Exp(-t * 45.0f);
                    float pitchDrop = 880f * Mathf.Exp(-t * 15.0f) + 220f;
                    data[i] = Mathf.Sin(2f * Mathf.PI * pitchDrop * t) * env * 0.7f;
                }
                s_ProceduralClickClip = AudioClip.Create("ProceduralClick_1", samples, 1, sr, false);
                s_ProceduralClickClip.SetData(data, 0);
            }

            if (_soundBankClips == null || _soundBankClips.Length < 2)
            {
                _soundBankClips = new[] { s_ProceduralBellClip, s_ProceduralClickClip };
            }
        }

        public void TriggerBankClip(int clipIndex, int playAction, float gain)
        {
            if (playAction == 0)
            {
                if (_bankSource != null) _bankSource.Stop();
                return;
            }

            EnsureBankSourceAndClips();

            int idx = Mathf.Clamp(clipIndex, 0, _soundBankClips.Length - 1);
            AudioClip clip = _soundBankClips[idx] != null ? _soundBankClips[idx] : s_ProceduralBellClip;
            _bankSource.volume = Mathf.Clamp01(gain);
            _bankSource.PlayOneShot(clip, Mathf.Clamp01(gain));
        }
    }
}
