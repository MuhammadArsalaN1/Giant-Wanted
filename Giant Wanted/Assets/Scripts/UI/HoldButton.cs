using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace GiantWanted
{
    /// <summary>
    /// A button that reports press and release instead of clicks, so it can drive
    /// full-auto fire. Also shrinks slightly while held for a bit of tactile feedback.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [System.Serializable] public class BoolEvent : UnityEvent<bool> { }

        public BoolEvent onHeldChanged = new BoolEvent();

        [Header("Feedback")]
        public float pressedScale = 0.92f;
        public float scaleSpeed = 14f;

        public bool IsHeld { get; private set; }

        Vector3 _baseScale;

        void Awake()
        {
            _baseScale = transform.localScale;
        }

        void OnDisable()
        {
            Set(false);
            transform.localScale = _baseScale;
        }

        public void OnPointerDown(PointerEventData eventData) => Set(true);
        public void OnPointerUp(PointerEventData eventData) => Set(false);
        public void OnPointerExit(PointerEventData eventData) => Set(false);

        void Set(bool held)
        {
            if (IsHeld == held) return;
            IsHeld = held;
            onHeldChanged.Invoke(held);
        }

        void Update()
        {
            Vector3 wanted = IsHeld ? _baseScale * pressedScale : _baseScale;
            transform.localScale = Vector3.Lerp(transform.localScale, wanted, scaleSpeed * Time.unscaledDeltaTime);
        }
    }
}
