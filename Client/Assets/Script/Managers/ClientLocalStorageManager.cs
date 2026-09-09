using Cysharp.Threading.Tasks;
using ProjectT;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace ProjectT
{
    public class ClientLocalStorageManager : ManagerBase
    {
        private string assetFolderPath;
        public string AssetFolderPath { get => assetFolderPath; }
        private string defaultFolder = "ClientLocalStorage";

        private Dictionary<EClientLocalStorageType, ClientLocalStorage> StorageDatas = new Dictionary<EClientLocalStorageType, ClientLocalStorage>();

        protected override async UniTask OnInitializeAsync(CancellationToken token)
        {
            InitAssetFolderPath();

            if (Context.LoadData)
                await LoadAllDataAsync(token);
        }

        protected override void OnShutdown(ShutdownReason reason)
        {
            try
            {
                if (reason != ShutdownReason.InitializationFailure)
                    SaveAllData();
            }
            finally
            {
                StorageDatas.Clear();
            }
        }

        private void InitAssetFolderPath()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.OSXEditor)
            {
                assetFolderPath = Application.dataPath;
            }
            else
            {
                assetFolderPath = Application.persistentDataPath;
            }

            assetFolderPath = Path.Combine(assetFolderPath, defaultFolder);
            Directory.CreateDirectory(assetFolderPath);
        }

        public T CreateData<T>(EClientLocalStorageType Type) where T : ClientLocalStorage, new()
        {
            if (StorageDatas.TryGetValue(Type, out var StorageData))
            {
                return StorageData as T;
            }

            T NewStorage = new T();

            if (NewStorage != null)
            {
                NewStorage.StorageType = Type;
                StorageDatas.Add(Type, NewStorage);
                return NewStorage;
            }

            return default(T);
        }

        public T GetData<T>(EClientLocalStorageType Type) where T : ClientLocalStorage
        {
            if (StorageDatas.TryGetValue(Type, out var StorageData))
            {
                return StorageData as T;
            }

            return default(T);
        }

        public void SaveData(EClientLocalStorageType Type)
        {
            if (string.IsNullOrEmpty(assetFolderPath))
            {
                Global.Instance.LogError($"[ClientLocalStorageManager] Fail Save Data This Asset Foler Path is Null");
                return;
            }

            if (StorageDatas.TryGetValue(Type, out var StorageData))
            {
                StorageData.Save(assetFolderPath);
            }
        }

        public async UniTask SaveDataAsync(EClientLocalStorageType Type, CancellationToken cancellationToken = default)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, cancellationToken))
            {
                await UniTask.Yield(cancellationToken: linked.Token);
                // 종료 시 저장과 충돌하지 않도록 실제 쓰기는 메인 스레드에서 완료한다.
                SaveData(Type);
            }
        }

        public void SaveAllData()
        {
            foreach (EClientLocalStorageType type in System.Enum.GetValues(typeof(EClientLocalStorageType)))
            {
                if (StorageDatas.TryGetValue(type, out var StorageData))
                {
                    StorageData.Save(assetFolderPath);
                }
            }
        }

        public void LoadData(EClientLocalStorageType Type)
        {
            if (string.IsNullOrEmpty(assetFolderPath))
            {
                Global.Instance.LogError($"[ClientLocalStorageManager] Fail Save Data This Asset Foler Path is Null");
                return;
            }
            if (!File.Exists(Path.Combine(assetFolderPath, $"{Type}.dat")))
                return;
            ClientLocalStorage StorageData = ClientLocalStorage.Load(assetFolderPath, Type);
            if (StorageData == null)
            {
                Global.Instance.LogError($"[ClientLocalStorageManager] Load Fail Type : {Type}");
                return;
            }

            StorageData.StorageType = Type;
            StorageData.CompleteLoad();

            StorageDatas[Type] = StorageData;
        }

        public async UniTask LoadDataAsync(EClientLocalStorageType Type, CancellationToken token = default)
        {
            await UniTask.Yield(cancellationToken: token);

            if (string.IsNullOrEmpty(assetFolderPath))
            {
                Global.Instance.LogError($"[ClientLocalStorageManager] Fail Save Data This Asset Foler Path is Null");
                return;
            }

            if (!File.Exists(Path.Combine(assetFolderPath, $"{Type}.dat")))
                return;
            ClientLocalStorage StorageData = ClientLocalStorage.Load(assetFolderPath, Type);
            if (StorageData == null)
            {
                Global.Instance.LogError($"[ClientLocalStorageManager] Load Fail Type : {Type}");
                return;
            }


            StorageData.StorageType = Type;
            StorageData.CompleteLoad();

            StorageDatas[Type] = StorageData;
        }

        public async UniTask LoadAllDataAsync(CancellationToken token = default)
        {
            foreach (EClientLocalStorageType Type in System.Enum.GetValues(typeof(EClientLocalStorageType)))
            {
                await LoadDataAsync(Type, token);
            }
        }

        public void LoadAllData()
        {
            foreach (EClientLocalStorageType Type in System.Enum.GetValues(typeof(EClientLocalStorageType)))
            {
                LoadData(Type);
            }
        }
    }

}
