using System;
using UnityEngine;

namespace SpatialMAS.Core
{
    [Serializable]
    public sealed class ResearcherPanelPalette
    {
        [Header("Surfaces")]
        public Color shell = new Color32(20, 28, 38, 250);
        [Tooltip("Shared color for the content surface and the joined active tab.")]
        public Color inset = new Color32(30, 42, 56, 255);
        public Color inactiveTab = new Color32(25, 35, 47, 255);
        public Color button = new Color32(36, 50, 66, 255);

        [Header("Button state tints")]
        public Color buttonHoverTint = new Color32(245, 245, 245, 255);
        public Color buttonPressedTint = new Color32(200, 200, 200, 255);
        public Color buttonSelectedTint = new Color32(245, 245, 245, 255);
        public Color buttonDisabledTint = new Color32(200, 200, 200, 128);

        [Header("Text and indicators")]
        public Color primaryText = new Color32(244, 246, 248, 255);
        public Color secondaryText = new Color32(184, 196, 208, 255);
        public Color accent = new Color32(104, 217, 230, 255);
        public Color tabMarker = new Color32(104, 217, 230, 255);
        public Color selectionMarker = new Color32(104, 217, 230, 255);
        public Color moveMarker = new Color32(104, 240, 255, 255);
        public Color running = new Color32(244, 246, 248, 255);
        public Color paused = new Color32(255, 198, 109, 255);
        public Color pending = new Color32(255, 198, 109, 255);
        public Color recording = new Color32(251, 110, 118, 255);
        public Color notRecording = new Color32(184, 196, 208, 255);
        public Color confirmation = new Color32(255, 198, 109, 255);

        [Header("Interaction feedback")]
        public Color hoverOutline = new Color32(104, 217, 230, 255);
        public Color dragOutline = new Color32(104, 217, 230, 155);
        [Tooltip("Tint and alpha multiplier for the hand ray while it hits the panel.")]
        public Color panelRay = Color.white;
        public Color enabledEndpoint = new Color32(244, 246, 248, 255);
        public Color disabledEndpoint = new Color32(184, 196, 208, 255);

        internal ResearcherPanelPalette Copy() => (ResearcherPanelPalette)MemberwiseClone();

        internal bool Matches(ResearcherPanelPalette other)
        {
            return other != null && shell.Equals(other.shell) && inset.Equals(other.inset) &&
                inactiveTab.Equals(other.inactiveTab) && button.Equals(other.button) &&
                buttonHoverTint.Equals(other.buttonHoverTint) && buttonPressedTint.Equals(other.buttonPressedTint) &&
                buttonSelectedTint.Equals(other.buttonSelectedTint) && buttonDisabledTint.Equals(other.buttonDisabledTint) &&
                primaryText.Equals(other.primaryText) && secondaryText.Equals(other.secondaryText) && accent.Equals(other.accent) &&
                tabMarker.Equals(other.tabMarker) && selectionMarker.Equals(other.selectionMarker) &&
                running.Equals(other.running) && paused.Equals(other.paused) && pending.Equals(other.pending) &&
                recording.Equals(other.recording) && notRecording.Equals(other.notRecording) &&
                confirmation.Equals(other.confirmation) && hoverOutline.Equals(other.hoverOutline) &&
                dragOutline.Equals(other.dragOutline) && panelRay.Equals(other.panelRay) &&
                enabledEndpoint.Equals(other.enabledEndpoint) && disabledEndpoint.Equals(other.disabledEndpoint);
        }
    }
}
