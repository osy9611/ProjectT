using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ProjectT
{
    public sealed class ManagerHost
    {
        private readonly Dictionary<Type, ManagerBase> registry = new Dictionary<Type, ManagerBase>();
        private readonly List<ManagerBase> managers = new List<ManagerBase>();

        private int startedCount;
        private List<Exception> shutdownErrors;

        // Shutdown에서 lifetime을 Dispose한 뒤에도 늦게 끝난 초기화가 취소 여부를 확인할 수 있도록 토큰을 따로 보관한다.
        private readonly CancellationToken lifetimeToken;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly UniTaskCompletionSource ready = new UniTaskCompletionSource();

        private bool isInitializationStarted;
        
        public ManagerState State { get; private set; } = ManagerState.Created;
        
        public Exception InitializationError { get; private set; }
        public IReadOnlyList<Exception> ShutdownErrors => (IReadOnlyList<Exception>)shutdownErrors ?? Array.Empty<Exception>();

        public UniTask WhenReady => ready.Task;

        public ManagerHost()
        {
            lifetimeToken = lifetime.Token;
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

        internal T GetReadyManager<T>() where T : ManagerBase
        {
            T manager = FindManager<T>();
            if (manager == null)
                throw new InvalidOperationException($"{typeof(T).Name} is not registered.");

            if (manager.State != ManagerState.Ready)
                throw new InvalidOperationException($"{typeof(T).Name} is {manager.State}, not Ready.");

            return manager;
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
            InitializeInternalAsync().Forget();
            return ready.Task;
        }

        private async UniTask InitializeInternalAsync()
        {
            try
            {
                foreach (var manager in managers)
                {
                    lifetimeToken.ThrowIfCancellationRequested();
                    ++startedCount;
                    await manager.InitializeAsync(lifetimeToken);
                }

                lifetimeToken.ThrowIfCancellationRequested();
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

                ShutdownInternal(ShutdownReason.InitializationFailure);
            }
        }

        public void Shutdown(ShutdownReason reason = ShutdownReason.Normal)
        {
            if (reason == ShutdownReason.InitializationFailure)
                throw new ArgumentException("InitializationFailure is reserved for the host.", nameof(reason));

            ShutdownInternal(reason);
        }

        private void ShutdownInternal(ShutdownReason reason)
        {
            if (State == ManagerState.Stopping || State == ManagerState.Stopped || State == ManagerState.Failed)
                return;

            State = ManagerState.Stopping;
            ErrorCollector.Run(ref shutdownErrors, lifetime, source => source.Cancel());

            for (int i = startedCount - 1; i >= 0; --i)
                ErrorCollector.Run(ref shutdownErrors, managers[i], manager => manager.Shutdown(reason));

            if (reason == ShutdownReason.InitializationFailure)
            {
                State = ManagerState.Failed;
                ready.TrySetException(InitializationError);
            }
            else
            {
                State = ManagerState.Stopped;
                ready.TrySetCanceled();
            }

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
