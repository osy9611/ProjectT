using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

namespace ProjectT
{
    public enum ManagerState
    {
        Created,
        Initializing,
        Ready,
        Stopping,
        Stopped,
        Failed
    }
    public enum ShutdownReason
    {
        Normal,
        ApplicationExit,
        InitializationFailure
    }

    public sealed class ManagerContext
    {
        private readonly ManagerHost host;
        public Transform Root { get; }
        public bool LoadData { get; }
        internal ManagerContext(ManagerHost host, Transform root, bool loadData)
        {
            this.host = host;
            Root = root;
            LoadData = loadData;
        }
        public T Get<T>() where T : ManagerBase => host.GetInitialized<T>();
    }

    public abstract class ManagerBase
    {
        public string Name => GetType().Name;
        public ManagerState State { get; private set; } = ManagerState.Created;
        protected ManagerContext Context { get; private set; }
        protected CancellationToken LifetimeToken { get; private set; }
        protected Transform m_rootObject;
        public Transform RootObject => m_rootObject;

        internal async UniTask InitializeAsync(ManagerContext context, CancellationToken token)
        {
            if (State != ManagerState.Created)
                throw new InvalidOperationException($"{Name} cannot initialize from {State}.");
            Context = context;
            LifetimeToken = token;
            State = ManagerState.Initializing;
            await OnInitializeAsync(token);
            token.ThrowIfCancellationRequested();
            if (State != ManagerState.Initializing)
                throw new OperationCanceledException(token);
            State = ManagerState.Ready;
        }

        internal void Shutdown(ShutdownReason reason)
        {
            if (State == ManagerState.Created || State == ManagerState.Stopping || State == ManagerState.Stopped)
                return;
            State = ManagerState.Stopping;
            try
            {
                OnShutdown(reason);
            }
            finally
            {
                try
                {
                    DestroyRootObject();
                }
                finally
                {
                    State = ManagerState.Stopped;
                }
            }
        }

        protected virtual UniTask OnInitializeAsync(CancellationToken token) => UniTask.CompletedTask;
        protected virtual void OnShutdown(ShutdownReason reason)
        {
        }
        protected void ThrowIfStopped()
        {
            LifetimeToken.ThrowIfCancellationRequested();
            if (State != ManagerState.Ready && State != ManagerState.Initializing)
                throw new InvalidOperationException($"{Name} is {State}.");
        }
        protected void CreateRootObject(Transform parent, string name)
        {
            ThrowIfStopped();
            if (m_rootObject == null)
                m_rootObject = new GameObject(name).transform;
            m_rootObject.SetParent(parent, false);
        }
        private void DestroyRootObject()
        {
            if (m_rootObject == null)
                return;
            var root = m_rootObject.gameObject;
            m_rootObject = null;
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
        }
        public virtual void OnFixedUpdate(float dt)
        {
        }
        public virtual void OnUpdate(float dt)
        {
        }
        public virtual void OnLateUpdate()
        {
        }
        public virtual void OnAppFocus(bool focused)
        {
        }
        public virtual void OnAppPause(bool paused)
        {
        }
    }
}
