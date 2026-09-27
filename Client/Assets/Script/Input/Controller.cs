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
        private struct EventEntry
        {
            public Action<InputAction.CallbackContext> legacyCallback;
            public Func<InputAction.CallbackContext, bool> priorityCallback;
            public int priority;
        }

        private sealed class EventBinding
        {
            private readonly Controller owner;
            private readonly InputAction action;
            private readonly eInputEvent eventType;
            private readonly Action<InputAction.CallbackContext> dispatcher;
            private EventEntry[] entries = Array.Empty<EventEntry>();
            private bool disposed;

            public EventBinding(Controller owner, InputAction action, eInputEvent eventType)
            {
                this.owner = owner;
                this.action = action;
                this.eventType = eventType;
                dispatcher = DispatchInternal;
            }

            public void AddInternal(Action<InputAction.CallbackContext> legacyCallback, Func<InputAction.CallbackContext, bool> priorityCallback, int priority)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (legacyCallback != null && entries[i].legacyCallback == legacyCallback ||
                        priorityCallback != null && entries[i].priorityCallback == priorityCallback)
                        return;
                }

                int index = 0;
                while (index < entries.Length && entries[index].priority >= priority)
                    index++;

                var updated = new EventEntry[entries.Length + 1];
                Array.Copy(entries, 0, updated, 0, index);
                updated[index] = new EventEntry
                {
                    legacyCallback = legacyCallback,
                    priorityCallback = priorityCallback,
                    priority = priority
                };
                Array.Copy(entries, index, updated, index + 1, entries.Length - index);

                bool attach = entries.Length == 0;
                entries = updated;
                if (attach)
                    SubscribeInternal();
            }

            public void RemoveInternal(Action<InputAction.CallbackContext> legacyCallback, Func<InputAction.CallbackContext, bool> priorityCallback)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (legacyCallback != null && entries[i].legacyCallback == legacyCallback ||
                        priorityCallback != null && entries[i].priorityCallback == priorityCallback)
                    {
                        var updated = new EventEntry[entries.Length - 1];
                        Array.Copy(entries, 0, updated, 0, i);
                        Array.Copy(entries, i + 1, updated, i, entries.Length - i - 1);
                        entries = updated;
                        if (updated.Length == 0)
                            UnsubscribeInternal();
                        return;
                    }
                }
            }

            public void DisposeInternal()
            {
                if (disposed)
                    return;

                disposed = true;
                if (entries.Length > 0)
                    UnsubscribeInternal();
                entries = Array.Empty<EventEntry>();
            }

            private void SubscribeInternal()
            {
                if (eventType == eInputEvent.Start)
                    action.started += dispatcher;
                else if (eventType == eInputEvent.Performed)
                    action.performed += dispatcher;
                else
                    action.canceled += dispatcher;
            }

            private void UnsubscribeInternal()
            {
                if (eventType == eInputEvent.Start)
                    action.started -= dispatcher;
                else if (eventType == eInputEvent.Performed)
                    action.performed -= dispatcher;
                else
                    action.canceled -= dispatcher;
            }

            private void DispatchInternal(InputAction.CallbackContext context)
            {
                if (disposed || eventType != eInputEvent.Cancel && (!owner.inputAllowed || !owner.enableRequested))
                    return;

                int version = owner.lifecycleVersion;
                var snapshot = entries;
                for (int i = 0; i < snapshot.Length; i++)
                {
                    if (snapshot[i].priorityCallback != null)
                    {
                        if (snapshot[i].priorityCallback(context))
                            return;
                    }
                    else
                        snapshot[i].legacyCallback(context);

                    if (disposed || owner.lifecycleVersion != version)
                        return;
                }
            }
        }

        protected InputActionAsset inputActionAsset;
        protected InputUser inputUser;
        protected InputActionRebindingExtensions.RebindingOperation rebindingOperation;

        private bool inputAllowed = true;
        private bool enableRequested;
        private bool releasing;
        private int lifecycleVersion;
        private int actionMapVersion;

        protected string actionKey = "Player";
        public string ActionKey { get => actionKey; }

        private Dictionary<string, InputAction> cachedActions = new Dictionary<string, InputAction>();
        private Dictionary<InputAction, EventBinding[]> eventBindings = new Dictionary<InputAction, EventBinding[]>();

        virtual public void Init(InputActionAsset inputActionAsset, string actionKey = null, InputUser? inputUser = null)
        {
            if (releasing)
                return;

            if (inputActionAsset == null)
            {
                Global.Instance.LogWarning("[Controller] This InputActioAsset is null");
                return;
            }

            int version = ++actionMapVersion;
            lifecycleVersion++;
            DisposeEventBindingsInternal();
            cachedActions.Clear();
            this.inputActionAsset?.FindActionMap(this.actionKey)?.Disable();
            if (actionMapVersion != version)
                return;

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
            if (releasing || inputActionAsset == null)
                return;

            enableRequested = true;
            if (inputAllowed)
                inputActionAsset.FindActionMap(actionKey)?.Enable();
        }

        virtual public void Disable()
        {
            lifecycleVersion++;
            if (inputActionAsset == null)
                return;

            enableRequested = false;
            inputActionAsset.FindActionMap(actionKey)?.Disable();
        }

        internal void SetInputAllowed(bool allowed)
        {
            if (releasing)
                return;

            lifecycleVersion++;
            inputAllowed = allowed;
            var map = inputActionAsset?.FindActionMap(actionKey);
            if (allowed && enableRequested)
                map?.Enable();
            else
                map?.Disable();
        }

        public virtual void Release()
        {
            if (releasing)
                return;

            releasing = true;
            lifecycleVersion++;
            actionMapVersion++;
            try
            {
                Disable();
            }
            finally
            {
                DisposeEventBindingsInternal();
                cachedActions.Clear();
                inputActionAsset = null;
                enableRequested = false;
                var operation = rebindingOperation;
                rebindingOperation = null;
                try
                {
                    operation?.Dispose();
                }
                finally
                {
                    releasing = false;
                }
            }
        }

        public void AddEvent(string actionName, System.Action<InputAction.CallbackContext> callback, eInputEvent eventType)
        {
            if (releasing)
                return;

            if (callback == null)
            {
                Global.Instance.LogWarning($"[Controller] AddEvent Fail Callback is null");
                return;
            }

            if (cachedActions.TryGetValue(actionName, out var inputAction))
                AddEventInternal(inputAction, callback, null, eventType, 0);
        }

        public void RemoveEvent(string actionName, System.Action<InputAction.CallbackContext> callback, eInputEvent eventType)
        {
            if (callback == null)
            {
                Global.Instance.LogWarning($"[Controller] RemoveEvent Fail Callback is null");
                return;
            }

            if (cachedActions.TryGetValue(actionName, out var inputAction))
                RemoveEventInternal(inputAction, callback, null, eventType);
        }

        public void AddPriorityEvent(string actionName, Func<InputAction.CallbackContext, bool> callback, eInputEvent eventType, int priority)
        {
            if (releasing)
                return;

            if (callback == null)
            {
                Global.Instance.LogWarning($"[Controller] AddPriorityEvent Fail Callback is null");
                return;
            }

            if (cachedActions.TryGetValue(actionName, out var inputAction))
                AddEventInternal(inputAction, null, callback, eventType, priority);
        }

        public void RemovePriorityEvent(string actionName, Func<InputAction.CallbackContext, bool> callback, eInputEvent eventType)
        {
            if (callback == null)
            {
                Global.Instance.LogWarning($"[Controller] RemovePriorityEvent Fail Callback is null");
                return;
            }

            if (cachedActions.TryGetValue(actionName, out var inputAction))
                RemoveEventInternal(inputAction, null, callback, eventType);
        }

        private EventBinding GetEventBindingInternal(InputAction inputAction, int phaseIndex, eInputEvent eventType)
        {
            if (!eventBindings.TryGetValue(inputAction, out var bindings))
            {
                bindings = new EventBinding[3];
                eventBindings.Add(inputAction, bindings);
            }

            if (bindings[phaseIndex] == null)
                bindings[phaseIndex] = new EventBinding(this, inputAction, eventType);
            return bindings[phaseIndex];
        }

        private void AddEventInternal(InputAction inputAction, Action<InputAction.CallbackContext> legacyCallback, Func<InputAction.CallbackContext, bool> priorityCallback, eInputEvent eventType, int priority)
        {
            if ((eventType & eInputEvent.Start) != 0)
                GetEventBindingInternal(inputAction, 0, eInputEvent.Start).AddInternal(legacyCallback, priorityCallback, priority);
            if ((eventType & eInputEvent.Performed) != 0)
                GetEventBindingInternal(inputAction, 1, eInputEvent.Performed).AddInternal(legacyCallback, priorityCallback, priority);
            if ((eventType & eInputEvent.Cancel) != 0)
                GetEventBindingInternal(inputAction, 2, eInputEvent.Cancel).AddInternal(legacyCallback, priorityCallback, priority);
        }

        private void RemoveEventInternal(InputAction inputAction, Action<InputAction.CallbackContext> legacyCallback, Func<InputAction.CallbackContext, bool> priorityCallback, eInputEvent eventType)
        {
            if (!eventBindings.TryGetValue(inputAction, out var bindings))
                return;

            if ((eventType & eInputEvent.Start) != 0)
                bindings[0]?.RemoveInternal(legacyCallback, priorityCallback);
            if ((eventType & eInputEvent.Performed) != 0)
                bindings[1]?.RemoveInternal(legacyCallback, priorityCallback);
            if ((eventType & eInputEvent.Cancel) != 0)
                bindings[2]?.RemoveInternal(legacyCallback, priorityCallback);
        }

        private void DisposeEventBindingsInternal()
        {
            foreach (var bindings in eventBindings.Values)
            {
                for (int i = 0; i < bindings.Length; i++)
                    bindings[i]?.DisposeInternal();
            }

            eventBindings.Clear();
        }

        virtual public void SwitchActionMap(string actionKey)
        {
            if (releasing)
                return;

            int version = ++actionMapVersion;
            Disable();
            if (actionMapVersion != version || inputActionAsset == null)
                return;

            this.actionKey = actionKey;
            CacheInputActions();
            Enable();
        }

        public bool IsPressed(string actionName)
        {
            return inputAllowed && enableRequested && cachedActions.TryGetValue(actionName, out var inputAction) && inputAction.IsPressed();
        }

        public bool WasPressedThisFrame(string actionName)
        {
            return inputAllowed && enableRequested && cachedActions.TryGetValue(actionName, out var inputAction) && inputAction.WasPressedThisFrame();
        }
        
        virtual public void SetRebind(string actionName, Action onComplete = null, string excludeControl = null)
        {
            Disable();
        }
    }
}

