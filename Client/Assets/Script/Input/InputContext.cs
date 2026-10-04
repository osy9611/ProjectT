using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace ProjectT.Controller
{
    public sealed class InputContext
    {
        internal sealed class Binding
        {
            public readonly Action<InputAction.CallbackContext> Callback;
            public readonly Action OnReset;
            public readonly eInputEvent Events;
            public readonly bool Consume;
            public bool Delivered;
            public eInputEvent DeliveredPhase;

            public Binding(Action<InputAction.CallbackContext> callback, eInputEvent events, bool consume, Action onReset)
            {
                Callback = callback;
                Events = events;
                Consume = consume;
                OnReset = onReset;
            }

            public void ResetInternal()
            {
                if (!Delivered)
                    return;

                Delivered = false;
                DeliveredPhase = eInputEvent.None;
                OnReset?.Invoke();
            }
        }

        private Dictionary<string, Binding> bindings = new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<InputAction, Binding> resolved = new Dictionary<InputAction, Binding>();

        public string Name { get; }
        public int Priority { get; }
        internal Controller Owner { get; private set; }
        internal BaseActor Actor { get; set; }
        internal Dictionary<InputAction, Binding> Resolved => resolved;

        public InputContext(string name, int priority)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Context name is required.", nameof(name));

            Name = name;
            Priority = priority;
        }

        public InputContext Bind(string actionPath, Action<InputAction.CallbackContext> callback,
            eInputEvent events = eInputEvent.Start | eInputEvent.Performed | eInputEvent.Cancel,
            bool consume = true, Action onReset = null)
        {
            ValidatePathInternal(actionPath);
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            if (events == eInputEvent.None || (events & ~(eInputEvent.Start | eInputEvent.Performed | eInputEvent.Cancel)) != 0)
                throw new ArgumentOutOfRangeException(nameof(events));

            if (Owner != null)
                Owner.ResolveActionInternal(actionPath);

            bindings.TryGetValue(actionPath, out var previous);
            var updated = new Dictionary<string, Binding>(bindings, StringComparer.OrdinalIgnoreCase);
            var binding = new Binding(callback, events, consume, onReset);
            updated[actionPath] = binding;
            bindings = updated;
            // 차단 리셋 예외가 이전 바인딩의 리셋을 건너뛰면 교체된 바인딩은 다시 리셋될 수 없으므로 먼저 리셋한다.
            Owner?.RefreshContextInternal(this);
            previous?.ResetInternal();
            Owner?.ResetBlockedInternal();
            return this;
        }

        public InputContext Unbind(string actionPath)
        {
            ValidatePathInternal(actionPath);
            if (!bindings.TryGetValue(actionPath, out var binding))
                return this;

            var updated = new Dictionary<string, Binding>(bindings, StringComparer.OrdinalIgnoreCase);
            updated.Remove(actionPath);
            bindings = updated;
            Owner?.RefreshContextInternal(this);
            binding.ResetInternal();
            Owner?.ResetBlockedInternal();
            return this;
        }

        internal Dictionary<InputAction, Binding> BuildResolvedInternal(Controller owner)
        {
            var updated = new Dictionary<InputAction, Binding>();
            foreach (var pair in bindings)
            {
                var action = owner.ResolveActionInternal(pair.Key);
                if (updated.ContainsKey(action))
                    throw new ArgumentException("The same action is bound through multiple paths.");

                updated.Add(action, pair.Value);
            }

            return updated;
        }

        internal void AttachInternal(Controller owner, Dictionary<InputAction, Binding> bindings)
        {
            Owner = owner;
            resolved = bindings;
        }

        internal void UpdateResolvedInternal(Dictionary<InputAction, Binding> bindings)
        {
            resolved = bindings;
        }

        internal void DetachInternal()
        {
            Owner = null;
            resolved = new Dictionary<InputAction, Binding>();
        }

        // 예외로 건너뛴 바인딩은 Delivered가 남아 이 컨텍스트의 다음 전체 리셋에서 onReset을 받는다. Actor 해제·등록 해제로 버려진 컨텍스트는 보장하지 않는다.
        internal void ResetAllInternal()
        {
            var snapshot = bindings;
            foreach (var binding in snapshot.Values)
                binding.ResetInternal();
        }

        internal bool TryGetBindingInternal(InputAction action, out Binding binding)
        {
            return resolved.TryGetValue(action, out binding);
        }

        private static void ValidatePathInternal(string actionPath)
        {
            int slash = actionPath?.IndexOf('/') ?? -1;
            if (slash < 1 || slash == actionPath.Length - 1)
                throw new ArgumentException("Use a qualified Map/Action path.", nameof(actionPath));
        }
    }
}
