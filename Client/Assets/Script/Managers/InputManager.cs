using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectT
{
    [Flags]
    public enum eInputEvent
    {
        None = 0,
        Start = 1 << 0,
        Performed = 1 << 1,
        Cancel = 1 << 2
    }

    public class InputManager : ManagerBase
    {
        private PlayerInput playerInput;

        public string ControllScheme
        {
            get
            {
                if (playerInput != null)
                    return playerInput.defaultControlScheme;

                return string.Empty;
            }
        }

        #region ManagerBase
        public override void OnAppEnd()
        {
        }

        public override void OnAppFocuse(bool focused)
        {
        }

        public override void OnAppPause(bool paused)
        {
        }

        public override void OnAppStart()
        {
        }

        public override void OnEnter()
        {
        }

        public override void OnFixedUpdate(float dt)
        {
        }

        public override void OnLateUpdate()
        {
        }

        public override void OnLeave()
        {
        }

        public override void OnUpdate(float dt)
        {
        }
        #endregion

        public void RegisterInput<T>(T actor, string schemeType) where T : UnityEngine.Component
        {
            if (actor == null)
                return;

            playerInput = actor.GetComponent<PlayerInput>();

            playerInput.defaultControlScheme = schemeType;
        }

        public void AddEvent(string eventName, System.Action<InputAction.CallbackContext> callback, eInputEvent eventType)
        {
            if (callback == null)
                return;

            if (eventType.HasFlag(eInputEvent.Start))
                playerInput.actions[eventName].started += callback;
            if (eventType.HasFlag(eInputEvent.Performed))
                playerInput.actions[eventName].performed += callback;
            if (eventType.HasFlag(eInputEvent.Cancel))
                playerInput.actions[eventName].canceled += callback;
        }

        public void RemoveEvent(string eventName, System.Action<InputAction.CallbackContext> callback)
        {
            if (callback == null)
                return;
            playerInput.actions[eventName].performed -= callback;
        }
    }
}
