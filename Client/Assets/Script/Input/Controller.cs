using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

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
        // 같은 컨텍스트를 재등록해도 진행 중인 입력에는 이전 등록만 남는다.
        private sealed class Registration
        {
            public readonly InputContext Context;

            public Registration(InputContext context)
            {
                Context = context;
            }
        }

        protected InputActionAsset inputActionAsset;

        private List<Registration> contexts = new List<Registration>();
        private bool inputAllowed = true;
        private bool enableRequested;
        private bool releasing;
        private bool applyingActive;
        private int deliveryVersion;

        private bool IsActive => inputActionAsset != null && enableRequested && inputAllowed && !releasing;

        public virtual void Init(InputActionAsset asset)
        {
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));

            if (inputActionAsset != null || releasing)
                throw new InvalidOperationException("Controller is already initialized or releasing.");

            inputActionAsset = asset;
            foreach (var map in asset.actionMaps)
            {
                foreach (var action in map.actions)
                {
                    action.started += OnStartedInternal;
                    action.performed += OnPerformedInternal;
                    action.canceled += OnCanceledInternal;
                }
            }

            ApplyActiveInternal();
        }

        public virtual void Enable()
        {
            if (releasing || enableRequested)
                return;

            enableRequested = true;
            deliveryVersion++;
            ApplyActiveInternal();
        }

        public virtual void Disable()
        {
            if (!enableRequested)
                return;

            enableRequested = false;
            deliveryVersion++;
            ApplyActiveInternal();
        }

        internal void SetInputAllowed(bool allowed)
        {
            if (releasing || inputAllowed == allowed)
                return;

            inputAllowed = allowed;
            deliveryVersion++;
            ApplyActiveInternal();
        }

        public virtual void Release()
        {
            if (releasing || inputActionAsset == null)
                return;

            releasing = true;
            enableRequested = false;
            deliveryVersion++;
            var asset = inputActionAsset;
            try
            {
                try
                {
                    ResetAllInternal();
                }
                finally
                {
                    DisableActionsInternal(asset);
                }
            }
            finally
            {
                foreach (var map in asset.actionMaps)
                {
                    foreach (var action in map.actions)
                    {
                        action.started -= OnStartedInternal;
                        action.performed -= OnPerformedInternal;
                        action.canceled -= OnCanceledInternal;
                    }
                }

                var previous = contexts;
                contexts = new List<Registration>();
                inputActionAsset = null;
                releasing = false;
                List<Exception> errors = null;
                foreach (var entry in previous)
                    ErrorCollector.Run(ref errors, entry.Context, item => item.DetachInternal());

                ErrorCollector.ThrowIfAny(errors);
            }
        }

        public void AddContext(InputContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (inputActionAsset == null || releasing)
                throw new InvalidOperationException("Controller is not initialized.");

            if (context.Owner != null && !ReferenceEquals(context.Owner, this))
                throw new InvalidOperationException("Input context is already registered to another controller.");

            var resolved = context.BuildResolvedInternal(this);
            var updated = new List<Registration>(contexts);
            updated.RemoveAll(entry => ReferenceEquals(entry.Context, context));
            int index = 0;
            while (index < updated.Count && updated[index].Context.Priority > context.Priority)
                index++;

            updated.Insert(index, new Registration(context));
            context.AttachInternal(this, resolved);
            contexts = updated;
            ResetBlockedInternal();
        }

        public void RemoveContext(InputContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (!ReferenceEquals(context.Owner, this))
                return;

            var updated = new List<Registration>(contexts);
            updated.RemoveAll(entry => ReferenceEquals(entry.Context, context));
            contexts = updated;
            context.DetachInternal();
        }

        internal InputAction ResolveActionInternal(string path)
        {
            if (inputActionAsset == null)
                throw new InvalidOperationException("Controller is not initialized.");

            return inputActionAsset.FindAction(path, true);
        }

        internal void RefreshContextInternal(InputContext context)
        {
            if (!ReferenceEquals(context.Owner, this))
                return;

            context.UpdateResolvedInternal(context.BuildResolvedInternal(this));
            var updated = new List<Registration>(contexts);
            int index = updated.FindIndex(entry => ReferenceEquals(entry.Context, context));
            updated[index] = new Registration(context);
            contexts = updated;
            ResetBlockedInternal();
        }

        private void ApplyActiveInternal()
        {
            if (inputActionAsset == null || applyingActive)
                return;

            applyingActive = true;
            try
            {
                int version;
                do
                {
                    version = deliveryVersion;
                    var asset = inputActionAsset;
                    if (IsActive)
                    {
                        foreach (var map in asset.actionMaps)
                        {
                            foreach (var action in map.actions)
                            {
                                action.Enable();
                                if (version != deliveryVersion)
                                    break;
                            }

                            if (version != deliveryVersion)
                                break;
                        }
                    }
                    else
                    {
                        try
                        {
                            DisableActionsInternal(asset);
                        }
                        finally
                        {
                            ResetAllInternal();
                        }
                    }
                }
                while (inputActionAsset != null && version != deliveryVersion);
            }
            finally
            {
                applyingActive = false;
            }
        }

        private static void DisableActionsInternal(InputActionAsset asset)
        {
            foreach (var map in asset.actionMaps)
            {
                foreach (var action in map.actions)
                    action.Disable();
            }
        }

        private void ResetAllInternal()
        {
            var snapshot = contexts;
            List<Exception> errors = null;
            for (int i = 0; i < snapshot.Count; i++)
                ErrorCollector.Run(ref errors, snapshot[i].Context, item => item.ResetAllInternal());

            ErrorCollector.ThrowIfAny(errors);
        }

        private void ResetBlockedInternal()
        {
            var snapshot = contexts;
            List<Exception> errors = null;
            for (int i = 0; i < snapshot.Count; i++)
            {
                var entry = snapshot[i];
                foreach (var pair in entry.Context.Resolved)
                {
                    if (!contexts.Contains(entry))
                        break;

                    if (pair.Value.Delivered && IsBlockedInternal(entry, pair.Key, pair.Value.DeliveredPhase))
                        ErrorCollector.Run(ref errors, pair.Value, binding => binding.ResetInternal());
                }
            }

            ErrorCollector.ThrowIfAny(errors);
        }

        private bool IsBlockedInternal(Registration registration, InputAction action, eInputEvent phase)
        {
            for (int i = 0; i < contexts.Count; i++)
            {
                var entry = contexts[i];
                if (ReferenceEquals(entry, registration))
                    return false;

                if (entry.Context.TryGetBindingInternal(action, out var binding) && binding.Consume && (binding.Events & phase) != 0)
                    return true;
            }

            return false;
        }

        private void OnStartedInternal(InputAction.CallbackContext context)
        {
            DispatchInternal(context, eInputEvent.Start);
        }

        private void OnPerformedInternal(InputAction.CallbackContext context)
        {
            DispatchInternal(context, eInputEvent.Performed);
        }

        private void OnCanceledInternal(InputAction.CallbackContext context)
        {
            DispatchInternal(context, eInputEvent.Cancel);
        }

        private void DispatchInternal(InputAction.CallbackContext context, eInputEvent phase)
        {
            if (!IsActive || !context.action.enabled)
                return;

            int version = deliveryVersion;
            var snapshot = contexts;
            for (int i = 0; i < snapshot.Count; i++)
            {
                var registration = snapshot[i];
                if (!contexts.Contains(registration) || !registration.Context.TryGetBindingInternal(context.action, out var binding))
                    continue;

                if ((binding.Events & phase) == 0)
                    continue;

                bool consume = binding.Consume;
                if (phase != eInputEvent.Cancel)
                {
                    binding.Delivered = true;
                    binding.DeliveredPhase = phase;
                }

                binding.Callback(context);
                if (phase == eInputEvent.Cancel)
                    binding.Delivered = false;

                if (deliveryVersion != version || !IsActive)
                    break;

                if (consume)
                {
                    if (phase != eInputEvent.Cancel)
                        ResetLowerBindingsInternal(snapshot, i + 1, context.action);
                    break;
                }
            }

            if (phase == eInputEvent.Cancel && IsActive)
                ResetUndeliveredCancellationInternal(context.action);

            if (IsActive && !ReferenceEquals(snapshot, contexts))
                ResetBlockedInternal();
        }

        private void ResetLowerBindingsInternal(List<Registration> snapshot, int start, InputAction action)
        {
            List<Exception> errors = null;
            for (int i = start; i < snapshot.Count; i++)
            {
                if (contexts.Contains(snapshot[i]) && snapshot[i].Context.TryGetBindingInternal(action, out var binding))
                    ErrorCollector.Run(ref errors, binding, item => item.ResetInternal());
            }

            ErrorCollector.ThrowIfAny(errors);
        }

        private void ResetUndeliveredCancellationInternal(InputAction action)
        {
            var snapshot = contexts;
            List<Exception> errors = null;
            foreach (var entry in snapshot)
            {
                if (contexts.Contains(entry) && entry.Context.TryGetBindingInternal(action, out var binding))
                    ErrorCollector.Run(ref errors, binding, item => item.ResetInternal());
            }

            ErrorCollector.ThrowIfAny(errors);
        }
    }
}
