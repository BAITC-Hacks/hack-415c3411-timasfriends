using TMPro;
using UnityEngine;

namespace QuestBridge
{
    public static class QuestBridgeComposer
    {
        // Call once after creating the input's text, placeholder and masked viewport.
        public static void Configure(TMP_InputField field, TMP_Text typed, TMP_Text placeholder, RectTransform viewport)
        {
            // AddComponent on an active object runs OnEnable before these references exist.
            // Enable again after wiring so TMP creates its caret/selection renderer and
            // registers text geometry and viewport mask callbacks.
            bool wasEnabled = field.enabled;
            field.enabled = false;
            field.textViewport = viewport;
            field.textComponent = typed;
            field.placeholder = placeholder;
            field.lineType = TMP_InputField.LineType.MultiLineNewline;
            field.richText = false;
            field.onFocusSelectAll = false;
            field.restoreOriginalTextOnEscape = false;
            field.customCaretColor = true;
            field.caretColor = new Color32(23, 33, 42, 255);
            field.caretWidth = 2;
            field.caretBlinkRate = 1.6f;
            field.selectionColor = new Color32(65, 145, 230, 105);

            // TMP scrolls the text to keep the caret in the viewport. Ellipsis would
            // discard the offscreen character geometry needed for editing/selection.
            typed.overflowMode = TextOverflowModes.Overflow;
            typed.textWrappingMode = TextWrappingModes.Normal;
            typed.alignment = TextAlignmentOptions.TopLeft;
            typed.enableAutoSizing = false;
            typed.raycastTarget = false;
            placeholder.overflowMode = TextOverflowModes.Overflow;
            placeholder.textWrappingMode = TextWrappingModes.Normal;
            placeholder.alignment = TextAlignmentOptions.TopLeft;
            placeholder.enableAutoSizing = false;
            placeholder.raycastTarget = false;

            var background = field.GetComponent<UnityEngine.UI.Image>();
            if (background != null)
            {
                // Let the input itself receive clicks and selection drags over its full area.
                background.raycastTarget = true;
                field.targetGraphic = background;
                field.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
                var colors = field.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color32(247, 250, 255, 255);
                colors.pressedColor = new Color32(232, 242, 255, 255);
                colors.selectedColor = new Color32(238, 246, 255, 255);
                colors.disabledColor = new Color32(230, 234, 240, 255);
                colors.colorMultiplier = 1;
                colors.fadeDuration = .1f;
                field.colors = colors;
            }

            field.enabled = wasEnabled;
            QuestBridgeBrowserText.AttachInput(field);
        }
    }
}
