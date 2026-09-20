using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT.Addressable;
using ProjectT.Scene;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace ProjectT
{
    public enum eSceneTransitionErrorCode
    {
        Success = 0,
        Failure = 1
    }

    public class SceneManager : ManagerBase
    {
        private sealed class SceneEntry
        {
            public SceneBase Scene;
            public ResourceScope Scope;
            public SceneInstance Instance;
            public bool IsAddressable;
            public bool IsAdditive;
        }

        private readonly Dictionary<Type, SceneEntry> scenes = new Dictionary<Type, SceneEntry>();
        private SceneEntry currentScene;
        private SceneTransitionView transitionView;
        private bool isTransitioning;
        private bool inputAllowed = true;
        private int transitionVersion;
        private UnityEngine.SceneManagement.Scene transitionScene;
        public bool IsTransitioning => isTransitioning;
        public bool IsInputAllowed => inputAllowed;
        public SceneBase CurrentScene => currentScene?.Scene != null && currentScene.Scene.State == SceneState.Active ? currentScene.Scene : null;
        public string PrevSceneName { get; private set; } = string.Empty;

        protected override UniTask OnInitializeAsync(CancellationToken token)
        {
            CreateRootObject("SceneRoot");

            transitionView = new GameObject("SceneTransitionView").AddComponent<SceneTransitionView>();
            transitionView.transform.SetParent(RootObject, false);
            transitionView.Initialize();
            return UniTask.CompletedTask;
        }

        protected override void OnShutdown(ShutdownReason reason)
        {
            try
            {
                StopScenes();
            }
            finally
            {
                scenes.Clear();
                currentScene = null;
                transitionView = null;
            }
        }

        public override void OnUpdate(float dt)
        {
            if (isTransitioning)
                return;

            int version = transitionVersion;

            foreach (var entry in scenes.Values)
            {
                if (entry.Scene != null && entry.Scene.State == SceneState.Active)
                    entry.Scene.OnUpdate(dt);

                if (version != transitionVersion || State != ManagerState.Ready)
                    break;
            }
        }

        public T GetScene<T>() where T : SceneBase
        {
            if (scenes.TryGetValue(typeof(T), out var entry) && entry.Scene != null && entry.Scene.State == SceneState.Active)
                return entry.Scene as T;

            return null;
        }

        public bool IsHaveScene<T>() where T : SceneBase
        {
            return GetScene<T>() != null;
        }

        public void Transition<T>(string resourceName, LoadSceneMode loadSceneMode, Action<eSceneTransitionErrorCode> completed = null, params object[] data) where T : SceneBase
        {
            NotifyTransitionAsync<T>(resourceName, loadSceneMode, completed, data).Forget(Global.LogException);
        }

        private async UniTask NotifyTransitionAsync<T>(string resourceName, LoadSceneMode mode, Action<eSceneTransitionErrorCode> completed, object[] data) where T : SceneBase
        {
            var result = eSceneTransitionErrorCode.Failure;

            try
            {
                await TransitionAsync<T>(resourceName, mode, data);
                result = eSceneTransitionErrorCode.Success;
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                Global.LogException(error);
            }

            completed?.Invoke(result);
        }

        public async UniTask TransitionAsync<T>(string resourceName, LoadSceneMode mode, params object[] data) where T : SceneBase
        {
            if (State != ManagerState.Ready || isTransitioning)
                throw new InvalidOperationException("Scene transition is unavailable.");

            if (mode != LoadSceneMode.Single && mode != LoadSceneMode.Additive)
                throw new ArgumentOutOfRangeException(nameof(mode));

            if (scenes.TryGetValue(typeof(T), out var existing) && (mode == LoadSceneMode.Additive || existing.Scene != null && existing.Scene.State == SceneState.Active))
                throw new InvalidOperationException($"Scene already registered: {typeof(T).Name}");

            if (mode == LoadSceneMode.Additive && CurrentScene == null)
                throw new InvalidOperationException("An additive scene requires an active primary scene.");

            string path = ResolvePath(resourceName, mode);
            transitionVersion++;
            isTransitioning = true;

            try
            {
                SetInputAllowed(false);
                await transitionView.ShowAsync(LifetimeToken).AttachExternalCancellation(LifetimeToken);
                await TransitionInternalAsync<T>(path, mode, data);
            }
            finally
            {
                EndTransition();
            }
        }

        private string ResolvePath(string resourceName, LoadSceneMode mode)
        {
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            if (mode == LoadSceneMode.Single && scenes.Count == 0 && active.IsValid() && active.isLoaded
                && (string.IsNullOrEmpty(resourceName) || string.Equals(active.name, resourceName, StringComparison.OrdinalIgnoreCase)))
                return null;

            if (string.IsNullOrWhiteSpace(resourceName))
                throw new ArgumentException("Scene table key is empty.", nameof(resourceName));

            var info = Global.Table.SceneDataInfos.Get(resourceName);

            if (info == null || string.IsNullOrWhiteSpace(info.Path))
                throw new ArgumentException($"Scene table entry is missing: {resourceName}");

            return info.Path;
        }

        private async UniTask TransitionInternalAsync<T>(string path, LoadSceneMode mode, object[] data) where T : SceneBase
        {
            bool single = mode == LoadSceneMode.Single;
            var previousScope = Global.Resource.SceneScope;
            var previousScenes = new List<SceneEntry>(scenes.Values);
            var entry = new SceneEntry { Scope = path == null ? previousScope : Global.Resource.CreateScope(), IsAdditive = !single };
            bool loaded = false;
            bool previousReleased = false;

            try
            {
                if (single)
                {
                    PrevSceneName = currentScene?.Scene != null ? currentScene.Scene.GetType().ToString() : PrevSceneName;

                    StopScenes();

                    await UniTask.NextFrame(cancellationToken: LifetimeToken);

                    if (path != null)
                    {
                        await UnloadPreviousScenesAsync();

                        LifetimeToken.ThrowIfCancellationRequested();
                        scenes.Clear();
                        currentScene = null;
                        Global.Resource.SetSceneScope(entry.Scope);
                        previousReleased = true;

                        List<Exception> errors = null;
                        foreach (var previous in previousScenes)
                        {
                            ErrorCollector.Run(ref errors, previous.Scope, scope => scope.Dispose());
                        }

                        ErrorCollector.Run(ref errors, previousScope, scope => scope.Dispose());
                        ErrorCollector.ThrowIfAny(errors);

                        await Global.Resource.UnloadUnusedAssetsAsync();
                    }
                }

                if (path != null)
                {
                    var handle = await Global.Resource.LoadSceneAsync(path, mode, null);
                    entry.Instance = handle.Result;
                    entry.IsAddressable = true;
                }

                LifetimeToken.ThrowIfCancellationRequested();
                loaded = true;

                var root = new GameObject(typeof(T).Name);
                root.transform.SetParent(RootObject, false);

                entry.Scene = root.AddComponent<T>();

                scenes.Add(typeof(T), entry);

                await entry.Scene.EnterAsync(entry.Scope, LifetimeToken, data);
                await transitionView.HideAsync(LifetimeToken).AttachExternalCancellation(LifetimeToken);

                entry.Scene.Activate();

                if (single)
                    currentScene = entry;
            }
            catch (Exception error)
            {
                List<Exception> errors = null;

                if (entry.Scene != null)
                    ErrorCollector.Run(ref errors, entry.Scene, scene => scene.Stop());

                // 종료 취소 중에는 새 Scope를 만들거나 언로드를 시작하지 않고 매니저 종료 정리에 맡긴다.
                if (!LifetimeToken.IsCancellationRequested)
                {
                    if (!loaded)
                    {
                        if (single)
                            Global.Resource.SetSceneScope(previousReleased ? Global.Resource.CreateScope() : previousScope);

                        if (!ReferenceEquals(entry.Scope, previousScope))
                            ErrorCollector.Run(ref errors, entry.Scope, scope => scope.Dispose());
                    }
                    else if (!single)
                    {
                        try
                        {
                            await CloseEntryAsync(typeof(T), entry);
                        }
                        catch (Exception closeError)
                        {
                            errors = errors ?? new List<Exception>();
                            errors.Add(closeError);
                        }
                    }
                    else if (!scenes.ContainsKey(typeof(T)))
                        scenes.Add(typeof(T), entry);
                }

                if (errors != null)
                {
                    errors.Insert(0, error);
                    throw new AggregateException(errors);
                }

                throw;
            }
        }

        private async UniTask UnloadPreviousScenesAsync()
        {
            if (!transitionScene.IsValid() || !transitionScene.isLoaded)
                transitionScene = UnityEngine.SceneManagement.SceneManager.CreateScene("SceneTransition");

            UnityEngine.SceneManagement.SceneManager.SetActiveScene(transitionScene);

            var previous = new List<UnityEngine.SceneManagement.Scene>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; ++i)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene != transitionScene && scene.isLoaded)
                    previous.Add(scene);
            }

            foreach (var scene in previous)
            {
                await Global.Resource.UnLoadSceneAsync(scene);
                LifetimeToken.ThrowIfCancellationRequested();
            }
        }

        private void StopScenes()
        {
            List<Exception> errors = null;

            foreach (var entry in new List<SceneEntry>(scenes.Values))
            {
                if (ReferenceEquals(entry, currentScene) || entry.Scene == null)
                    continue;

                ErrorCollector.Run(ref errors, entry.Scene, scene => scene.Stop());
            }

            if (currentScene?.Scene != null)
                ErrorCollector.Run(ref errors, currentScene.Scene, scene => scene.Stop());

            ErrorCollector.ThrowIfAny(errors);
        }

        private void SetInputAllowed(bool allowed)
        {
            inputAllowed = allowed;
            Global.UI.SetInputAllowed(allowed);

            foreach (var entry in scenes.Values)
            {
                if (entry.Scene != null)
                    entry.Scene.SetInputAllowed(allowed);
            }
        }

        private void EndTransition()
        {
            isTransitioning = false;

            if (LifetimeToken.IsCancellationRequested)
                return;

            transitionView.ResetView();
            SetInputAllowed(true);
        }

        public async UniTask UnloadAdditiveAsync<T>() where T : SceneBase
        {
            if (State != ManagerState.Ready || isTransitioning)
                throw new InvalidOperationException("Scene transition is unavailable.");

            if (!scenes.TryGetValue(typeof(T), out var entry) || !entry.IsAdditive)
                throw new InvalidOperationException("The requested additive scene is not registered.");

            transitionVersion++;
            isTransitioning = true;

            try
            {
                SetInputAllowed(false);
                await CloseEntryAsync(typeof(T), entry);
            }
            finally
            {
                EndTransition();
            }
        }

        private async UniTask CloseEntryAsync(Type type, SceneEntry entry)
        {
            Exception stopError = null;

            try
            {
                entry.Scene?.Stop();
            }
            catch (Exception error)
            {
                stopError = error;
            }

            await UniTask.NextFrame(cancellationToken: LifetimeToken);

            if (entry.IsAddressable && entry.Instance.Scene.isLoaded)
                await Global.Resource.UnLoadSceneAsync(entry.Instance, null);

            scenes.Remove(type);
            entry.Scope.Dispose();

            if (stopError != null)
                ExceptionDispatchInfo.Capture(stopError).Throw();
        }
    }
}
