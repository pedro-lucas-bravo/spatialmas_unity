using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

namespace SpatialMAS.XR
{
    /// <summary>Native deliberate pinch for UI, separate from physical pinch/palm object grabs.</summary>
    public class XRHandUISelectReader : MonoBehaviour, IXRInputButtonReader
    {
        [SerializeField] private XRHandPinchHysteresis _source;
        public void Configure(XRHandPinchHysteresis source) => _source = source;
        public bool ReadIsPerformed() => _source != null && _source.ReadUIIsPerformed();
        public bool ReadWasPerformedThisFrame() => _source != null && _source.ReadUIWasPerformedThisFrame();
        public bool ReadWasCompletedThisFrame() => _source != null && _source.ReadUIWasCompletedThisFrame();
        public float ReadValue() => _source != null ? _source.ReadUIValue() : 0f;
        public bool TryReadValue(out float value) { value = ReadValue(); return _source != null; }
    }
}
