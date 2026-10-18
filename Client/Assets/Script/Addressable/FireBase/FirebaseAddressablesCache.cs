using Cysharp.Threading.Tasks;
using Firebase.Extensions;
using Firebase.Storage;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace ProjectT.Addressable
{
    public static class FirebaseAddressablesCache
    {
        private static readonly Dictionary<string, string> internalIdToStorageUrlDict = new Dictionary<string, string>();

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

        public static UniTask PreWarmDependenciesAsync(IList<string> keys, CancellationToken token)
        {
            var ready = new UniTaskCompletionSource();
            GetFirebaseUrl(keys, () => ready.TrySetResult());

            return ready.Task.AttachExternalCancellation(token);
        }

        private static void GetFirebaseUrl(IList<string> keys, Action completed)
        {
            var references = new List<(StorageReference reference, string firebaseUrl)>();
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

                                references.Add((reference, firebaseUrl));
                            }
                        }
                    }
                }
            }

            // 로컬 카탈로그처럼 gs:// 의존성이 없으면 요청이 한 번도 시작되지 않으므로 여기서 완료를 알린다.
            if (references.Count == 0)
            {
                completed();
                return;
            }

            // 남은 요청 수를 호출마다 따로 세야 앞선 PreWarm이 끝나기 전에 들어온 호출도 자기 요청이 끝나면 완료된다.
            int remainingFetchCount = references.Count;
            foreach (var (reference, firebaseUrl) in references)
            {
                StartUrlFetch(reference, firebaseUrl, () =>
                {
                    remainingFetchCount--;
                    if (remainingFetchCount == 0)
                        completed();
                });
            }
        }

        private static void StartUrlFetch(StorageReference reference, string firebaseUrl, Action fetched)
        {
            reference.GetDownloadUrlAsync().ContinueWithOnMainThread(task =>
            {
                // 실패한 주소는 gs://로 남아 번들 프로바이더가 로드할 때 다시 해석하고, 그때의 실패가 로드 실패로 전달된다.
                if (!task.IsCanceled && !task.IsFaulted)
                    SetInternalIdToStorageUrlMapping(firebaseUrl, task.Result.ToString());

                fetched();
            });
        }
    }
}