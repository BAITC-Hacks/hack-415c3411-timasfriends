using UnityEngine;
using UnityEngine.EventSystems;

namespace QuestBridge
{
    /// <summary>Small unscaled UI feedback; independent of server polling and layout.</summary>
    public sealed class QuestBridgeMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public bool scaleOnHover = true;
        public float hoverScale = 1.018f;
        public UnityEngine.UI.Image surface;
        public Color resting = Color.white, hovered = new Color(.96f,.98f,1);
        bool over, down;
        float pop;
        public void Pulse(float amount = .045f) { pop = amount; }
        public void OnPointerEnter(PointerEventData e) { over = true; }
        public void OnPointerExit(PointerEventData e) { over = false; down = false; }
        public void OnPointerDown(PointerEventData e) { down = true; }
        public void OnPointerUp(PointerEventData e) { down = false; }
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            pop = Mathf.Lerp(pop,0,1-Mathf.Exp(-8*dt));
            float target = (down ? .982f : over && scaleOnHover ? hoverScale : 1) + pop;
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one*target,1-Mathf.Exp(-18*dt));
            if(surface) surface.color=Color.Lerp(surface.color,over?hovered:resting,1-Mathf.Exp(-12*dt));
        }
    }
}
