using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ProjectT
{
    /// <remarks>Use public APIs on the Unity main thread. Owner tokens may be canceled from worker threads.</remarks>
    public class NotificationManager : ManagerBase
    {
        private sealed class Subscription
        {
            public readonly NotificationManager Owner;
            public readonly NotificationId Id;
            public readonly Action<object[]> Listener;
            public readonly CancellationToken OwnerToken;
            public readonly int Priority;
            public bool IsOwnerCanceled;
            public bool HasCancellationRegistration;
            public CancellationTokenRegistration CancellationRegistration;

            public Subscription(NotificationManager owner, NotificationId id, Action<object[]> listener, CancellationToken ownerToken, int priority)
            {
                Owner = owner;
                Id = id;
                Listener = listener;
                OwnerToken = ownerToken;
                Priority = priority;
            }
        }

        private static readonly object[] EmptyArgs = Array.Empty<object>();

        private readonly object syncRoot = new object();
        private readonly Dictionary<NotificationId, List<Subscription>> subscriptions = new Dictionary<NotificationId, List<Subscription>>();

        protected override void OnShutdown(ShutdownReason reason)
        {
            List<CancellationTokenRegistration> registrations = null;

            lock (syncRoot)
            {
                foreach (var pair in subscriptions)
                {
                    var listeners = pair.Value;
                    for (int i = 0; i < listeners.Count; ++i)
                    {
                        var subscription = listeners[i];
                        subscription.IsOwnerCanceled = true;
                        AddCancellationRegistrationInternal(subscription, ref registrations);
                    }
                }

                subscriptions.Clear();
            }

            DisposeRegistrationsInternal(registrations);
        }

        public void Subscribe(NotificationId id, Action<object[]> listener, CancellationToken cancellationToken = default, int priority = 0)
        {
            ValidateIdInternal(id);

            if (listener == null)
                throw new ArgumentNullException(nameof(listener));

            ThrowIfWorkUnavailableInternal();

            List<CancellationTokenRegistration> registrations = null;

            try
            {
                lock (syncRoot)
                {
                    ThrowIfWorkUnavailableInternal();

                    subscriptions.TryGetValue(id, out var listeners);

                    int expiredIndex = -1;
                    for (int i = 0; listeners != null && i < listeners.Count; ++i)
                    {
                        var existing = listeners[i];
                        if (existing.Listener != listener)
                            continue;
                        if (!existing.IsOwnerCanceled && !existing.OwnerToken.IsCancellationRequested)
                            return;

                        expiredIndex = i;
                        break;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    if (expiredIndex >= 0)
                    {
                        var existing = listeners[expiredIndex];
                        existing.IsOwnerCanceled = true;
                        listeners.RemoveAt(expiredIndex);
                        AddCancellationRegistrationInternal(existing, ref registrations);
                    }

                    if (listeners == null)
                    {
                        listeners = new List<Subscription>();
                        subscriptions.Add(id, listeners);
                    }

                    var subscription = new Subscription(this, id, listener, cancellationToken, priority);
                    int insertionIndex = listeners.Count;
                    for (int i = 0; i < listeners.Count; ++i)
                    {
                        if (priority > listeners[i].Priority)
                        {
                            insertionIndex = i;
                            break;
                        }
                    }

                    listeners.Insert(insertionIndex, subscription);

                    if (cancellationToken.CanBeCanceled)
                    {
                        var registration = cancellationToken.Register(OnSubscriptionCanceledInternal, subscription);
                        subscription.CancellationRegistration = registration;
                        subscription.HasCancellationRegistration = true;

                        if (subscription.IsOwnerCanceled)
                            AddCancellationRegistrationInternal(subscription, ref registrations);
                    }
                }
            }
            finally
            {
                DisposeRegistrationsInternal(registrations);
            }
        }

        public void Unsubscribe(NotificationId id, Action<object[]> listener)
        {
            ValidateIdInternal(id);

            if (listener == null)
                throw new ArgumentNullException(nameof(listener));

            CancellationTokenRegistration registration = default;
            bool shouldDisposeRegistration = false;

            lock (syncRoot)
            {
                if (!subscriptions.TryGetValue(id, out var listeners))
                    return;

                for (int i = 0; i < listeners.Count; ++i)
                {
                    var subscription = listeners[i];
                    if (subscription.Listener != listener)
                        continue;

                    listeners.RemoveAt(i);
                    if (listeners.Count == 0)
                        subscriptions.Remove(id);

                    if (subscription.HasCancellationRegistration)
                    {
                        registration = subscription.CancellationRegistration;
                        subscription.HasCancellationRegistration = false;
                        shouldDisposeRegistration = true;
                    }

                    break;
                }
            }

            if (shouldDisposeRegistration)
                registration.Dispose();
        }

        public void Publish(NotificationId id, params object[] args)
        {
            ValidateIdInternal(id);

            ThrowIfWorkUnavailableInternal();

            Subscription[] snapshot;
            lock (syncRoot)
            {
                if (!IsWorkAllowedInternal() || !subscriptions.TryGetValue(id, out var listeners) || listeners.Count == 0)
                    return;

                snapshot = listeners.ToArray();
            }

            var normalizedArgs = args ?? EmptyArgs;
            for (int i = 0; i < snapshot.Length; ++i)
            {
                var subscription = snapshot[i];
                lock (syncRoot)
                {
                    if (!IsWorkAllowedInternal())
                        return;
                    if (subscription.IsOwnerCanceled || subscription.OwnerToken.IsCancellationRequested)
                        continue;
                }

                subscription.Listener(normalizedArgs);
            }
        }

        public async UniTask PublishNextFrameAsync(NotificationId id, CancellationToken cancellationToken = default, params object[] args)
        {
            ValidateIdInternal(id);

            ThrowIfWorkUnavailableInternal();

            var scheduledArgs = CloneArgsInternal(args);
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, cancellationToken))
            {
                await UniTask.NextFrame(cancellationToken: linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                Publish(id, scheduledArgs);
            }
        }

        public async UniTask PublishAfterSecondsAsync(NotificationId id, float seconds, CancellationToken cancellationToken = default, params object[] args)
        {
            ValidateIdInternal(id);

            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));

            ThrowIfWorkUnavailableInternal();

            var scheduledArgs = CloneArgsInternal(args);
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, cancellationToken))
            {
                if (seconds == 0.0f)
                    await UniTask.NextFrame(cancellationToken: linked.Token);
                else
                    await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: linked.Token);

                linked.Token.ThrowIfCancellationRequested();
                Publish(id, scheduledArgs);
            }
        }

        private static void OnSubscriptionCanceledInternal(object state)
        {
            var subscription = (Subscription)state;
            subscription.Owner.CancelSubscriptionInternal(subscription);
        }

        private void CancelSubscriptionInternal(Subscription subscription)
        {
            CancellationTokenRegistration registration = default;
            bool shouldDisposeRegistration = false;

            lock (syncRoot)
            {
                subscription.IsOwnerCanceled = true;

                if (subscriptions.TryGetValue(subscription.Id, out var listeners))
                {
                    listeners.Remove(subscription);
                    if (listeners.Count == 0)
                        subscriptions.Remove(subscription.Id);
                }

                if (subscription.HasCancellationRegistration)
                {
                    registration = subscription.CancellationRegistration;
                    subscription.HasCancellationRegistration = false;
                    shouldDisposeRegistration = true;
                }
            }

            if (shouldDisposeRegistration)
                registration.Dispose();
        }

        private void AddCancellationRegistrationInternal(Subscription subscription, ref List<CancellationTokenRegistration> registrations)
        {
            if (!subscription.HasCancellationRegistration)
                return;

            if (registrations == null)
                registrations = new List<CancellationTokenRegistration>();

            registrations.Add(subscription.CancellationRegistration);
            subscription.HasCancellationRegistration = false;
        }

        private static void DisposeRegistrationsInternal(List<CancellationTokenRegistration> registrations)
        {
            if (registrations == null)
                return;

            for (int i = 0; i < registrations.Count; ++i)
                registrations[i].Dispose();
        }

        private bool IsWorkAllowedInternal()
        {
            return State == ManagerState.Ready && !LifetimeToken.IsCancellationRequested;
        }

        private static object[] CloneArgsInternal(object[] args)
        {
            return args == null || args.Length == 0 ? EmptyArgs : (object[])args.Clone();
        }

        private static void ValidateIdInternal(NotificationId id)
        {
            if (id == NotificationId.None)
                throw new ArgumentOutOfRangeException(nameof(id));
        }
    }
}
