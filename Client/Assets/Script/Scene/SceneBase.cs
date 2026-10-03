using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT.Addressable;
using UnityEngine;

namespace ProjectT.Scene
{
    public enum SceneState
    {
        Created,
        Entering,
        Prepared,
        Active,
        Stopped
    }

    public abstract class SceneBase : MonoBehaviour
    {
        private CancellationTokenSource lifetime;
        protected internal CancellationToken LifetimeToken { get; private set; }
        protected internal ResourceScope ResourceScope { get; private set; }
        internal event Action Stopped;
        public SceneState State { get; private set; }

        internal async UniTask EnterAsync(ResourceScope scope, CancellationToken token, params object[] data)
        {
            if (State != SceneState.Created)
                throw new InvalidOperationException($"Scene is {State}.");

            ResourceScope = scope;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            LifetimeToken = lifetime.Token;
            State = SceneState.Entering;

            LifetimeToken.ThrowIfCancellationRequested();
            OnInitialize();
            LifetimeToken.ThrowIfCancellationRequested();

            await OnEnter(LifetimeToken, data).AttachExternalCancellation(LifetimeToken);

            LifetimeToken.ThrowIfCancellationRequested();
            State = SceneState.Prepared;
        }

        internal void Activate()
        {
            LifetimeToken.ThrowIfCancellationRequested();

            if (State != SceneState.Prepared)
                throw new InvalidOperationException($"Scene is {State}.");

            OnLoadingAnimEnd();
            LifetimeToken.ThrowIfCancellationRequested();
            State = SceneState.Active;
        }

        internal void Stop()
        {
            if (State == SceneState.Stopped)
                return;

            bool entered = State != SceneState.Created;
            State = SceneState.Stopped;

            List<Exception> errors = null;
            ExecuteInternal(() => lifetime?.Cancel(), ref errors);

            if (entered)
            {
                ExecuteInternal(OnFinalize, ref errors);
                ExecuteInternal(OnExit, ref errors);
            }

            var stopped = Stopped;
            Stopped = null;
            if (stopped != null)
            {
                foreach (Action callback in stopped.GetInvocationList())
                {
                    ExecuteInternal(callback, ref errors);
                }
            }

            StopAllCoroutines();

            lifetime?.Dispose();
            gameObject.SetActive(false);
            Destroy(gameObject);

            if (errors != null)
                throw new AggregateException(errors);
        }

        private static void ExecuteInternal(Action action, ref List<Exception> errors)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                if (errors == null)
                    errors = new List<Exception>();

                errors.Add(error);
            }
        }

        public virtual UniTask OnEnter(CancellationToken token, params object[] data) => UniTask.CompletedTask;

        public virtual void OnExit() { }

        public virtual void OnLoadingAnimEnd() { }

        public abstract void OnInitialize();

        public abstract void OnFinalize();

        public virtual void OnUpdate(float dt) { }
    }
}
