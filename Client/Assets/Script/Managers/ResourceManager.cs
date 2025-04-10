using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using ProjectT.Addressable;
using System.Collections;

namespace ProjectT
{
    public class ResourceManager : ManagerBase
    {
        private Dictionary<string, IResource> datas = new Dictionary<string, IResource>();

        #region ManagerBase
        public override void OnEnter()
        {

        }

        public override void OnFixedUpdate(float dt)
        {
        }

        public override void OnLateUpdate()
        {
        }

        public override void OnLeave()
        {
            foreach (var data in datas.Values)
            {
                Addressables.Release(data.ResourceData);
            }

            datas.Clear();
        }

        public override void OnUpdate(float dt)
        {
        }

        public override void OnAppStart()
        {
        }
        public override void OnAppEnd()
        {
        }

        public override void OnAppFocuse(bool focused)
        {
        }

        public override void OnAppPause(bool paused)
        {
        }
        #endregion

        #region SceneLoad
        public async UniTask<AsyncOperationHandle<SceneInstance>> LoadSceneAsync(string sceneName, UnityEngine.SceneManagement.LoadSceneMode sceneMode, System.Action<float> OnLoadingProgressAction)
        {
            var handle = Addressables.LoadSceneAsync(sceneName, sceneMode);

            OnLoadingProgressAction?.Invoke(0);

            while (handle.IsDone == false)
            {
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);

                OnLoadingProgressAction?.Invoke(handle.PercentComplete);
            }

            OnLoadingProgressAction?.Invoke(1.0f);
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);

            return handle;
        }

        public async UniTask<int> UnLoadSceneAsync(SceneInstance scene, System.Action<float> OnLoadingProgressAction)
        {
            var handle = Addressables.UnloadSceneAsync(scene);

            int sceneIndex = UnityEngine.SceneManagement.SceneManager.GetSceneByName(scene.Scene.name).buildIndex;

            OnLoadingProgressAction?.Invoke(0);

            while (handle.IsDone == false)
            {
                OnLoadingProgressAction?.Invoke(handle.PercentComplete);

                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            }

            OnLoadingProgressAction?.Invoke(1.0f);

            return sceneIndex;
        }

        #endregion

        public T LoadAndGet<T>(string path, bool dontDestroy = false)
        {
            if (datas.ContainsKey(path))
                return (T)datas[path].ResourceData;

            var handle = Addressables.LoadAssetAsync<T>(path);

            handle.WaitForCompletion();

            if (handle.Status != AsyncOperationStatus.Succeeded)
                return default(T);

            IResource resource = new IResource(path, handle.Result);
            resource.DontDestroy = dontDestroy;
            datas.Add(path, resource);

            return handle.Result;
        }

        public async UniTask<T> LoadAndGetAsync<T>(string path, bool dontDestroy = false)
        {
            if (datas.ContainsKey(path))
                return (T)datas[path].ResourceData;

            var handle = Addressables.LoadAssetAsync<T>(path);
            await UniTask.WaitUntil(() => { return handle.IsDone; });

            if (handle.Status != AsyncOperationStatus.Succeeded)
                return default(T);

            IResource resource = new IResource(path, handle.Result);
            resource.DontDestroy = dontDestroy;
            datas.Add(path, resource);

            return handle.Result;
        }


        public void LoadAsset<T>(string path, System.Action<T> callback, bool dontDestroy = false, bool autoReleaseOnFail = true)
        {
            if (string.IsNullOrEmpty(path))
                callback?.Invoke(default(T));

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

        public async UniTask LoadAssetAsync<T>(string path, System.Action<T> callback, bool dontDestroy = false, bool autoReleaseOnFail = true) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(path))
                callback?.Invoke(default(T));


            if (datas.ContainsKey(path))
            {
                callback?.Invoke((T)datas[path].ResourceData);
            }
            else
            {
                var handle = Addressables.LoadAssetAsync<T>(path);
                await UniTask.WaitUntil(() => { return handle.IsDone; });

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
                    callback?.Invoke(default(T));

                    if (autoReleaseOnFail)
                    {
                        if (handle.IsValid())
                            Addressables.Release(handle);
                    }
                }
            }
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
            if (datas.TryGetValue(addressable, out var resource))
            {
                Addressables.Release(resource.ResourceData);
                datas.Remove(addressable);
            }
        }

        public async UniTask ReleaseAsync(string addressable)
        {
            await UniTask.Yield();
            if (datas.TryGetValue(addressable, out var resource))
            {
                Addressables.Release(resource.ResourceData);
                datas.Remove(addressable);
            }
        }

        public void ReleaseAll(bool isAll = false)
        {
            foreach (var data in datas)
            {
                var resource = data.Value;

                if (resource.DontDestroy && !isAll)
                    continue;

                Release(data.Key);
            }

            datas.Clear();
        }

        public async UniTask ReleaseAllAsync(bool isAll = false)
        {
            await UniTask.Yield();

            foreach (var data in datas)
            {
                var resource = data.Value;
                if (resource.DontDestroy && !isAll)
                    continue;

                Addressables.Release(resource.ResourceData);
            }

            datas.Clear();
        }

    }
}