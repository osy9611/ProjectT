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

    public abstract class ManagerBase
    {
        public string Name => GetType().Name;
        public ManagerState State { get; private set; } = ManagerState.Created;
        protected CancellationToken LifetimeToken { get; private set; }
        protected Transform rootObject;
        public Transform RootObject => rootObject;

        internal async UniTask InitializeAsync(CancellationToken token)
        {
            if (State != ManagerState.Created)
                throw new InvalidOperationException($"{Name} cannot initialize from {State}.");

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

        protected void CreateRootObject(string name)
        {
            if (rootObject == null)
                rootObject = new GameObject(name).transform;
            
            rootObject.SetParent(Global.Instance.transform, false);
        }

        private void DestroyRootObject()
        {
            if (rootObject == null)
                return;

            var root = rootObject.gameObject;
            rootObject = null;
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
