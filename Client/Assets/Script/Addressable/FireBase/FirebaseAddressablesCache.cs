using Cysharp.Threading.Tasks;
using Firebase.Extensions;
using Firebase.Storage;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace ProjectT.Addressable
{
    public static class FirebaseAddressablesCache
    {
        private static readonly Dictionary<string, string> internalIdToStorageUrlDict = new Dictionary<string, string>();

        private static int runningFetchUrlOperationCount;


        public static string IdTransformFunc(IResourceLocation location)
        {
            if (internalIdToStorageUrlDict.TryGetValue(location.InternalId, out string storageUrl))
            {
                return storageUrl;
            }
            return location.InternalId;
        }

        public static void SetInternalIdToStorageUrlMapping(string internalId, string storageUrl)
        {
            internalIdToStorageUrlDict[internalId] = storageUrl;
        }

        public static void PreWarmDependencies(object key, Action completed)
        {
            PreWarmDependencies(new List<object>() { key }, completed);
        }

        public static UniTask PreWarmDependenciesAsync(IList<string> keys, CancellationToken token)
        {
            var ready = new UniTaskCompletionSource();
            PreWarmDependencies(new List<object>(keys), () => ready.TrySetResult());

            return ready.Task.AttachExternalCancellation(token);
        }

        public static void PreWarmDependencies(List<object> keys, Action completed)
        {
            if (FirebaseAddressablesManager.IsFirebaseSetupFinished == false)
            {
                FirebaseAddressablesManager.FirebaseSetupFinished += () => { PreWarmDependencies(keys, completed); };
            }
            else
            {
                var initOperation = UnityEngine.AddressableAssets.Addressables.InitializeAsync();

                if (initOperation.IsDone == false)
                {
                    initOperation.Completed += handle => GetFirebaseUrl(keys, completed);
                }
                else
                {
                    GetFirebaseUrl(keys, completed);
                }
            }
        }

        private static void GetFirebaseUrl(List<object> keys, Action completed)
        {
            if (runningFetchUrlOperationCount > 0)
            {
                Debug.LogError("Wait until the previous operation is completed before starting a new one");
                return;
            }
            runningFetchUrlOperationCount = 0;
            foreach (var key in keys)
            {
                foreach (IResourceLocator locator in UnityEngine.AddressableAssets.Addressables.ResourceLocators)
                {
                    if (locator.Locate(key, typeof(object), out IList<IResourceLocation> locations))
                    {
                        foreach (IResourceLocation location in locations)
                        {
                            foreach (var dependency in location.Dependencies)
                            {
                                string firebaseUrl = UnityEngine.AddressableAssets.Addressables.ResourceManager.TransformInternalId(dependency);
                                if (FirebaseAddressablesManager.IsFirebaseStorageLocation(firebaseUrl) == false)
                                {
                                    continue;
                                }

                                StorageReference reference = FirebaseStorage.DefaultInstance.GetReferenceFromUrl(firebaseUrl);

                                StartUrlFetch(completed, reference, firebaseUrl);
                            }
                        }
                    }
                }
            }

            // 로컬 카탈로그처럼 gs:// 의존성이 없으면 요청이 한 번도 시작되지 않으므로 여기서 완료를 알린다.
            if (runningFetchUrlOperationCount == 0)
                completed();
        }

        private static void StartUrlFetch(Action completed, StorageReference reference, string firebaseUrl)
        {
            runningFetchUrlOperationCount++;
            reference.GetDownloadUrlAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled || task.IsFaulted)
                {
                    Debug.LogError($"Could not get url for: {firebaseUrl}, {task.Exception}");
                }
                else
                {
                    string url = task.Result.ToString();
                    SetInternalIdToStorageUrlMapping(firebaseUrl, url);
                }

                runningFetchUrlOperationCount--;
                if (runningFetchUrlOperationCount <= 0)
                {
                    completed();
                }
            });
        }
    }
}