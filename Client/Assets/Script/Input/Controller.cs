using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ProjectT;
using System;
using UnityEngine.InputSystem.Users;

namespace ProjectT.Controller
{
    [Flags]
    public enum eInputEvent
    {
        None = 0,
        Start = 1 << 0,
        Performed = 1 << 1,
        Cancel = 1 << 2
    }

    public class Controller
    {
        protected InputActionAsset inputActionAsset;
        protected InputUser inputUser;
        protected InputActionRebindingExtensions.RebindingOperation rebindingOperation;

        private bool inputAllowed = true;
        private bool enableRequested;

        protected string actionKey = "Player";
        public string ActionKey { get => actionKey; }

        private Dictionary<string, InputAction> cachedActions = new Dictionary<string, InputAction>();

        virtual public void Init(InputActionAsset inputActionAsset, string actionKey = null, InputUser? inputUser = null)
        {
            if (inputActionAsset == null)
            {
                Global.Instance.LogWarning("[Controller] This InputActioAsset is null");
                return;
            }

            this.inputActionAsset = inputActionAsset;

            if (!string.IsNullOrEmpty(actionKey))
                this.actionKey = actionKey;

            if (inputUser.HasValue)
                this.inputUser = inputUser.Value;

            CacheInputActions();
            SetInputAllowed(inputAllowed);
        }

        private void CacheInputActions()
        {
            cachedActions.Clear();

            InputActionMap actionMap = inputActionAsset.FindActionMap(actionKey);
            if (actionMap == null)
            {
                Global.Instance.LogError($"[Controller] CacheInpuAction Fail InputActionMap is null actionKey : {actionKey}");
                return;
            }

            foreach (var action in actionMap.actions)
            {
                cachedActions.Add(action.name, action);
            }
        }

        virtual public void Enable()
        {
            if (inputActionAsset == null)
                return;

            enableRequested = true;
            if (inputAllowed)
                inputActionAsset.FindActionMap(actionKey)?.Enable();
        }

        virtual public void Disable()
        {
            if (inputActionAsset == null)
                return;

            enableRequested = false;
            inputActionAsset.FindActionMap(actionKey)?.Disable();
        }

        internal void SetInputAllowed(bool allowed)
        {
            inputAllowed = allowed;
            var map = inputActionAsset?.FindActionMap(actionKey);
            if (allowed && enableRequested)
                map?.Enable();
            else
                map?.Disable();
        }

        public virtual void Release()
        {
            Disable();
            rebindingOperation?.Dispose();
            rebindingOperation = null;
            cachedActions.Clear();
            inputActionAsset = null;
        }

        public void AddEvent(string actionName, System.Action<InputAction.CallbackContext> callback, eInputEvent eventType)
        {
            if (callback == null)
            {
                Global.Instance.LogWarning($"[Controller] AddEvent Fail Callback is null");
                return;
            }

            if (cachedActions.TryGetValue(actionName, out var inputAction))
            {
                if (eventType.HasFlag(eInputEvent.Start))
                    inputAction.started += callback;
                if (eventType.HasFlag(eInputEvent.Performed))
                    inputAction.performed += callback;
                if (eventType.HasFlag(eInputEvent.Cancel))
                    inputAction.canceled += callback;
            }
        }

        public void RemoveEvent(string actionName, System.Action<InputAction.CallbackContext> callback, eInputEvent eventType)
        {
            if (callback == null)
            {
                Global.Instance.LogWarning($"[Controller] RemoveEvent Fail Callback is null");
                return;
            }

            if (cachedActions.TryGetValue(actionName, out var inputAction))
            {
                if (eventType.HasFlag(eInputEvent.Start))
                    inputAction.started -= callback;
                if (eventType.HasFlag(eInputEvent.Performed))
                    inputAction.performed -= callback;
                if (eventType.HasFlag(eInputEvent.Cancel))
                    inputAction.canceled -= callback;
            }
        }

        virtual public void SwitchActionMap(string actionKey)
        {
            Disable();
            this.actionKey = actionKey;
            Enable();
            CacheInputActions();
        }

        public bool IsPressed(string actionName)
        {
            return inputAllowed && enableRequested && cachedActions.ContainsKey(actionName) && cachedActions[actionName].IsPressed();
        }

        public bool WasPressedThisFrame(string actionName)
        {
            return inputAllowed && enableRequested && cachedActions.ContainsKey(actionName) && cachedActions[actionName].WasPressedThisFrame();
        }
        
        virtual public void SetRebind(string actionName, Action onComplete = null, string excludeControl = null)
        {
            Disable();

            //if(cachedActions.TryGetValue(actionName,out var inputAction))
            //{
            //    rebindingOperation = 
            //}
        }
    }
}

