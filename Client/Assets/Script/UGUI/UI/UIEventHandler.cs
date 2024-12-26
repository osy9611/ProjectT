namespace ProjectT.UGUI
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.InputSystem.Layouts;
    using UnityEngine.InputSystem.OnScreen;

    public class UIEventHandler : OnScreenControl, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public System.Action<PointerEventData, System.Action<Vector2>> OnPointerDownHandler = null;
        public System.Action<PointerEventData, System.Action<Vector2>> OnDragHandler = null;
        public System.Action<PointerEventData, System.Action<Vector2>> OnPointerUpHandler = null;
        public System.Action<PointerEventData, System.Action<Vector2>> OnClickHandler = null;

        [SerializeField, InputControl(layout = "Vector2")]
        private string controlPath;

        protected override string controlPathInternal { get => controlPath; set => controlPath = value; }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (OnPointerDownHandler != null)
                OnPointerDownHandler.Invoke(eventData, SendValueToControl);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (OnDragHandler != null)
                OnDragHandler.Invoke(eventData, SendValueToControl);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (OnPointerUpHandler != null)
                OnPointerUpHandler.Invoke(eventData, SendValueToControl);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (OnClickHandler != null)
                OnClickHandler.Invoke(eventData, SendValueToControl);
        }
    }
}
