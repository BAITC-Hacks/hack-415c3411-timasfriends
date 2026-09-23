using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace QuestBridge
{
    /// <summary>Highlights a role panel while moving only its illustration.</summary>
    public sealed class QuestBridgeRoleHover : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        public UnityEngine.UI.Image surface, marker;
        public RectTransform illustration;
        public TMP_Text title, actionLabel;
        public Color resting = Color.white;
        public Color hovered = new Color32(234, 243, 255, 255);
        public Color titleRest = new Color32(23, 33, 42, 255);
        public Color titleHover = new Color32(35, 120, 204, 255);
        public Color actionRest = new Color32(83, 97, 112, 255);

        bool pointerOver, selected, pressed, initialized;
        Vector2 illustrationPosition;
        Vector3 illustrationScale;

        void Start()
        {
            // The panel factory assigns the references after AddComponent and before Start.
            if (illustration)
            {
                illustrationPosition = illustration.anchoredPosition;
                illustrationScale = illustration.localScale;
            }
            initialized = true;
            ApplyAppearance(1);
        }

        public void OnPointerEnter(PointerEventData eventData) => pointerOver = true;
        public void OnPointerExit(PointerEventData eventData) { pointerOver = false; pressed = false; }
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) pressed = true;
        }
        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) pressed = false;
        }
        public void OnSelect(BaseEventData eventData) => selected = true;
        public void OnDeselect(BaseEventData eventData) { selected = false; pressed = false; }

        void Update()
        {
            ApplyAppearance(1 - Mathf.Exp(-10 * Time.unscaledDeltaTime));
        }

        void ApplyAppearance(float amount)
        {
            bool highlighted = pointerOver || selected;
            if (surface) surface.color = Color.Lerp(surface.color, highlighted ? hovered : resting, amount);
            if (title) title.color = Color.Lerp(title.color, highlighted ? titleHover : titleRest, amount);
            if (actionLabel) actionLabel.color = Color.Lerp(actionLabel.color, highlighted ? titleHover : actionRest, amount);
            if (marker)
            {
                Color tint = marker.color;
                tint.a = Mathf.Lerp(tint.a, highlighted ? 1 : 0, amount);
                marker.color = tint;
            }
            if (initialized && illustration)
            {
                Vector2 position = illustrationPosition + (highlighted ? Vector2.up * 8 : Vector2.zero);
                Vector3 scale = illustrationScale * (pressed ? .97f : highlighted ? 1.035f : 1);
                illustration.anchoredPosition = Vector2.Lerp(illustration.anchoredPosition, position, amount);
                illustration.localScale = Vector3.Lerp(illustration.localScale, scale, amount);
            }
        }

        void OnDisable()
        {
            pointerOver = selected = pressed = false;
            ApplyAppearance(1);
        }
    }
}
