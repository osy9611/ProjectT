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
        Active,
        Stopped
    }

    public abstract class SceneBase : MonoBehaviour
    {
        private CancellationTokenSource lifetime;
        protected CancellationToken LifetimeToken { get; private set; }
        protected ResourceScope ResourceScope { get; private set; }
        public SceneState State { get; private set; }

        internal async UniTask EnterAsync(ResourceScope scope, CancellationToken token, params object[] data)
        {
            if (State != SceneState.Created)
                throw new InvalidOperationException($"Scene is {State}.");

            ResourceScope = scope;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            LifetimeToken = lifetime.Token;
            State = SceneState.Entering;

            await OnEnter(0f, LifetimeToken, data).AttachExternalCancellation(LifetimeToken);

            LifetimeToken.ThrowIfCancellationRequested();
            OnInitialize();
            LifetimeToken.ThrowIfCancellationRequested();
            State = SceneState.Active;
        }

        internal void Stop()
        {
            if (State == SceneState.Stopped)
                return;

            bool entered = State != SceneState.Created;
            State = SceneState.Stopped;

            var errors = new List<Exception>();
            try
            {
                lifetime?.Cancel();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }

            if (entered)
            {
                try
                {
                    OnFinalize();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
                try
                {
                    OnExit();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }

            lifetime?.Dispose();
            gameObject.SetActive(false);
            Destroy(gameObject);

            if (errors.Count > 0)
                throw new AggregateException(errors);
        }

        protected T LoadAndGet<T>(string path, ResourceSource source = ResourceSource.Addressables)
        {
            LifetimeToken.ThrowIfCancellationRequested();
            return Global.Resource.LoadAndGet<T>(path, scope: ResourceScope, source: source);
        }

        protected UniTask<T> LoadAndGetAsync<T>(string path, ResourceSource source = ResourceSource.Addressables)
        {
            return Global.Resource.LoadAndGetAsync<T>(path, cancelToken: LifetimeToken, scope: ResourceScope, source: source);
        }

        public virtual UniTask OnEnter(float progress, CancellationToken token, params object[] data) => UniTask.CompletedTask;

        public virtual void OnExit() { }

        public abstract void OnInitialize();

        public abstract void OnFinalize();

        public virtual void OnUpdate(float dt) { }
    }
}
