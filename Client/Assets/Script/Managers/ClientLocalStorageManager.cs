using Cysharp.Threading.Tasks;
using ProjectT;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ProjectT
{
    public class ClientLocalStorageManager : ManagerBase
    {
        private string assetFolderPath;
        public string AssetFolderPath { get => assetFolderPath; }
        private string defaultFolder = "ClientLocalStorage";

        private Dictionary<EClientLocalStorageType, ClientLocalStorage> StorageDatas =new Dictionary<EClientLocalStorageType, ClientLocalStorage>();

        #region ManagerBase
        public override void OnAppEnd()
        {
        }

        public override void OnAppFocuse(bool focused)
        {
        }

        public override void OnAppPause(bool paused)
        {
        }

        public override void OnAppStart()
        {
        }

        public override void OnEnter()
        {
            InitAssetFolderPath();
        }

        public override void OnFixedUpdate(float dt)
        {
        }

        public override void OnLateUpdate()
        {
        }

        public override void OnLeave()
        {
            SaveAllData();
        }

        public override void OnUpdate(float dt)
        {
        }
        #endregion

        private void InitAssetFolderPath()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.OSXEditor)
            {
                assetFolderPath = Application.dataPath;
            }
            else if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer || Application.platform == RuntimePlatform.WindowsPlayer)
            {
                assetFolderPath = Application.persistentDataPath;
            }

            if (assetFolderPath.Last() != '/')
            {
                assetFolderPath += $"/{defaultFolder}/" ;
            }
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

        public async UniTask SaveDataAsync(EClientLocalStorageType Type)
        {
            await UniTask.Yield();

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
            ClientLocalStorage StorageData = ClientLocalStorage.Load(assetFolderPath, Type);
            if (StorageData == null)
            {
                Global.Instance.LogError($"[ClientLocalStorageManager] Load Fail Type : {Type}");
                return;
            }


            StorageData.StorageType = Type;
            StorageData.CompleteLoad();

            StorageDatas.Add(Type, StorageData);
        }

        public async UniTask LoadDataAsync(EClientLocalStorageType Type)
        {
            await UniTask.Yield();

            if (string.IsNullOrEmpty(assetFolderPath))
            {
                Global.Instance.LogError($"[ClientLocalStorageManager] Fail Save Data This Asset Foler Path is Null");
                return;
            }

            ClientLocalStorage StorageData = ClientLocalStorage.Load(assetFolderPath, Type);
            if (StorageData == null)
            {
                Global.Instance.LogError($"[ClientLocalStorageManager] Load Fail Type : {Type}");
                return;
            }


            StorageData.StorageType = Type;
            StorageData.CompleteLoad();

            StorageDatas.Add(Type, StorageData);
        }

        public async UniTask LoadAllDataAsync()
        {
            foreach (EClientLocalStorageType Type in System.Enum.GetValues(typeof(EClientLocalStorageType)))
            {
                await LoadDataAsync(Type);
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