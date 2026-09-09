using System;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using ProjectT.Addressable;
using System.Collections;
using System.Threading;

namespace ProjectT
{
    public class ResourceManager : ManagerBase
    {
        private Dictionary<string, IResource> datas = new Dictionary<string, IResource>();

        protected override void OnShutdown(ShutdownReason reason)
        {
            var errors = new List<Exception>();
            foreach (var scene in loadedScenes)
            {
                try
                {
                    ReleaseScene(scene);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
            loadedScenes.Clear();
            try
            {
                ReleaseAll(true);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            if (errors.Count > 0)
                throw new AggregateException(errors);
        }


        private readonly HashSet<AsyncOperationHandle<SceneInstance>> loadedScenes = new HashSet<AsyncOperationHandle<SceneInstance>>();

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
                loadedScenes.Add(handle);
                retained = true;
                OnLoadingProgressAction?.Invoke(1f);
                return handle;
            }
            finally
            {
                if (!retained)
                    ReleaseScene(handle);
            }
        }

        public async UniTask<int> UnLoadSceneAsync(SceneInstance scene, Action<float> OnLoadingProgressAction)
        {
            int sceneIndex = scene.Scene.buildIndex;
            loadedScenes.RemoveWhere(h => !h.IsValid() || (h.IsDone && h.Result.Scene == scene.Scene));
            var handle = Addressables.UnloadSceneAsync(scene, autoReleaseHandle: false);
            try
            {
                while (!handle.IsDone)
                {
                    OnLoadingProgressAction?.Invoke(handle.PercentComplete);
                    await UniTask.Yield(cancellationToken: LifetimeToken);
                }
                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw handle.OperationException ?? new InvalidOperationException("Scene unload failed.");
                OnLoadingProgressAction?.Invoke(1f);
                return sceneIndex;
            }
            finally
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
        }

        #endregion

        public T LoadAndGet<T>(string path, bool dontDestroy = false)
        {
            if (datas.ContainsKey(path))
                return (T)datas[path].ResourceData;

            var handle = Addressables.LoadAssetAsync<T>(path);

            handle.WaitForCompletion();

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
                return default(T);
            }

            IResource resource = new IResource(path, handle.Result);
            resource.DontDestroy = dontDestroy;
            datas.Add(path, resource);

            return handle.Result;
        }

        public async UniTask<T> LoadAndGetAsync<T>(string path, bool dontDestroy = false, CancellationToken cancelToken = default)
        {
            cancelToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Asset path is empty.", nameof(path));
            if (datas.TryGetValue(path, out var cached))
                return (T)cached.ResourceData;
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, cancelToken))
            {
                var handle = Addressables.LoadAssetAsync<T>(path);
                bool retained = false;
                try
                {
                    while (!handle.IsDone) await UniTask.Yield(cancellationToken: linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    if (handle.Status != AsyncOperationStatus.Succeeded)
                        throw handle.OperationException ?? new InvalidOperationException($"Asset load failed: {path}");
                    if (datas.TryGetValue(path, out cached))
                        return (T)cached.ResourceData;
                    datas.Add(path, new IResource(path, handle.Result) { DontDestroy = dontDestroy });
                    retained = true;
                    return handle.Result;
                }
                finally
                {
                    if (!retained && handle.IsValid())
                        Addressables.Release(handle);
                }
            }
        }


        public void LoadAsset<T>(string path, System.Action<T> callback, bool dontDestroy = false, bool autoReleaseOnFail = true)
        {
            if (string.IsNullOrEmpty(path))
            {
                callback?.Invoke(default(T));
                return;
            }

            AsyncOperationHandle handle;
            if (datas.ContainsKey(path))
            {
                callback?.Invoke((T)datas[path].ResourceData);
            }
            else
            {
                handle = Addressables.LoadAssetAsync<T>(path);
                handle.WaitForCompletion();

                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    if (!datas.ContainsKey(path))
                    {
                        IResource resource = new IResource(path, handle.Result);
                        resource.DontDestroy = dontDestroy;
                        datas.Add(path, resource);
                    }
                    callback?.Invoke((T)handle.Result);
                }
                else
                {
                    if (autoReleaseOnFail)
                    {
                        if (handle.IsValid())
                            Addressables.Release(handle);
                    }

                    callback?.Invoke(default(T));
                }
            }
        }

        public async UniTask LoadAssetAsync<T>(string path, Action<T> callback, bool dontDestroy = false, bool autoReleaseOnFail = true, CancellationToken cancelToken = default) where T : UnityEngine.Object
        {
            // 핸들을 호출자에게 전달하지 않으므로 실패 시 항상 반환한다.
            var result = await LoadAndGetAsync<T>(path, dontDestroy, cancelToken);
            cancelToken.ThrowIfCancellationRequested();
            callback?.Invoke(result);
        }

        public string GetPath(AssetReference assetRef)
        {
            string result = string.Empty;
            var handle = Addressables.LoadResourceLocationsAsync(assetRef);
            handle.WaitForCompletion();

            using (AsyncOperationDisposer disposer = new AsyncOperationDisposer(handle))
            {
                if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null
                    && handle.Result.Count > 0)
                {
                    result = handle.Result[0].InternalId;
                }
                else
                {
                    Debug.Log("Fail To Load LoadResourceLocationsAsync");
                }
            }

            return result;
        }

        public string GetPathFromData(string name)
        {
            string result = string.Empty;

            if (datas != null)
                return datas.Keys.FirstOrDefault(x => x.Contains(name));

            return result;
        }

        public void Release(string addressable)
        {
            if (string.IsNullOrEmpty(addressable))
                return;
            if (datas.TryGetValue(addressable, out var resource))
            {
                Addressables.Release(resource.ResourceData);
                datas.Remove(addressable);
            }
        }

        public async UniTask ReleaseAsync(string addressable)
        {
            await UniTask.Yield(cancellationToken: LifetimeToken);
            if (string.IsNullOrEmpty(addressable))
                return;
            if (datas.TryGetValue(addressable, out var resource))
            {
                Addressables.Release(resource.ResourceData);
                datas.Remove(addressable);
            }
        }

        public void ReleaseAll(bool isAll = false)
        {
            var errors = new List<Exception>();
            foreach (var pair in new List<KeyValuePair<string, IResource>>(datas))
            {
                if (pair.Value.DontDestroy && !isAll)
                    continue;
                datas.Remove(pair.Key);
                try
                {
                    Addressables.Release(pair.Value.ResourceData);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
            if (errors.Count > 0)
                throw new AggregateException(errors);
        }

        public async UniTask ReleaseAllAsync(bool isAll = false)
        {
            await UniTask.Yield(cancellationToken: LifetimeToken);
            ReleaseAll(isAll);
        }

    }
}
