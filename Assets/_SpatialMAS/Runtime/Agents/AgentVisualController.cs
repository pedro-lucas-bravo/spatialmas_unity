using UnityEngine;

namespace SpatialMAS.Agents
{
    /// <summary>
    /// Updates agent colors and hover/grab highlights using a shared MaterialPropertyBlock
    /// to avoid per-agent Material cloning and maintain GPU instancing batching on Quest 3, Vision Pro & Desktop.
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
        private bool _isHighlighted;

        public Color CurrentColor => _currentColor;
        public bool IsHighlighted => _isHighlighted;

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
            ApplyPropertyBlock();
        }

        public void SetHighlight(bool highlighted)
        {
            EnsureInitialized();
            _isHighlighted = highlighted;
            ApplyPropertyBlock();
        }

        private void ApplyPropertyBlock()
        {
            if (_renderer == null) return;
            _renderer.GetPropertyBlock(_mpb);

            Color displayColor = _isHighlighted
                ? Color.Lerp(_currentColor, Color.white, 0.48f)
                : _currentColor;

            _mpb.SetColor(s_BaseColorId, displayColor);
            _mpb.SetColor(s_ColorId, displayColor);

            float boost = _isHighlighted ? 1.25f : 0.35f;
            Color emissive = new Color(
                displayColor.r * boost,
                displayColor.g * boost,
                displayColor.b * boost,
                1.0f);
            _mpb.SetColor(s_EmissionColorId, emissive);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
