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

        // 카탈로그와 번들이 gs:// 위치를 사용하므로 첫 Addressables 로드 이전에 프로바이더와 URL 변환을 등록하고 Firebase 의존성을 확인해야 한다.
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
                Addressables.InternalIdTransformFunc = FirebaseAddressablesCache.IdTransformFunc;
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
