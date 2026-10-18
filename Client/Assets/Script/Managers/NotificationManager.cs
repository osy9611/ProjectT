using System;
using System.Collections.Generic;
using System.Threading;

namespace ProjectT
{
    /// <remarks>Use public APIs on the Unity main thread. Owner tokens may be canceled from any thread; canceled subscriptions stop receiving immediately and are removed on the next Subscribe or Publish of the same id.</remarks>
    public class NotificationManager : ManagerBase
    {
        private readonly struct Subscription
        {
            public readonly Action Listener;
            public readonly CancellationToken OwnerToken;

            public Subscription(Action listener, CancellationToken ownerToken)
            {
                Listener = listener;
                OwnerToken = ownerToken;
            }
        }

        private readonly Dictionary<NotificationId, List<Subscription>> subscriptions = new Dictionary<NotificationId, List<Subscription>>();

        protected override void OnShutdown(ShutdownReason reason)
        {
            subscriptions.Clear();
        }

        public void Subscribe(NotificationId id, Action listener, CancellationToken ownerToken)
        {
            ValidateIdInternal(id);

            if (listener == null)
                throw new ArgumentNullException(nameof(listener));

            ThrowIfWorkUnavailableInternal();
            ownerToken.ThrowIfCancellationRequested();

            if (!subscriptions.TryGetValue(id, out var listeners))
            {
                listeners = new List<Subscription>();
                subscriptions.Add(id, listeners);
            }

            RemoveCanceledInternal(listeners);
            for (int i = 0; i < listeners.Count; ++i)
            {
                if (listeners[i].Listener == listener)
                    return;
            }

            listeners.Add(new Subscription(listener, ownerToken));
        }

        public void Publish(NotificationId id)
        {
            ValidateIdInternal(id);

            ThrowIfWorkUnavailableInternal();

            if (!subscriptions.TryGetValue(id, out var listeners))
                return;

            RemoveCanceledInternal(listeners);
            var snapshot = listeners.ToArray();
            for (int i = 0; i < snapshot.Length; ++i)
            {
                if (!CanAcceptWorkInternal())
                    return;
                if (snapshot[i].OwnerToken.IsCancellationRequested)
                    continue;

                snapshot[i].Listener();
            }
        }

        private static void RemoveCanceledInternal(List<Subscription> listeners)
        {
            listeners.RemoveAll(subscription => subscription.OwnerToken.IsCancellationRequested);
        }

        private static void ValidateIdInternal(NotificationId id)
        {
            if (id == NotificationId.None)
                throw new ArgumentOutOfRangeException(nameof(id));
        }
    }
}
