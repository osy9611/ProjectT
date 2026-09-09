using System.Threading;
using Cysharp.Threading.Tasks;
using DesignTable;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using UnityEngine;

namespace ProjectT
{
    public class DataManager : ManagerBase
    {
        private ResourceManager resource;

        private DesignTable.DataMgr tableData;
        public DesignTable.DataMgr Table { get => tableData; }
        private DesignLocal.LocalData localData = new DesignLocal.LocalData();
        private SystemLanguage currentLanguage;

        private bool isDone;

        protected override async UniTask OnInitializeAsync(CancellationToken token)
        {
            resource = Context.Get<ResourceManager>();

            if (Context.LoadData)
                await GetTableDatas(token);
        }

        protected override void OnShutdown(ShutdownReason reason)
        {
            tableData = null;
            localData = null;
            isDone = false;
        }

        public async UniTask GetTableDatas(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (tableData == null)
                tableData = new DataMgr();

            tableData.Init();

            //Load Locl Data
            await LoadLocalDataAsync((result) =>
            {
                Global.Instance.Log($"Local Data Load Result :  {GetResultString(result)}");
            }, token);

            //Load Table Data
            await LoadTableDataAsync((result) =>
            {
                Global.Instance.Log($"Table Data Load Result :  {GetResultString(result)}");
            }, token);
        }

        private string GetResultString(bool result)
        {
            return result == true ? "Success" : "Fail";
        }

        public async UniTask LoadLocalDataAsync(System.Action<bool> callback = null, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            string localPath = "Assets/Automation/Local/";
            currentLanguage = Application.systemLanguage;

            switch (currentLanguage)
            {
                case SystemLanguage.Korean:
                    localPath += "Ko.bytes";
                    break;
                case SystemLanguage.Japanese:
                    localPath += "Jp.bytes";
                    break;
                default:
                case SystemLanguage.English:
                    localPath += "En.bytes";
                    break;
            }

            TextAsset textAsset = null;

            await resource.LoadAssetAsync<TextAsset>(localPath,
                (resAsset) =>
                {
                    if (resAsset == null)
                    {
                        Global.Instance.LogError("Local Asset Is Null");
                        callback?.Invoke(false);
                        return;
                    }

                    textAsset = resAsset;
                }, cancelToken: token);

            token.ThrowIfCancellationRequested();
            if (textAsset == null)
                throw new System.InvalidOperationException($"Missing localization asset: {localPath}");
            try
            {
                localData.LoadData(textAsset.bytes);
            }
            finally
            {
                resource.Release(localPath);
            }
            callback?.Invoke(true);
        }

        public async UniTask LoadTableDataAsync(System.Action<bool> callback = null, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (isDone)
            {
                callback?.Invoke(true);
                return;
            }

            foreach (TableId tableID in System.Enum.GetValues(typeof(DesignTable.TableId)))
            {
                string tablePath = $"Assets/Automation/Table/{tableID}.bytes";

                TextAsset textAsset = null;

                await resource.LoadAssetAsync<TextAsset>(tablePath,
                    (resAsset) =>
                    {
                        if (resAsset == null)
                        {
                            Global.Instance.LogError("Table Asset Is Null");
                            callback?.Invoke(false);
                            return;
                        }

                        textAsset = resAsset;
                    }, cancelToken: token);

                token.ThrowIfCancellationRequested();
                if (textAsset == null)
                    throw new System.InvalidOperationException($"Missing table asset: {tablePath}");
                try
                {
                    tableData.LoadData(tableID, textAsset.bytes);
                }
                finally
                {
                    resource.Release(tablePath);
                }

                Global.Instance.Log($"[Table] {tableID} Load Complete!!");
            }

            tableData.SetUpRef();
            callback?.Invoke(true);
            isDone = true;
        }

        public string GetLocalString(DesignLocal.StringDef stringDef)
        {
            return localData.localString[stringDef];
        }
    }
}