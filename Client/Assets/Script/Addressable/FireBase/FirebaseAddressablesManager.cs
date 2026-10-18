using Cysharp.Threading.Tasks;
using Firebase;
using Firebase.Extensions;
using Firebase.Storage;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace ProjectT.Addressable
{
    public class FirebaseAddressablesManager
    {
        private static bool isFirebaseSetupFinished;
        private static bool providersRegistered;

        public static bool IsFirebaseStorageLocation(string internalId)
        {
            return internalId.StartsWith("gs://");
        }

        // 프로바이더가 gs:// 주소를 다운로드 URL로 바꾼 뒤 메인 스레드에서 이어서 로드한다. 주소를 받지 못하면 요청을 그 예외로 실패시킨다.
        internal static void ResolveDownloadUrl(ProvideHandle provideHandle, string storageUrl, Action<string> resolved)
        {
            FirebaseStorage.DefaultInstance.GetReferenceFromUrl(storageUrl).GetDownloadUrlAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled || task.IsFaulted)
                {
                    provideHandle.Complete<object>(null, false, task.Exception);
                    return;
                }

                resolved(task.Result.ToString());
            });
        }

        // 카탈로그와 번들이 gs:// 위치를 사용하므로 첫 Addressables 로드 이전에 프로바이더와 다운로드 URL 교체를 등록하고 Firebase 의존성을 확인해야 한다.
        // 프로바이더와 PreWarm은 이 순서를 전제로 준비를 기다리지 않고 Firebase Storage를 호출한다.
        public static async UniTask InitializeAsync(CancellationToken token)
        {
            if (isFirebaseSetupFinished)
                return;

            if (!providersRegistered)
            {
                Addressables.ResourceManager.ResourceProviders.Add(new FirebaseStorageAssetBundleProvider());
                Addressables.ResourceManager.ResourceProviders.Add(new FirebaseStorageJsonAssetProvider());
                Addressables.ResourceManager.ResourceProviders.Add(new FirebaseStorageHashProvider());
                Addressables.WebRequestOverride = FirebaseAddressablesCache.OverrideWebRequest;
                providersRegistered = true;
            }

            var status = await FirebaseApp.CheckAndFixDependenciesAsync().AsUniTask().AttachExternalCancellation(token);

            if (status != DependencyStatus.Available)
                throw new InvalidOperationException($"Firebase dependencies are unavailable: {status}");

            isFirebaseSetupFinished = true;

            await Addressables.InitializeAsync().ToUniTask(cancellationToken: token);
        }
    }
}
