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

        /// <summary>
        /// Set this bool as soon as the app is ready to download from Firebase Storage. If you require authentication
        /// to access items on Firebase Storage you should set this after your User has logged in.
        /// The Addressables Pipeline will wait and 'load' until you set this to true.
        /// </summary>
        public static bool IsFirebaseSetupFinished
        {
            get => isFirebaseSetupFinished;
            set
            {
                if (isFirebaseSetupFinished != value)
                {
                    isFirebaseSetupFinished = value;
                    FireFirebaseSetupFinished();
                }
            }
        }

        public static LogLevel LogLevel = LogLevel.Warning;

        public static event System.Action FirebaseSetupFinished;

        public static bool IsFirebaseStorageLocation(string internalId)
        {
            return internalId.StartsWith(FirebaseAddressablesConstants.GS_URL_START);

        }

        // 카탈로그와 번들이 gs:// 위치를 사용하므로 첫 Addressables 로드 이전에 프로바이더와 URL 변환을 등록하고 준비 완료를 알려야 한다.
        public static async UniTask InitializeAsync(CancellationToken token)
        {
            if (IsFirebaseSetupFinished)
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

            IsFirebaseSetupFinished = true;

            await Addressables.InitializeAsync().ToUniTask(cancellationToken: token);
        }

        private static void FireFirebaseSetupFinished()
        {
            FirebaseSetupFinished?.Invoke();
        }
    }
}
