namespace ProjectT.Addressable
{
    using Cysharp.Threading.Tasks;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using UnityEngine.ResourceManagement.ResourceLocations;

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

    public static class Downloader
    {
        // 번들 단위 재시도는 그룹 스키마의 RetryCount가 담당하고, 여기서는 라벨 전체 다운로드를 다시 시도한다.
        private const int RetryCount = 2;
        private const float RetryDelaySeconds = 1f;

        public static async UniTask<long> GetDownloadSizeAsync(IList<string> labels, CancellationToken token)
        {
            var locations = await LoadLocationsAsync(labels, token);

            try
            {
                var handle = Addressables.GetDownloadSizeAsync(locations.Result);

                try
                {
                    return await handle.ToUniTask(cancellationToken: token);
                }
                finally
                {
                    if (handle.IsValid())
                        Addressables.Release(handle);
                }
            }
            finally
            {
                Addressables.Release(locations);
            }
        }

        public static async UniTask DownloadAsync(IList<string> labels, Action<DownloadInfo> onProgress, CancellationToken token)
        {
            var locations = await LoadLocationsAsync(labels, token);

            try
            {
                for (int attempt = 0; ; ++attempt)
                {
                    try
                    {
                        await DownloadInternalAsync(locations.Result, onProgress, token);
                        return;
                    }
                    catch (Exception) when (attempt < RetryCount && !token.IsCancellationRequested)
                    {
                        await UniTask.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), cancellationToken: token);
                    }
                }
            }
            finally
            {
                Addressables.Release(locations);
            }
        }

        private static async UniTask DownloadInternalAsync(IList<IResourceLocation> locations, Action<DownloadInfo> onProgress, CancellationToken token)
        {
            var handle = Addressables.DownloadDependenciesAsync(locations, false);

            try
            {
                var info = new DownloadInfo();

                while (!handle.IsDone)
                {
                    token.ThrowIfCancellationRequested();

                    if (onProgress != null)
                    {
                        var status = handle.GetDownloadStatus();
                        info.size = status.TotalBytes;
                        info.downloadedByte = status.DownloadedBytes;
                        info.progress = status.Percent;
                        onProgress(info);
                    }

                    await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, token);
                }

                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw handle.OperationException ?? new InvalidOperationException("Bundle download failed.");

                if (onProgress != null)
                {
                    var status = handle.GetDownloadStatus();
                    info.size = status.TotalBytes;
                    info.downloadedByte = status.DownloadedBytes;
                    info.progress = 1f;
                    onProgress(info);
                }
            }
            finally
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
        }

        private static async UniTask<AsyncOperationHandle<IList<IResourceLocation>>> LoadLocationsAsync(IList<string> labels, CancellationToken token)
        {
            if (labels == null || labels.Count == 0)
                throw new ArgumentException("Download labels are empty.", nameof(labels));

            var keys = new List<object>(labels.Count);
            foreach (var label in labels)
            {
                keys.Add(label);
            }

            var handle = Addressables.LoadResourceLocationsAsync(keys, Addressables.MergeMode.Union, null);
            bool retained = false;

            try
            {
                await handle.ToUniTask(cancellationToken: token);
                retained = true;

                return handle;
            }
            finally
            {
                if (!retained && handle.IsValid())
                    Addressables.Release(handle);
            }
        }
    }
}
