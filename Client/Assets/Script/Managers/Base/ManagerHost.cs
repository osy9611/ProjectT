using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace ProjectT
{
    public sealed class ManagerHost
    {
        private readonly Dictionary<Type, ManagerBase> registry = new Dictionary<Type, ManagerBase>();
        private readonly List<ManagerBase> managers = new List<ManagerBase>();
        private int startedCount;
        private readonly List<Exception> shutdownErrors = new List<Exception>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly CancellationToken token;
        private readonly UniTaskCompletionSource ready = new UniTaskCompletionSource();
        private readonly ManagerContext context;
        private bool isInitializationStarted;
        public ManagerState State { get; private set; } = ManagerState.Created;
        public Exception InitializationError { get; private set; }
        public IReadOnlyList<Exception> ShutdownErrors { get; }
        public UniTask WhenReady => ready.Task;

        public ManagerHost(Transform root, bool loadData)
        {
            ShutdownErrors = shutdownErrors.AsReadOnly();
            token = lifetime.Token;
            context = new ManagerContext(this, root, loadData);
        }

        public T Register<T>(T manager) where T : ManagerBase
        {
            if (State != ManagerState.Created)
                throw new InvalidOperationException("Registration is closed.");

            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            if (manager.GetType() != typeof(T))
                throw new ArgumentException("Register using the concrete manager type.");

            if (registry.ContainsKey(typeof(T)))
                throw new ArgumentException($"{typeof(T).Name} is already registered.");

            registry.Add(typeof(T), manager);
            managers.Add(manager);
            return manager;
        }

        internal T GetInitialized<T>() where T : ManagerBase
        {
            T manager = FindManager<T>();
            if (manager == null || manager.State != ManagerState.Ready)
                throw new InvalidOperationException($"Dependency {typeof(T).Name} is not initialized.");

            return manager;
        }

        public T GetReady<T>() where T : ManagerBase
        {
            if (State != ManagerState.Ready)
                throw new InvalidOperationException($"Managers are {State}; await WhenReady first.");

            return GetInitialized<T>();
        }

        public bool TryGetReady<T>(out T manager) where T : ManagerBase
        {
            manager = null;
            if (State != ManagerState.Ready)
                return false;

            manager = FindManager<T>();
            return manager != null;
        }

        private T FindManager<T>() where T : ManagerBase
        {
            if (registry.TryGetValue(typeof(T), out var manager))
                return (T)manager;

            return null;
        }

        public UniTask InitializeAsync()
        {
            if (isInitializationStarted)
                return ready.Task;

            if (State != ManagerState.Created)
                throw new InvalidOperationException($"Cannot initialize from {State}.");

            isInitializationStarted = true;
            State = ManagerState.Initializing;
            InitializeCoreAsync().Forget();
            return ready.Task;
        }

        private async UniTask InitializeCoreAsync()
        {
            try
            {
                foreach (var manager in managers)
                {
                    token.ThrowIfCancellationRequested();
                    ++startedCount;
                    await manager.InitializeAsync(context, token);
                }

                token.ThrowIfCancellationRequested();
                State = ManagerState.Ready;
                ready.TrySetResult();
            }
            catch (Exception error)
            {
                if (State == ManagerState.Stopping || State == ManagerState.Stopped)
                {
                    ready.TrySetCanceled();
                    return;
                }

                InitializationError = error;

                Shutdown(ShutdownReason.InitializationFailure);
            }
        }

        public void Shutdown(ShutdownReason reason = ShutdownReason.Normal)
        {
            if (State == ManagerState.Stopping || State == ManagerState.Stopped || State == ManagerState.Failed)
                return;

            State = ManagerState.Stopping;

            try
            {
                lifetime.Cancel();
            }
            catch (Exception error)
            {
                shutdownErrors.Add(error);
            }

            for (int i = startedCount - 1; i >= 0; --i)
            {
                try
                {
                    managers[i].Shutdown(reason);
                }
                catch (Exception error)
                {
                    shutdownErrors.Add(new Exception($"{managers[i].Name} shutdown failed.", error));
                }
            }

            State = reason == ShutdownReason.InitializationFailure ? ManagerState.Failed : ManagerState.Stopped;

            if (State == ManagerState.Failed)
                ready.TrySetException(InitializationError ?? new InvalidOperationException("Initialization was aborted."));
            else
                ready.TrySetCanceled();

            lifetime.Dispose();
        }

        public void Update(float dt)
        {
            if (State != ManagerState.Ready)
                return;

            foreach (var manager in managers)
            {
                if (State != ManagerState.Ready)
                    break;

                manager.OnUpdate(dt);
            }
        }

        public void FixedUpdate(float dt)
        {
            if (State != ManagerState.Ready)
                return;

            foreach (var manager in managers)
            {
                if (State != ManagerState.Ready)
                    break;

                manager.OnFixedUpdate(dt);
            }
        }

        public void LateUpdate()
        {
            if (State != ManagerState.Ready)
                return;

            foreach (var manager in managers)
            {
                if (State != ManagerState.Ready)
                    break;

                manager.OnLateUpdate();
            }
        }

        public void Focus(bool value)
        {
            if (State != ManagerState.Ready)
                return;

            foreach (var manager in managers)
            {
                if (State != ManagerState.Ready)
                    break;

                manager.OnAppFocus(value);
            }
        }

        public void Pause(bool value)
        {
            if (State != ManagerState.Ready)
                return;

            foreach (var manager in managers)
            {
                if (State != ManagerState.Ready)
                    break;

                manager.OnAppPause(value);
            }
        }
    }
}
