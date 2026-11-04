using System;
using SpatialMAS.XR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SpatialMAS.UI
{
    /// <summary>Local panel access and placement only; never changes experiment state.</summary>
    public sealed class XRResearcherPanelLifecycle : MonoBehaviour
    {
        private Transform _panel;
        private CanvasGroup _group;
        private Action _recall;
        private bool _researcherMode = true, _visible = true, _headsetActive = true;
        private bool _launcherContact, _waitingForRelease;
        private int _openedFrame;
        private int _neutralFrame = -1;
        private float _controllerRecoveryTime;
        private bool _controllerRecoveryLatched;
        private XRHandPinchHysteresis[] _hands = Array.Empty<XRHandPinchHysteresis>();
        private NearFarInteractor[] _interactors = Array.Empty<NearFarInteractor>();
        private float _refreshTime;

        public bool ResearcherMode
        {
            get => _researcherMode;
            set
            {
                if (_researcherMode == value) return;
                _researcherMode = value;
                if (!value) Hide();
                else { _waitingForRelease = true; _openedFrame = Time.frameCount; _neutralFrame = -1; ApplyVisibility(); }
            }
        }
        public bool Visible => _visible && _researcherMode;
        public bool HeadsetActive => _headsetActive;
        public bool InputReady => Visible && _headsetActive && !_waitingForRelease;
        public Transform Panel => _panel;
        public event Action<bool> VisibilityChanged;
        public event Action Hidden;

        public void Initialize(Transform panel, Action recall)
        {
            _panel = panel;
            _recall = recall;
            _group = panel.GetComponent<CanvasGroup>();
            if (_group == null) _group = panel.gameObject.AddComponent<CanvasGroup>();
            BuildMoveHandle();
            RefreshInputs();
            ApplyVisibility();
        }

        public void SetHeadsetActive(bool active)
        {
            if (_headsetActive == active) return;
            _headsetActive = active;
            if (!active) Hidden?.Invoke();
            _waitingForRelease = true;
            _openedFrame = Time.frameCount;
            _neutralFrame = -1;
            RefreshInputs();
            ApplyVisibility();
        }

        public void Show()
        {
            if (!_researcherMode) return;
            _visible = true;
            _waitingForRelease = true;
            _openedFrame = Time.frameCount;
            _neutralFrame = -1;
            RefreshInputs();
            ApplyVisibility();
            VisibilityChanged?.Invoke(true);
        }

        public void Recall()
        {
            if (!_researcherMode) return;
            _recall?.Invoke();
            Show();
        }

        public void ToggleFromLauncher()
        {
            if (!_researcherMode) return;
            if (Visible) Hide();
            else Recall();
        }

        public void Hide()
        {
            _visible = false;
            _waitingForRelease = true;
            _neutralFrame = -1;
            _launcherContact = false;
            if (EventSystem.current != null && _panel != null && EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.transform.IsChildOf(_panel))
                EventSystem.current.SetSelectedGameObject(null);
            ApplyVisibility();
            VisibilityChanged?.Invoke(false);
            Hidden?.Invoke();
        }

        public void SetLauncherContact(bool touching) => _launcherContact = touching;

        /// <summary>Explicit local recovery, available even when participant mode hides every launcher.</summary>
        public void RecoverResearcherAccess()
        {
            ResearcherMode = true;
            Recall();
        }

        private void Update()
        {
            if (Time.unscaledTime >= _refreshTime) RefreshInputs();
            // F8 is a local researcher recovery key, not a remote identity/authentication mechanism.
            if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame) RecoverResearcherAccess();
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            bool menu = left.isValid && left.TryGetFeatureValue(UnityEngine.XR.CommonUsages.menuButton, out bool down) && down;
            if (!menu) { _controllerRecoveryTime = 0f; _controllerRecoveryLatched = false; }
            else if (!_controllerRecoveryLatched)
            {
                _controllerRecoveryTime += Time.unscaledDeltaTime;
                if (_controllerRecoveryTime >= 2f) { _controllerRecoveryLatched = true; RecoverResearcherAccess(); }
            }
            if (_waitingForRelease)
            {
                if (_launcherContact || AnyInputPressed()) _neutralFrame = -1;
                else if (_neutralFrame < 0) _neutralFrame = Time.frameCount;
                // Leave the canvas blocked for an entire neutral input frame, so XRI can
                // drain a cached pointer-up before any newly revealed button is interactive.
                else if (Time.frameCount > _openedFrame && Time.frameCount > _neutralFrame)
                {
                    _waitingForRelease = false;
                    ApplyVisibility();
                }
            }
        }

        private void OnDisable()
        {
            if (_panel != null) Hide();
        }

        private void RefreshInputs()
        {
            _hands = FindObjectsByType<XRHandPinchHysteresis>(FindObjectsSortMode.None);
            _interactors = FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
            _refreshTime = Time.unscaledTime + 1f;
        }

        private bool AnyInputPressed()
        {
            if (Mouse.current != null && Mouse.current.leftButton.isPressed) return true;
            foreach (var hand in _hands)
                if (hand != null && hand.isActiveAndEnabled && (hand.ReadUIIsPerformed() || hand.IsPinching)) return true;
            foreach (var interactor in _interactors)
                if (interactor != null && interactor.isActiveAndEnabled &&
                    (interactor.hasSelection || interactor.uiPressInput.ReadIsPerformed() || interactor.selectInput.ReadIsPerformed())) return true;
            return false;
        }

        private void ApplyVisibility()
        {
            if (_panel == null) return;
            bool active = Visible && _headsetActive;
            _panel.gameObject.SetActive(active);
            _group.interactable = active && !_waitingForRelease;
            _group.blocksRaycasts = active && !_waitingForRelease;
        }

        private void BuildMoveHandle()
        {
            if (_panel.Find("Move Handle") != null) return;
            var go = new GameObject("Move Handle", typeof(RectTransform), typeof(Image), typeof(XRPanelMoveHandle));
            go.transform.SetParent(_panel, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(18f, -904f);
            rect.sizeDelta = new Vector2(584f, 42f);
            go.GetComponent<Image>().color = new Color(0.18f, 0.4f, 0.44f, 0.95f);
            go.GetComponent<XRPanelMoveHandle>().Initialize(this);
            var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var label = textGo.GetComponent<Text>();
            label.text = "Hold and drag to move panel";
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 19;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            var material = Resources.Load<Material>("Mat_SpatialMAS_WorldUI");
            if (material != null) { go.GetComponent<Image>().material = material; label.material = material; }
        }
    }
}
