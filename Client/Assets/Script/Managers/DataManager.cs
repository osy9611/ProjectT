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
        private DesignTable.DataMgr tableData = new DataMgr();
        public DesignTable.DataMgr Table { get => tableData; }
        private DesignLocal.LocalData localData = new DesignLocal.LocalData();
        private SystemLanguage currentLanguage;

        private bool isDone;

        #region ManagerBase
        public override void OnAppStart()
        {
        }

        public override void OnEnter()
        {

        }

        public override void OnFixedUpdate(float dt)
        {
        }

        public override void OnLateUpdate()
        {
        }

        public override void OnLeave()
        {
        }

        public override void OnUpdate(float dt)
        {
        }

        public override void OnAppEnd()
        {
        }

        public override void OnAppFocuse(bool focused)
        {
        }

        public override void OnAppPause(bool paused)
        {
        }
        #endregion

        public async UniTask GetTableDatas()
        {
            tableData.Init();

            //Load Locl Data
            await LoadLocalDataAsync((result) =>
            {
                Global.Instance.Log($"Local Data Load Result :  {GetResultString(result)}");
            });

            //Load Table Data
            await LoadTableDataAsync((result) =>
            {
                Global.Instance.Log($"Table Data Load Result :  {GetResultString(result)}");
            });
        }

        private string GetResultString(bool result)
        {
            return result == true ? "Success" : "Fail";
        }

        public async UniTask LoadLocalDataAsync(System.Action<bool> callback = null)
        {
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
                case SystemLanguage.English:
                    localPath += "En.bytes";
                    break;
            }

            TextAsset textAsset = null;

            await Global.Resource.LoadAssetAsync<TextAsset>(localPath,
                (resAsset) =>
                {
                    if (resAsset == null)
                    {
                        Global.Instance.LogError("Local Asset Is Null");
                        callback?.Invoke(false);
                        return;
                    }

                    textAsset = resAsset;
                });

            localData.LoadData(textAsset.bytes);
            callback?.Invoke(true);
        }

        public async UniTask LoadTableDataAsync(System.Action<bool> callback = null)
        {
            if (isDone)
            {
                callback?.Invoke(true);
                return;
            }

            foreach (TableId tableID in System.Enum.GetValues(typeof(DesignTable.TableId)))
            {
                string tablePath = $"Assets/Automation/Table/{tableID}.bytes";

                TextAsset textAsset = null;

                await Global.Resource.LoadAssetAsync<TextAsset>(tablePath,
                    (resAsset) =>
                    {
                        if (resAsset == null)
                        {
                            Global.Instance.LogError("Table Asset Is Null");
                            callback?.Invoke(false);
                            return;
                        }

                        textAsset = resAsset;
                    });

                tableData.LoadData(tableID, textAsset.bytes);
                await Global.Resource.ReleaseAsync(tablePath);

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