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

        // Shutdown에서 lifetime을 Dispose한 뒤에도 늦게 끝난 초기화가 취소 여부를 확인할 수 있도록 토큰을 따로 보관한다.
        private readonly CancellationToken lifetimeToken;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly UniTaskCompletionSource ready = new UniTaskCompletionSource();

        private bool isInitializationStarted;
        
        public ManagerState State { get; private set; } = ManagerState.Created;

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
            if (!registry.TryGetValue(typeof(T), out var manager))
                throw new InvalidOperationException($"{typeof(T).Name} is not registered.");

            if (manager.State != ManagerState.Ready)
                throw new InvalidOperationException($"{typeof(T).Name} is {manager.State}, not Ready.");

            return (T)manager;
        }

        public UniTask InitializeAsync()
        {
            if (isInitializationStarted)
                return ready.Task;

            if (State != ManagerState.Created)
                throw new InvalidOperationException($"Cannot initialize from {State}.");

            isInitializationStarted = true;
            State = ManagerState.Initializing;
            InitializeInternalAsync().Forget(Global.LogException);
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
                    if (error is OperationCanceledException && lifetimeToken.IsCancellationRequested)
                        return;

                    // 종료 뒤 늦게 끝난 매니저의 오류는 ready가 이미 취소되어 전달할 곳이 없으므로 Forget 처리기에서 보고한다.
                    throw;
                }

                var errors = StopManagersInternal(ShutdownReason.InitializationFailure);
                State = ManagerState.Failed;
                // 정리 오류는 초기화 오류와 함께 ready로 전달해 Global의 초기화 경계에서 한 번에 보고한다.
                // UniTaskCompletionSource는 취소 예외를 취소로 바꿔 메시지를 버리므로 취소 예외도 감싸서 전달한다.
                if (errors != null || error is OperationCanceledException)
                {
                    errors ??= new List<Exception>();
                    errors.Insert(0, error);
                    error = new AggregateException(errors);
                }

                ready.TrySetException(error);
                lifetime.Dispose();
            }
        }

        public void Shutdown(ShutdownReason reason = ShutdownReason.Normal)
        {
            if (reason == ShutdownReason.InitializationFailure)
                throw new ArgumentException("InitializationFailure is reserved for the host.", nameof(reason));

            if (State == ManagerState.Stopping || State == ManagerState.Stopped || State == ManagerState.Failed)
                return;

            var errors = StopManagersInternal(reason);
            State = ManagerState.Stopped;
            ready.TrySetCanceled();
            lifetime.Dispose();
            ErrorCollector.ThrowIfAny(errors);
        }

        private List<Exception> StopManagersInternal(ShutdownReason reason)
        {
            State = ManagerState.Stopping;
            List<Exception> errors = null;
            ErrorCollector.Run(ref errors, lifetime, source => source.Cancel());

            for (int i = startedCount - 1; i >= 0; --i)
                ErrorCollector.Run(ref errors, managers[i], manager => manager.Shutdown(reason));

            return errors;
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
