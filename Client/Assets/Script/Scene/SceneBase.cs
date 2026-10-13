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

            OnActivate();
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
            ErrorCollector.Run(ref errors, lifetime, source => source?.Cancel());

            if (entered)
            {
                ErrorCollector.Run(ref errors, this, scene => scene.OnFinalize());
            }

            StopAllCoroutines();

            lifetime?.Dispose();
            gameObject.SetActive(false);
            Destroy(gameObject);

            ErrorCollector.ThrowIfAny(errors);
        }

        protected virtual UniTask OnEnter(CancellationToken token, params object[] data) => UniTask.CompletedTask;

        protected virtual void OnActivate() { }

        protected abstract void OnInitialize();

        protected abstract void OnFinalize();

        protected internal virtual void OnUpdate(float dt) { }
    }
}
