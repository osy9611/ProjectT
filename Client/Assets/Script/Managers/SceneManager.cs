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
            public bool IsActive => Scene != null && Scene.State == SceneState.Active;
        }

        private readonly Dictionary<Type, SceneEntry> scenes = new Dictionary<Type, SceneEntry>();
        private SceneEntry currentEntry;
        private SceneTransitionView transitionView;
        private bool isTransitioning;
        private bool inputAllowed = true;
        private int transitionVersion;
        private UnityEngine.SceneManagement.Scene transitionScene;
        public bool IsTransitioning => isTransitioning;
        public bool IsInputAllowed => inputAllowed;
        public SceneBase CurrentScene => currentEntry != null && currentEntry.IsActive ? currentEntry.Scene : null;
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
                currentEntry = null;
                transitionView = null;
            }
        }

        public override void OnUpdate(float dt)
        {
            if (isTransitioning)
                return;

            int version = transitionVersion;

            // 씬 OnUpdate에서 전환이 시작되거나 매니저가 종료되면 남은 씬은 갱신하지 않는다.
            foreach (var entry in scenes.Values)
            {
                if (entry.IsActive)
                    entry.Scene.OnUpdate(dt);

                if (version != transitionVersion || State != ManagerState.Ready)
                    break;
            }
        }

        public T GetScene<T>() where T : SceneBase
        {
            if (scenes.TryGetValue(typeof(T), out var entry) && entry.IsActive)
                return entry.Scene as T;

            return null;
        }

        public bool IsHaveScene<T>() where T : SceneBase
        {
            return GetScene<T>() != null;
        }

        public void Transition<T>(string resourceName, LoadSceneMode loadSceneMode, Action<eSceneTransitionErrorCode> completed = null, params object[] data) where T : SceneBase
        {
            TransitionWithCallbackAsync<T>(resourceName, loadSceneMode, completed, data).Forget(Global.LogException);
        }

        private async UniTask TransitionWithCallbackAsync<T>(string resourceName, LoadSceneMode mode, Action<eSceneTransitionErrorCode> completed, object[] data) where T : SceneBase
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
            ThrowIfTransitionUnavailable();

            if (mode != LoadSceneMode.Single && mode != LoadSceneMode.Additive)
                throw new ArgumentOutOfRangeException(nameof(mode));

            if (scenes.TryGetValue(typeof(T), out var existing) && (mode == LoadSceneMode.Additive || existing.IsActive))
                throw new InvalidOperationException($"Scene already registered: {typeof(T).Name}");

            if (mode == LoadSceneMode.Additive && CurrentScene == null)
                throw new InvalidOperationException("An additive scene requires an active primary scene.");

            string path = ResolvePath(resourceName, mode);

            await RunTransitionAsync(async () =>
            {
                await transitionView.ShowAsync(LifetimeToken).AttachExternalCancellation(LifetimeToken);
                await TransitionInternalAsync<T>(path, mode, data);
            });
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
                    PrevSceneName = currentEntry?.Scene != null ? currentEntry.Scene.GetType().ToString() : PrevSceneName;

                    StopScenes();

                    // 씬 오브젝트를 참조한 UI와 씬 Scope 클립을 재생하는 공간 음원이 남지 않도록 언로드와 Scope 반환보다 먼저 정리한다.
                    Global.UI.ClearTransientWidgets();
                    Global.Sound.StopAllSpatialSounds();

                    await UniTask.NextFrame(cancellationToken: LifetimeToken);

                    if (path != null)
                    {
                        await UnloadPreviousScenesAsync();

                        LifetimeToken.ThrowIfCancellationRequested();
                        scenes.Clear();
                        currentEntry = null;
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
                    var handle = await Global.Resource.LoadSceneAsync(path, mode);
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
                    currentEntry = entry;
            }
            catch (Exception error)
            {
                List<Exception> errors = null;

                if (entry.Scene != null)
                    StopEntryInternal(entry, ref errors);

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
                await Global.Resource.UnloadSceneAsync(scene);
                LifetimeToken.ThrowIfCancellationRequested();
            }
        }

        private void StopScenes()
        {
            List<Exception> errors = null;

            foreach (var entry in new List<SceneEntry>(scenes.Values))
            {
                if (ReferenceEquals(entry, currentEntry) || entry.Scene == null)
                    continue;

                StopEntryInternal(entry, ref errors);
            }

            if (currentEntry?.Scene != null)
                StopEntryInternal(currentEntry, ref errors);

            ErrorCollector.ThrowIfAny(errors);
        }

        // 씬 Scope는 언로드 뒤에 해제되므로 그 사이 씬 Scope 풀에서 빌린 객체가 동작하지 않도록 정지 직후 풀을 정리한다.
        private void StopEntryInternal(SceneEntry entry, ref List<Exception> errors)
        {
            if (entry.Scene != null)
                ErrorCollector.Run(ref errors, entry.Scene, scene => scene.Stop());

            ErrorCollector.Run(ref errors, entry.Scope, scope => Global.Pool.ClearScope(scope));
        }

        private void SetInputAllowed(bool allowed)
        {
            int version = transitionVersion;
            inputAllowed = allowed;
            Global.UI.SetInputAllowed(allowed);

            // UI 입력 변경 콜백에서 새 전환이 시작되거나 매니저가 종료되면 그 상태를 덮어쓰지 않도록 Input 갱신을 건너뛴다.
            if (version != transitionVersion || inputAllowed != allowed || State != ManagerState.Ready)
                return;

            Global.Input.SetInputAllowed(allowed);
        }

        private void ThrowIfTransitionUnavailable()
        {
            if (State != ManagerState.Ready || isTransitioning)
                throw new InvalidOperationException("Scene transition is unavailable.");
        }

        private async UniTask RunTransitionAsync(Func<UniTask> transition)
        {
            transitionVersion++;
            isTransitioning = true;

            try
            {
                SetInputAllowed(false);
                await transition();
            }
            finally
            {
                EndTransition();
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
            ThrowIfTransitionUnavailable();

            if (!scenes.TryGetValue(typeof(T), out var entry) || !entry.IsAdditive)
                throw new InvalidOperationException("The requested additive scene is not registered.");

            await RunTransitionAsync(() => CloseEntryAsync(typeof(T), entry));
        }

        private async UniTask CloseEntryAsync(Type type, SceneEntry entry)
        {
            List<Exception> stopErrors = null;
            StopEntryInternal(entry, ref stopErrors);

            await UniTask.NextFrame(cancellationToken: LifetimeToken);

            if (entry.IsAddressable && entry.Instance.Scene.isLoaded)
                await Global.Resource.UnloadSceneAsync(entry.Instance);

            scenes.Remove(type);
            entry.Scope.Dispose();

            ErrorCollector.ThrowIfAny(stopErrors);
        }
    }
}
