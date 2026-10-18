using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT.Addressable;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using UnityScene = UnityEngine.SceneManagement.Scene;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace ProjectT
{
    public class ResourceManager : ManagerBase
    {
        private readonly Dictionary<string, ResourceBase> resources = new Dictionary<string, ResourceBase>();
        private readonly HashSet<ResourceScope> scopes = new HashSet<ResourceScope>();
        private readonly HashSet<AsyncOperationHandle<SceneInstance>> loadedScenes = new HashSet<AsyncOperationHandle<SceneInstance>>();
        private readonly Dictionary<UnityScene, UniTaskCompletionSource> unloadingScenes = new Dictionary<UnityScene, UniTaskCompletionSource>();
        private ResourceScope sceneScope;
        private ResourceScope appScope;
        internal ResourceScope SceneScope => sceneScope;
        public ResourceScope AppScope => appScope;

        protected override UniTask OnInitializeAsync(CancellationToken token)
        {
            sceneScope = CreateScope();
            appScope = CreateScope();
            return UniTask.CompletedTask;
        }

        protected override void OnShutdown(ShutdownReason reason)
        {
            List<Exception> errors = null;

            // 앱 종료 시에는 Unity가 씬을 정리하며, 마지막 씬 언로드는 Unity가 거부하고 경고만 남긴다.
            if (reason != ShutdownReason.ApplicationExit)
            {
                foreach (var scene in loadedScenes)
                {
                    ErrorCollector.Run(ref errors, scene, handle =>
                    {
                        if (handle.IsValid() && !unloadingScenes.ContainsKey(handle.Result.Scene))
                            ReleaseScene(handle);
                    });
                }
            }

            loadedScenes.Clear();

            foreach (var scope in new List<ResourceScope>(scopes))
            {
                ErrorCollector.Run(ref errors, scope, target => target.Dispose());
            }

            ErrorCollector.ThrowIfAny(errors);
        }

        public ResourceScope CreateScope()
        {
            ThrowIfWorkUnavailableDuringInitInternal();

            var scope = new ResourceScope(ReleaseScope);
            scopes.Add(scope);

            return scope;
        }

        internal void SetSceneScope(ResourceScope scope)
        {
            sceneScope = ResolveScope(scope);
        }

        internal ResourceScope ResolveScope(ResourceScope scope)
        {
            LifetimeToken.ThrowIfCancellationRequested();

            scope = scope ?? sceneScope;

            if (scope == null || !scopes.Contains(scope))
                throw new InvalidOperationException("Resource scope is not active in this manager.");

            scope.Token.ThrowIfCancellationRequested();

            return scope;
        }

        private void ReleaseScope(ResourceScope scope)
        {
            if (!scopes.Remove(scope))
                return;

            List<Exception> errors = null;

            foreach (var pair in new List<KeyValuePair<string, ResourceBase>>(resources))
            {
                if (!pair.Value.Owners.Remove(scope) || pair.Value.Owners.Count > 0)
                    continue;

                resources.Remove(pair.Key);
                ErrorCollector.Run(ref errors, pair.Value, resource => resource.Dispose());
            }

            ErrorCollector.ThrowIfAny(errors);
        }

        // 씬 Scope 풀 정리를 거치지 않으므로 공개 진입점으로 두지 않는다. 씬 전환은 언로드 순서 때문에 SetSceneScope로 직접 교체한다.
        internal void ReleaseAll()
        {
            var previous = sceneScope;
            sceneScope = CreateScope();
            previous.Dispose();
        }

        #region SceneLoad
        internal async UniTask<AsyncOperationHandle<SceneInstance>> LoadSceneAsync(string sceneName, LoadSceneMode sceneMode)
        {
            LifetimeToken.ThrowIfCancellationRequested();
            var handle = Addressables.LoadSceneAsync(sceneName, sceneMode);
            bool retained = false;

            try
            {
                while (!handle.IsDone)
                {
                    await UniTask.Yield(cancellationToken: LifetimeToken);
                }

                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw handle.OperationException ?? new InvalidOperationException($"Scene load failed: {sceneName}");

                LifetimeToken.ThrowIfCancellationRequested();
                loadedScenes.RemoveWhere(loaded => !loaded.IsValid() || !loaded.Result.Scene.isLoaded);
                loadedScenes.Add(handle);
                retained = true;

                return handle;
            }
            finally
            {
                if (!retained)
                    ReleaseScene(handle);
            }
        }

        internal UniTask UnloadAddressableSceneAsync(SceneInstance scene)
        {
            if (unloadingScenes.TryGetValue(scene.Scene, out var pending))
                return pending.Task;

            LifetimeToken.ThrowIfCancellationRequested();

            pending = new UniTaskCompletionSource();
            unloadingScenes.Add(scene.Scene, pending);

            UnloadSceneInternalAsync(scene, pending).Forget(Global.LogException);

            return pending.Task;
        }

        private async UniTask UnloadSceneInternalAsync(SceneInstance scene, UniTaskCompletionSource pending)
        {
            try
            {
                var handle = Addressables.UnloadSceneAsync(scene, autoReleaseHandle: false);
                try
                {
                    // 언로드가 시작되면 완료까지 기다린 뒤 씬 소유를 해제한다.
                    while (!handle.IsDone)
                    {
                        await UniTask.Yield();
                    }

                    if (handle.Status != AsyncOperationStatus.Succeeded)
                        throw handle.OperationException ?? new InvalidOperationException("Scene unload failed.");

                    loadedScenes.RemoveWhere(loaded => !loaded.IsValid() || loaded.Result.Scene == scene.Scene);
                }
                finally
                {
                    if (handle.IsValid())
                        Addressables.Release(handle);
                }

                unloadingScenes.Remove(scene.Scene);
                pending.TrySetResult();
            }
            catch (Exception error)
            {
                unloadingScenes.Remove(scene.Scene);
                pending.TrySetException(error);
            }
        }

        // 시작 씬처럼 Addressables로 로드하지 않은 씬은 진행 중인 언로드를 공유하지 않고 Unity API로 직접 언로드한다.
        internal async UniTask UnloadSceneAsync(UnityScene scene)
        {
            foreach (var handle in loadedScenes)
            {
                if (handle.IsValid() && handle.Result.Scene == scene)
                {
                    var instance = handle.Result;
                    await UnloadAddressableSceneAsync(instance);
                    return;
                }
            }

            var operation = UnitySceneManager.UnloadSceneAsync(scene);
            if (operation == null)
                throw new InvalidOperationException($"Scene unload could not start: {scene.name}");

            while (!operation.isDone)
            {
                await UniTask.Yield();
            }

            if (scene.isLoaded)
                throw new InvalidOperationException($"Scene unload failed: {scene.name}");
        }

        private static void ReleaseScene(AsyncOperationHandle<SceneInstance> handle)
        {
            if (!handle.IsValid())
                return;

            if (!handle.IsDone)
            {
                handle.Completed += ReleaseScene;
                return;
            }

            if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result.Scene.isLoaded)
                UnloadReleasedSceneInternalAsync(handle).Forget(Global.LogException);
            else
                Addressables.Release(handle);
        }

        // 종료나 취소된 로드의 정리라 결과를 기다리는 호출자가 없으므로 언로드 실패는 Forget 경계에서 보고한다.
        private static async UniTask UnloadReleasedSceneInternalAsync(AsyncOperationHandle<SceneInstance> scene)
        {
            var handle = Addressables.UnloadSceneAsync(scene, autoReleaseHandle: false);
            try
            {
                while (!handle.IsDone)
                {
                    await UniTask.Yield();
                }

                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw handle.OperationException ?? new InvalidOperationException("Scene unload failed.");
            }
            finally
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
        }
        #endregion

        public T LoadAndGet<T>(string path, ResourceScope scope = null)
        {
            return LoadResource<Resource<T>>(path, scope).Asset;
        }

        public async UniTask<T> LoadAndGetAsync<T>(string path, CancellationToken cancelToken = default, ResourceScope scope = null)
        {
            var resource = await LoadResourceAsync<Resource<T>>(path, cancelToken, scope);
            return resource.Asset;
        }

        public TResource LoadResource<TResource>(string path, ResourceScope scope = null) where TResource : ResourceBase, new()
        {
            scope = ResolveScope(scope);
            var resource = GetResource<TResource>(path, scope);

            if (!resource.IsDone)
            {
                try
                {
                    resource.WaitForCompletion();
                }
                catch (Exception error)
                {
                    resource.Fail(error);
                }
            }

            scope.Token.ThrowIfCancellationRequested();

            resource.ThrowIfFailed();

            return resource;
        }

        public async UniTask<TResource> LoadResourceAsync<TResource>(string path, CancellationToken cancelToken = default, ResourceScope scope = null) where TResource : ResourceBase, new()
        {
            cancelToken.ThrowIfCancellationRequested();
            scope = ResolveScope(scope);
            var resource = GetResource<TResource>(path, scope);

            if (!resource.IsDone)
            {
                if (cancelToken.CanBeCanceled)
                {
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(scope.Token, cancelToken))
                        await resource.WhenReady.AttachExternalCancellation(linked.Token);
                }
                else
                    await resource.WhenReady.AttachExternalCancellation(scope.Token);
            }

            cancelToken.ThrowIfCancellationRequested();
            scope.Token.ThrowIfCancellationRequested();

            resource.ThrowIfFailed();

            return resource;
        }

        public async UniTask PreloadAsync<T>(string path, CancellationToken cancelToken = default, ResourceScope scope = null)
        {
            await LoadAndGetAsync<T>(path, cancelToken, scope);
        }

        public async UniTask PreloadResourceAsync<TResource>(string path, CancellationToken cancelToken = default, ResourceScope scope = null) where TResource : ResourceBase, new()
        {
            await LoadResourceAsync<TResource>(path, cancelToken, scope);
        }

        public Sprite GetSprite(string path, string name, ResourceScope scope = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Sprite name is empty.", nameof(name));

            return LoadResource<SpriteAtlasResource>(path, scope).GetSprite(name);
        }

        public async UniTask<Sprite> GetSpriteAsync(string path, string name, CancellationToken cancelToken = default, ResourceScope scope = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Sprite name is empty.", nameof(name));

            var resource = await LoadResourceAsync<SpriteAtlasResource>(path, cancelToken, scope);
            return resource.GetSprite(name);
        }

        private TResource GetResource<TResource>(string path, ResourceScope scope) where TResource : ResourceBase, new()
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Asset path is empty.", nameof(path));

            if (resources.TryGetValue(path, out var resource))
            {
                if (!(resource is TResource))
                    throw new InvalidOperationException($"{path} is managed by {resource.GetType().Name}, not {typeof(TResource).Name}.");
            }
            else
                resource = CreateResource<TResource>(path);

            resource.Owners.Add(scope);

            return (TResource)resource;
        }

        private ResourceBase CreateResource<TResource>(string path) where TResource : ResourceBase, new()
        {
            var resource = new TResource();
            resource.Initialize(path, RemoveFailedResource);

            resources.Add(path, resource);

            try
            {
                resource.Observe();
            }
            catch (Exception error)
            {
                resource.Fail(error);
            }

            return resource;
        }

        private void RemoveFailedResource(ResourceBase resource)
        {
            if (resources.TryGetValue(resource.Path, out var current) && ReferenceEquals(current, resource))
                resources.Remove(resource.Path);
        }

        internal async UniTask UnloadUnusedAssetsAsync()
        {
            LifetimeToken.ThrowIfCancellationRequested();

            var operation = Resources.UnloadUnusedAssets();

            while (!operation.isDone)
            {
                await UniTask.Yield(cancellationToken: LifetimeToken);
            }

            LifetimeToken.ThrowIfCancellationRequested();
        }
    }
}
