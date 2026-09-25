namespace ProjectT.UGUI
{
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
        private new string controlPath;

        private readonly HashSet<(int pointerId, PointerEventData.InputButton button)> acceptedPresses = new HashSet<(int, PointerEventData.InputButton)>();
        private readonly HashSet<(int pointerId, PointerEventData.InputButton button)> clickEligiblePresses = new HashSet<(int, PointerEventData.InputButton)>();
        private bool inputAllowed = true;
        private object inputGeneration;
        private System.Action<Vector2> inputSender;

        protected override string controlPathInternal { get => controlPath; set => controlPath = value; }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!inputAllowed)
                return;

            var press = (eventData.pointerId, eventData.button);
            clickEligiblePresses.Clear();
            acceptedPresses.Add(press);

            if (OnPointerDownHandler != null)
                OnPointerDownHandler.Invoke(eventData, GetInputSenderInternal());
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!inputAllowed || !acceptedPresses.Contains((eventData.pointerId, eventData.button)))
                return;

            if (OnDragHandler != null)
                OnDragHandler.Invoke(eventData, GetInputSenderInternal());
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            var press = (eventData.pointerId, eventData.button);
            if (!inputAllowed || !acceptedPresses.Remove(press))
                return;

            if (eventData.eligibleForClick)
                clickEligiblePresses.Add(press);

            if (OnPointerUpHandler != null)
                OnPointerUpHandler.Invoke(eventData, GetInputSenderInternal());
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            var wasAccepted = clickEligiblePresses.Remove((eventData.pointerId, eventData.button));
            if (!inputAllowed || !eventData.eligibleForClick || !wasAccepted)
                return;

            if (OnClickHandler != null)
                OnClickHandler.Invoke(eventData, GetInputSenderInternal());
        }

        internal void SetInputAllowedInternal(bool allowed)
        {
            if (inputAllowed == allowed)
                return;

            inputAllowed = allowed;
            if (!allowed)
                CancelInputInternal();
        }

        protected override void OnDisable()
        {
            CancelInputInternal();
            base.OnDisable();
        }

        private System.Action<Vector2> GetInputSenderInternal()
        {
            if (inputSender != null)
                return inputSender;

            var generation = new object();
            inputGeneration = generation;
            inputSender = value =>
            {
                if (inputAllowed && ReferenceEquals(inputGeneration, generation))
                    SendValueToControl(value);
            };
            return inputSender;
        }

        private void CancelInputInternal()
        {
            inputGeneration = null;
            inputSender = null;
            acceptedPresses.Clear();
            clickEligiblePresses.Clear();
            SentDefaultValueToControl();
        }
    }
}
