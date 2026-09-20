using System;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using ProjectT.Addressable;
using System.Threading;

namespace ProjectT
{
    public class ResourceManager : ManagerBase
    {
        private readonly struct ResourceKey : IEquatable<ResourceKey>
        {
            public readonly ResourceSource Source;
            public readonly string Path;

            public ResourceKey(ResourceSource source, string path)
            {
                Source = source;
                Path = path;
            }

            public bool Equals(ResourceKey other)
            {
                return Source == other.Source && string.Equals(Path, other.Path, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is ResourceKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Source, Path);
            }
        }

        private readonly Dictionary<ResourceKey, IResource> datas = new Dictionary<ResourceKey, IResource>();
        private readonly HashSet<ResourceScope> scopes = new HashSet<ResourceScope>();
        private readonly HashSet<ResourceRequest> pendingResources = new HashSet<ResourceRequest>();
        private ResourceScope sceneScope;
        private ResourceScope appScope;
        internal ResourceScope SceneScope => sceneScope;

        internal void SetSceneScope(ResourceScope scope)
        {
            sceneScope = ResolveScope(false, scope);
        }

        protected override UniTask OnInitializeAsync(CancellationToken token)
        {
            sceneScope = CreateScope();
            appScope = CreateScope();
            return UniTask.CompletedTask;
        }

        public ResourceScope CreateScope()
        {
            LifetimeToken.ThrowIfCancellationRequested();

            if (State != ManagerState.Initializing && State != ManagerState.Ready)
                throw new InvalidOperationException($"ResourceManager is {State}.");

            var scope = new ResourceScope(ReleaseScope);
            scopes.Add(scope);

            return scope;
        }

        protected override void OnShutdown(ShutdownReason reason)
        {
            var errors = new List<Exception>();
            foreach (var scene in loadedScenes)
            {
                try
                {
                    if (scene.IsValid() && !unloadingScenes.ContainsKey(scene.Result.Scene))
                        ReleaseScene(scene);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }

            loadedScenes.Clear();

            foreach (var scope in new List<ResourceScope>(scopes))
            {
                try
                {
                    scope.Dispose();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
            if (errors.Count > 0)
                throw new AggregateException(errors);
        }


        private readonly HashSet<AsyncOperationHandle<SceneInstance>> loadedScenes = new HashSet<AsyncOperationHandle<SceneInstance>>();
        private readonly Dictionary<UnityEngine.SceneManagement.Scene, UniTaskCompletionSource<int>> unloadingScenes = new Dictionary<UnityEngine.SceneManagement.Scene, UniTaskCompletionSource<int>>();

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
                Addressables.UnloadSceneAsync(handle);
            else if (handle.IsValid())
                Addressables.Release(handle);
        }

        #region SceneLoad
        public async UniTask<AsyncOperationHandle<SceneInstance>> LoadSceneAsync(string sceneName, UnityEngine.SceneManagement.LoadSceneMode sceneMode, Action<float> OnLoadingProgressAction)
        {
            LifetimeToken.ThrowIfCancellationRequested();
            var handle = Addressables.LoadSceneAsync(sceneName, sceneMode);
            bool retained = false;

            try
            {
                while (!handle.IsDone)
                {
                    OnLoadingProgressAction?.Invoke(handle.PercentComplete);
                    await UniTask.Yield(cancellationToken: LifetimeToken);
                }

                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw handle.OperationException ?? new InvalidOperationException($"Scene load failed: {sceneName}");

                LifetimeToken.ThrowIfCancellationRequested();
                loadedScenes.RemoveWhere(scene => !scene.IsValid() || !scene.Result.Scene.isLoaded);
                OnLoadingProgressAction?.Invoke(1f);
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

        public UniTask<int> UnLoadSceneAsync(SceneInstance scene, Action<float> OnLoadingProgressAction)
        {
            if (unloadingScenes.TryGetValue(scene.Scene, out var pending))
                return pending.Task;

            LifetimeToken.ThrowIfCancellationRequested();

            pending = new UniTaskCompletionSource<int>();
            unloadingScenes.Add(scene.Scene, pending);

            UnloadSceneInternalAsync(scene, OnLoadingProgressAction, pending).Forget(Global.LogException);

            return pending.Task;
        }

        private async UniTask UnloadSceneInternalAsync(SceneInstance scene, Action<float> OnLoadingProgressAction, UniTaskCompletionSource<int> pending)
        {
            try
            {
                int sceneIndex = scene.Scene.buildIndex;
                var handle = Addressables.UnloadSceneAsync(scene, autoReleaseHandle: false);
                try
                {
                    // Once unloading starts, settle it before dropping scene ownership.
                    while (!handle.IsDone)
                    {
                        OnLoadingProgressAction?.Invoke(handle.PercentComplete);
                        await UniTask.Yield();
                    }

                    if (handle.Status != AsyncOperationStatus.Succeeded)
                        throw handle.OperationException ?? new InvalidOperationException("Scene unload failed.");

                    loadedScenes.RemoveWhere(h => !h.IsValid() || (h.IsDone && h.Result.Scene == scene.Scene));
                }
                finally
                {
                    if (handle.IsValid())
                        Addressables.Release(handle);
                }

                OnLoadingProgressAction?.Invoke(1f);

                unloadingScenes.Remove(scene.Scene);
                pending.TrySetResult(sceneIndex);
            }
            catch (Exception error)
            {
                unloadingScenes.Remove(scene.Scene);
                pending.TrySetException(error);
            }
        }

        #endregion

        public T LoadAndGet<T>(string path, bool dontDestroy = false, ResourceScope scope = null, ResourceSource source = ResourceSource.Addressables)
        {
            return LoadResource<Resource<T>>(path, dontDestroy, scope, source).Asset;
        }

        public async UniTask<T> LoadAndGetAsync<T>(string path, bool dontDestroy = false, CancellationToken cancelToken = default, ResourceScope scope = null, ResourceSource source = ResourceSource.Addressables)
        {
            var resource = await LoadResourceAsync<Resource<T>>(path, dontDestroy, cancelToken, scope, source);
            return resource.Asset;
        }

        public TResource LoadResource<TResource>(string path, bool dontDestroy = false, ResourceScope scope = null, ResourceSource source = ResourceSource.Addressables) where TResource : IResource, new()
        {
            scope = ResolveScope(dontDestroy, scope);
            var resource = GetResource<TResource>(path, scope, source, false);

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

            resource.GetResult<object>();

            return resource;
        }

        public async UniTask<TResource> LoadResourceAsync<TResource>(string path, bool dontDestroy = false, CancellationToken cancelToken = default, ResourceScope scope = null, ResourceSource source = ResourceSource.Addressables) where TResource : IResource, new()
        {
            cancelToken.ThrowIfCancellationRequested();
            scope = ResolveScope(dontDestroy, scope);
            var resource = GetResource<TResource>(path, scope, source, true);

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

            resource.GetResult<object>();

            return resource;
        }

        public void LoadAsset<T>(string path, Action<T> callback, bool dontDestroy = false, ResourceScope scope = null, ResourceSource source = ResourceSource.Addressables)
        {
            var result = LoadAndGet<T>(path, dontDestroy, scope, source);
            callback?.Invoke(result);
        }

        public async UniTask LoadAssetAsync<T>(string path, Action<T> callback, bool dontDestroy = false, CancellationToken cancelToken = default, ResourceScope scope = null, ResourceSource source = ResourceSource.Addressables)
        {
            var result = await LoadAndGetAsync<T>(path, dontDestroy, cancelToken, scope, source);
            callback?.Invoke(result);
        }

        public async UniTask PreloadAsync<T>(string path, bool dontDestroy = false, CancellationToken cancelToken = default, ResourceScope scope = null, ResourceSource source = ResourceSource.Addressables)
        {
            await LoadAndGetAsync<T>(path, dontDestroy, cancelToken, scope, source);
        }

        public async UniTask PreloadResourceAsync<TResource>(string path, bool dontDestroy = false, CancellationToken cancelToken = default, ResourceScope scope = null, ResourceSource source = ResourceSource.Addressables) where TResource : IResource, new()
        {
            await LoadResourceAsync<TResource>(path, dontDestroy, cancelToken, scope, source);
        }

        public Sprite GetSprite(string path, string name, bool dontDestroy = false, ResourceScope scope = null, ResourceSource source = ResourceSource.Addressables)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Sprite name is empty.", nameof(name));

            return LoadResource<SpriteAtlasResource>(path, dontDestroy, scope, source).GetSprite(name);
        }

        public async UniTask<Sprite> GetSpriteAsync(string path, string name, bool dontDestroy = false, CancellationToken cancelToken = default, ResourceScope scope = null, ResourceSource source = ResourceSource.Addressables)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Sprite name is empty.", nameof(name));

            var resource = await LoadResourceAsync<SpriteAtlasResource>(path, dontDestroy, cancelToken, scope, source);
            return resource.GetSprite(name);
        }


        internal ResourceScope ResolveScope(bool dontDestroy, ResourceScope scope)
        {
            LifetimeToken.ThrowIfCancellationRequested();

            if (scope != null && dontDestroy)
                throw new ArgumentException("Choose either an explicit scope or dontDestroy.");

            scope = scope ?? (dontDestroy ? appScope : sceneScope);

            if (scope == null || !scopes.Contains(scope))
                throw new InvalidOperationException("Resource scope is not active in this manager.");

            scope.Token.ThrowIfCancellationRequested();

            return scope;
        }

        private TResource GetResource<TResource>(string path, ResourceScope scope, ResourceSource source, bool asynchronous) where TResource : IResource, new()
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Asset path is empty.", nameof(path));

            if (source != ResourceSource.Addressables && source != ResourceSource.Resources)
                throw new ArgumentOutOfRangeException(nameof(source));

            var key = new ResourceKey(source, path);
            if (datas.TryGetValue(key, out var resource))
            {
                if (!(resource is TResource))
                    throw new InvalidOperationException($"{path} is managed by {resource.GetType().Name}, not {typeof(TResource).Name}.");

                resource.Owners.Add(scope);

                return (TResource)resource;
            }

            resource = new TResource();
            if (source == ResourceSource.Resources && !typeof(UnityEngine.Object).IsAssignableFrom(resource.AssetType))
                throw new ArgumentException("Resources requires a UnityEngine.Object type.");
            resource.Initialize(path, source, asynchronous, RemoveFailedResource);

            if (resource.Request != null && !resource.Request.isDone)
            {
                pendingResources.Add(resource.Request);
                resource.Request.completed += OnResourceCompleted;
            }

            resource.Owners.Add(scope);

            datas.Add(key, resource);

            try
            {
                resource.Observe();
            }
            catch (Exception error)
            {
                resource.Fail(error);
            }

            return (TResource)resource;
        }

        private void RemoveFailedResource(IResource resource)
        {
            var key = new ResourceKey(resource.Source, resource.Path);

            if (datas.TryGetValue(key, out var current) && ReferenceEquals(current, resource))
                datas.Remove(key);
        }

        private void ReleaseScope(ResourceScope scope)
        {
            if (!scopes.Remove(scope))
                return;

            var errors = new List<Exception>();

            foreach (var pair in new List<KeyValuePair<ResourceKey, IResource>>(datas))
            {
                if (!pair.Value.Owners.Remove(scope) || pair.Value.Owners.Count > 0)
                    continue;

                datas.Remove(pair.Key);

                try
                {
                    pair.Value.Dispose();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }

            if (errors.Count > 0)
                throw new AggregateException(errors);
        }

        private void OnResourceCompleted(AsyncOperation operation)
        {
            operation.completed -= OnResourceCompleted;
            pendingResources.Remove((ResourceRequest)operation);
        }

        internal async UniTask UnloadUnusedAssetsAsync()
        {
            while (pendingResources.Count > 0)
            {
                await UniTask.Yield(cancellationToken: LifetimeToken);
            }

            LifetimeToken.ThrowIfCancellationRequested();

            var operation = Resources.UnloadUnusedAssets();

            while (!operation.isDone)
            {
                await UniTask.Yield(cancellationToken: LifetimeToken);
            }

            LifetimeToken.ThrowIfCancellationRequested();
        }

        internal async UniTask UnLoadSceneAsync(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var handle in loadedScenes)
            {
                if (handle.IsValid() && handle.Result.Scene == scene)
                {
                    var instance = handle.Result;
                    await UnLoadSceneAsync(instance, null);
                    return;
                }
            }

            var operation = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
            if (operation == null)
                throw new InvalidOperationException($"Scene unload could not start: {scene.name}");

            while (!operation.isDone)
            {
                await UniTask.Yield();
            }

            if (scene.isLoaded)
                throw new InvalidOperationException($"Scene unload failed: {scene.name}");
        }

        // 씬 전환은 언로드 순서 때문에 SetSceneScope로 직접 교체한다. 그 외 기본 씬 리소스 일괄 반환은 이 진입점을 사용한다.
        public void ReleaseAll()
        {
            var previous = sceneScope;
            sceneScope = CreateScope();
            previous.Dispose();
        }
    }
}
