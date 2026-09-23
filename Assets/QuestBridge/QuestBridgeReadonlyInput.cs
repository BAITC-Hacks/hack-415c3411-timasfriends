using System;
using TMPro;
using UnityEngine.EventSystems;

namespace QuestBridge
{
    /// <summary>TMP selection for Game View/desktop; wheel scrolling returns to the enclosing list.</summary>
    public sealed class QuestBridgeReadonlyInput : TMP_InputField
    {
        public Action closeRequested;

        public override void OnDeselect(BaseEventData eventData)
        {
            base.OnDeselect(eventData);
            closeRequested?.Invoke();
        }

        public override void OnUpdateSelected(BaseEventData eventData)
        {
            bool wasFocused = isFocused;
            base.OnUpdateSelected(eventData);
            if (wasFocused && !isFocused) closeRequested?.Invoke();
        }

        public override void OnScroll(PointerEventData eventData)
        {
            var parentScroll = transform.parent ? transform.parent.GetComponentInParent<UnityEngine.UI.ScrollRect>() : null;
            closeRequested?.Invoke();
            if (parentScroll && parentScroll.isActiveAndEnabled) parentScroll.OnScroll(eventData);
        }
    }
}
