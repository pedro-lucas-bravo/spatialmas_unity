using UnityEngine;

namespace SpatialMAS.Agents
{
    /// <summary>
    /// Updates agent colors and emission highlights using a shared MaterialPropertyBlock
    /// to avoid per-agent Material cloning and maintain GPU instancing batching on Quest 3 & Vision Pro.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class AgentVisualController : MonoBehaviour
    {
        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorId = Shader.PropertyToID("_Color");
        private static readonly int s_EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private Renderer _renderer;
        private MaterialPropertyBlock _mpb;
        private Color _currentColor = new Color(0.15f, 0.85f, 1.0f, 1.0f);
        private float _emissionIntensity = 0.45f;

        public Color CurrentColor => _currentColor;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (_renderer == null) _renderer = GetComponent<Renderer>();
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
        }

        public void SetColor(Color color, float emissionBoost = 0.45f)
        {
            EnsureInitialized();
            _currentColor = color;
            _emissionIntensity = emissionBoost;
            ApplyPropertyBlock();
        }

        public void SetHighlight(bool highlighted)
        {
            EnsureInitialized();
            _emissionIntensity = highlighted ? 1.35f : 0.45f;
            ApplyPropertyBlock();
        }

        private void ApplyPropertyBlock()
        {
            if (_renderer == null) return;
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(s_BaseColorId, _currentColor);
            _mpb.SetColor(s_ColorId, _currentColor);
            Color emissive = new Color(
                _currentColor.r * _emissionIntensity,
                _currentColor.g * _emissionIntensity,
                _currentColor.b * _emissionIntensity,
                1.0f);
            _mpb.SetColor(s_EmissionColorId, emissive);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
