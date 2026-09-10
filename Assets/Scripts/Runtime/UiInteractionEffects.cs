using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ChunithmMobile.Runtime
{
    public sealed class UiPressAnimation : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        private Vector3 target = Vector3.one;
        private bool hovered;

        private void Update()
        {
            transform.localScale = Vector3.Lerp(transform.localScale, target, 1f - Mathf.Exp(-24f * Time.unscaledDeltaTime));
        }

        public void OnPointerDown(PointerEventData eventData) { target = new Vector3(.965f, .965f, 1f); }
        public void OnPointerUp(PointerEventData eventData) { target = hovered ? new Vector3(1.008f, 1.008f, 1f) : Vector3.one; }
        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            target = new Vector3(1.008f, 1.008f, 1f);
        }
        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            target = Vector3.one;
        }
        private void OnDisable() { hovered = false; target = Vector3.one; transform.localScale = Vector3.one; }
    }

    public sealed class UiDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public Action<Vector2> Changed;
        public bool LockX;
        public bool LockY;

        public void OnBeginDrag(PointerEventData eventData) { Apply(eventData); }
        public void OnDrag(PointerEventData eventData) { Apply(eventData); }

        private void Apply(PointerEventData eventData)
        {
            RectTransform parent = transform.parent as RectTransform;
            if (parent == null) return;
            Camera camera = eventData.pressEventCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, camera, out Vector2 local)) return;
            Rect rect = parent.rect;
            Vector2 normalized = new Vector2(
                Mathf.InverseLerp(rect.xMin, rect.xMax, local.x),
                Mathf.InverseLerp(rect.yMin, rect.yMax, local.y));
            RectTransform own = transform as RectTransform;
            if (LockX && own != null) normalized.x = own.anchorMin.x;
            if (LockY && own != null) normalized.y = own.anchorMin.y;
            Changed?.Invoke(normalized);
        }
    }

    public sealed class SettingsPageSwipe : MonoBehaviour, IBeginDragHandler, IEndDragHandler
    {
        public UnityEngine.UI.ScrollRect Scroll;
        public Action Next;
        public Action Previous;
        private float startY;
        private bool wasAtBottom;
        private bool wasAtTop;

        public void OnBeginDrag(PointerEventData eventData)
        {
            startY = eventData.position.y;
            // Reaching an edge consumes the first swipe. A second swipe that
            // starts while already at that edge changes the settings section.
            wasAtBottom = Scroll != null && Scroll.verticalNormalizedPosition <= .025f;
            wasAtTop = Scroll != null && Scroll.verticalNormalizedPosition >= .975f;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (Scroll == null) return;
            float distance = eventData.position.y - startY;
            if (distance > 90f && wasAtBottom && Scroll.verticalNormalizedPosition <= .025f) Next?.Invoke();
            else if (distance < -90f && wasAtTop && Scroll.verticalNormalizedPosition >= .975f) Previous?.Invoke();
        }
    }

    public sealed class DelayTestTapTarget : MonoBehaviour, IPointerDownHandler
    {
        public Action<double> Tapped;
        public void OnPointerDown(PointerEventData eventData) { Tapped?.Invoke(AudioSettings.dspTime); }
    }

    public sealed class UiResizeHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public Action<float> Changed;
        private Vector2 previous;

        public void OnBeginDrag(PointerEventData eventData) { previous = eventData.position; }

        public void OnDrag(PointerEventData eventData)
        {
            Vector2 delta = eventData.position - previous;
            previous = eventData.position;
            Changed?.Invoke((delta.x + delta.y) * .18f);
        }
    }

    /// <summary>Invokes a step immediately, then repeats while held.</summary>
    public sealed class UiHoldRepeatButton : MonoBehaviour, IPointerDownHandler,
        IPointerUpHandler, IPointerExitHandler
    {
        public Action Pressed;
        [Range(.15f, 1f)] public float InitialDelay = .35f;
        [Range(.03f, .25f)] public float RepeatInterval = .08f;
        private Coroutine repeat;

        public void OnPointerDown(PointerEventData eventData)
        {
            Pressed?.Invoke();
            StopRepeating();
            repeat = StartCoroutine(RepeatWhileHeld());
        }

        public void OnPointerUp(PointerEventData eventData) { StopRepeating(); }
        public void OnPointerExit(PointerEventData eventData) { StopRepeating(); }

        private System.Collections.IEnumerator RepeatWhileHeld()
        {
            yield return new WaitForSecondsRealtime(InitialDelay);
            while (true) {
                Pressed?.Invoke();
                yield return new WaitForSecondsRealtime(RepeatInterval);
            }
        }

        private void StopRepeating()
        {
            if (repeat == null) return;
            StopCoroutine(repeat);
            repeat = null;
        }

        private void OnDisable() { StopRepeating(); }
    }
}
