namespace ProjectT.Addressable
{
    using Cysharp.Threading.Tasks;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;

    public enum eDownloadStatus
    {
        None,
        Failed,
        Ing,
        Complete
    }

    public enum eDownloadByLabelStatus
    {
        None,
        Failed,
        Ing,
        CompleteOneLabel,
        CompleteAll,
    }

    public class DownloadInfo
    {
        public long size;
        public long downloadedByte;
        public float progress;

        public override string ToString()
        {
            return JsonUtility.ToJson(this);
        }
    }

    public class DownloadByLabelsInfo
    {
        public string lable;

        public long size;
        public long downloadedByte;
        public float progress;

        public long accmulateDownloadByte;

        public long totalSize;
        public float totalProgress;

        public override string ToString()
        {
            return JsonUtility.ToJson(this);
        }
    }

    public class FirebaseDonwloader
    {
        //public async UniTask GetDownloadSize(List<string> lableKeys, System.Action<bool, long> callback)
        //{
        //    Addressables.ResourceManager.ResourceProviders.Add(new FirebaseStorageAssetBundleProvider());
        //    Addressables.ResourceManager.ResourceProviders.Add(new FirebaseStorageJsonAssetProvider());
        //    Addressables.ResourceManager.ResourceProviders.Add(new FirebaseStorageHashProvider());
        //}
    }

    public static class Downloader 
    {
        static public async UniTask GetDownloadSize(List<string> lableKeys, System.Action<bool,long> callback)
        {
            await UniTask.Yield();


            List<string> keys = new List<string>();
            keys.AddRange(lableKeys);

#pragma warning disable CS0612 // Type or member is obsolete
            var locHandle = UnityEngine.AddressableAssets.Addressables.LoadResourceLocationsAsync(keys,
                UnityEngine.AddressableAssets.Addressables.MergeMode.Union);
#pragma warning restore CS0612 // Type or member is obsolete
            locHandle.WaitForCompletion();

            using (AsyncOperationDisposer locDisposer = new AsyncOperationDisposer(locHandle))
            {
                if (locHandle.Status == AsyncOperationStatus.Failed)
                {
                    callback?.Invoke(false, 0);
                    return;
                }

                var sizeHandle = UnityEngine.AddressableAssets.Addressables.GetDownloadSizeAsync(locHandle.Result);
                sizeHandle.WaitForCompletion();

                using (AsyncOperationDisposer sizeDisposer = new AsyncOperationDisposer(sizeHandle))
                {
                    if(sizeHandle.Status == AsyncOperationStatus.Failed)
                    {
                        callback?.Invoke(false, 0);
                        return;
                    }

                    callback?.Invoke(true, sizeHandle.Result);
                }
            }
        }

        static async public UniTask Download(List<string> labelkeys, System.Action<eDownloadStatus, DownloadInfo> callback)
        {
#pragma warning disable CS0612 // Type or member is obsolete
            var locHandle = UnityEngine.AddressableAssets.Addressables.LoadResourceLocationsAsync(
                labelkeys, UnityEngine.AddressableAssets.Addressables.MergeMode.Union);
#pragma warning restore CS0612 // Type or member is obsolete

            locHandle.WaitForCompletion();

            using(AsyncOperationDisposer locDisposer = new AsyncOperationDisposer(locHandle))
            {
                if(locHandle.Status == AsyncOperationStatus.Failed)
                {
                    callback?.Invoke(eDownloadStatus.Failed, null);
                    return;
                }

                AsyncOperationHandle<long> sizeHandle = UnityEngine.AddressableAssets.Addressables.GetDownloadSizeAsync(locHandle.Result);
                sizeHandle.WaitForCompletion();

                long size = 0;

                using (AsyncOperationDisposer sizeDisposer = new AsyncOperationDisposer(sizeHandle))
                {
                    if(sizeHandle.Status == AsyncOperationStatus.Failed)
                    {
                        callback?.Invoke(eDownloadStatus.Failed, null);
                        return;
                    }

                    size = sizeHandle.Result;
                }

                if(size == 0)
                {
                    callback?.Invoke(eDownloadStatus.None, null);
                    return;
                }

                DownloadInfo info = new DownloadInfo();
                info.size = size;

                AsyncOperationHandle downloadHandle = UnityEngine.AddressableAssets.Addressables.DownloadDependenciesAsync(locHandle.Result);

                using (AsyncOperationDisposer downloadDisposer = new AsyncOperationDisposer(downloadHandle))
                {
                    while(downloadHandle.IsDone == false)
                    {
                        info.progress = downloadHandle.PercentComplete;
                        info.downloadedByte = downloadHandle.GetDownloadStatus().DownloadedBytes;

                        callback?.Invoke(eDownloadStatus.Ing, info);

                        await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                    }

                    if(downloadHandle.Status == AsyncOperationStatus.Failed)
                    {
                        callback?.Invoke(eDownloadStatus.Failed, null);
                        return;
                    }

                    info.progress = downloadHandle.PercentComplete;
                    info.downloadedByte = downloadHandle.GetDownloadStatus().DownloadedBytes;

                    callback?.Invoke(eDownloadStatus.Complete, info);
                }
            }
        }
    }
}
