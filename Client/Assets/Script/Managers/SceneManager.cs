using System;
using System.Collections.Generic;
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

        private ResourceManager resource;
        private ClientLocalStorageManager localStorage;
        private readonly Dictionary<Type, SceneEntry> scenes = new Dictionary<Type, SceneEntry>();
        private SceneEntry currentScene;
        private bool isTransitioning;
        private int transitionVersion;
        private UnityEngine.SceneManagement.Scene transitionScene;
        public bool IsTransitioning => isTransitioning;
        public SceneBase CurrentScene => currentScene?.Scene != null && currentScene.Scene.State == SceneState.Active ? currentScene.Scene : null;
        public string PrevSceneName { get; private set; } = string.Empty;

        protected override UniTask OnInitializeAsync(CancellationToken token)
        {
            resource = Context.Get<ResourceManager>();
            localStorage = Context.Get<ClientLocalStorageManager>();
            CreateRootObject(Context.Root, "SceneRoot");
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

        public void Transition<T>(string resourceName, float startLoadingGage, float fadeOutDuration, LoadSceneMode loadSceneMode, Action<eSceneTransitionErrorCode> completed, params object[] data) where T : SceneBase
        {
            Transition<T>(resourceName, loadSceneMode, completed, data);
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
                await TransitionCoreAsync<T>(path, mode, data);
            }
            finally
            {
                isTransitioning = false;
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

        private async UniTask TransitionCoreAsync<T>(string path, LoadSceneMode mode, object[] data) where T : SceneBase
        {
            bool single = mode == LoadSceneMode.Single;
            var previousScope = resource.SceneScope;
            var previousScenes = new List<SceneEntry>(scenes.Values);
            var entry = new SceneEntry { Scope = path == null ? previousScope : resource.CreateScope(), IsAdditive = !single };
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
                        resource.SetSceneScope(entry.Scope);
                        previousReleased = true;

                        var errors = new List<Exception>();
                        foreach (var previous in previousScenes)
                        {
                            try
                            {
                                previous.Scope.Dispose();
                            }
                            catch (Exception error)
                            {
                                errors.Add(error);
                            }
                        }
                        try
                        {
                            previousScope.Dispose();
                        }
                        catch (Exception error)
                        {
                            errors.Add(error);
                        }

                        if (errors.Count > 0)
                            throw new AggregateException(errors);

                        await resource.UnloadUnusedAssetsAsync();
                    }
                }

                if (path != null)
                {
                    var handle = await resource.LoadSceneAsync(path, mode, null);
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

                LifetimeToken.ThrowIfCancellationRequested();

                if (single)
                    currentScene = entry;
            }
            catch
            {
                if (entry.Scene != null)
                {
                    try
                    {
                        entry.Scene.Stop();
                    }
                    catch (Exception error)
                    {
                        Global.LogException(error);
                    }
                }

                if (State == ManagerState.Ready)
                {
                    if (!loaded)
                    {
                        if (single)
                            resource.SetSceneScope(previousReleased ? resource.CreateScope() : previousScope);

                        if (!ReferenceEquals(entry.Scope, previousScope))
                            entry.Scope.Dispose();
                    }
                    else if (!single)
                    {
                        try
                        {
                            await CloseEntryAsync(typeof(T), entry);
                        }
                        catch (Exception error)
                        {
                            Global.LogException(error);
                        }
                    }
                    else if (!scenes.ContainsKey(typeof(T)))
                        scenes.Add(typeof(T), entry);
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
                await resource.UnLoadSceneAsync(scene);
                LifetimeToken.ThrowIfCancellationRequested();
            }
        }

        private void StopScenes()
        {
            var errors = new List<Exception>();

            foreach (var entry in new List<SceneEntry>(scenes.Values))
            {
                if (ReferenceEquals(entry, currentScene) || entry.Scene == null)
                    continue;

                try
                {
                    entry.Scene.Stop();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }

            if (currentScene?.Scene != null)
            {
                try
                {
                    currentScene.Scene.Stop();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
            if (errors.Count > 0)
                throw new AggregateException(errors);
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
                await CloseEntryAsync(typeof(T), entry);
            }
            finally
            {
                isTransitioning = false;
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

            await UniTask.NextFrame();

            if (State != ManagerState.Ready)
                return;

            if (entry.IsAddressable && entry.Instance.Scene.isLoaded)
                await resource.UnLoadSceneAsync(entry.Instance, null);

            scenes.Remove(type);
            entry.Scope.Dispose();

            if (stopError != null)
                throw stopError;
        }

        public void GoTitle()
        {
            Transition<TitleScene>("TitleScene", LoadSceneMode.Single, result =>
            {
                if (result == eSceneTransitionErrorCode.Success)
                    localStorage.LoadAllData();
            });
        }
    }
}
