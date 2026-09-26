using System.Threading;
using Cysharp.Threading.Tasks;
using DesignTable;
using System;
using UnityEngine;

namespace ProjectT
{
    public class DataManager : ManagerBase
    {
        private readonly bool loadData;
        private DesignTable.DataMgr tableData;
        public DesignTable.DataMgr Table
        {
            get
            {
                EnsureDataLoadedInternal();
                return tableData;
            }
        }
        private DesignLocal.LocalData localData;
        private UniTaskCompletionSource loadCompletion;

        private bool isDone;

        public DataManager(bool loadData = false)
        {
            this.loadData = loadData;
        }

        protected override async UniTask OnInitializeAsync(CancellationToken token)
        {
            if (loadData)
                await GetTableDatas(token);
        }

        protected override void OnShutdown(ShutdownReason reason)
        {
            loadCompletion?.TrySetCanceled();
            loadCompletion = null;
            tableData = null;
            localData = null;
            isDone = false;
        }

        public async UniTask GetTableDatas(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            LifetimeToken.ThrowIfCancellationRequested();

            if (State != ManagerState.Initializing && State != ManagerState.Ready)
                throw new InvalidOperationException($"{Name} cannot load data from {State}.");

            if (isDone)
                return;

            var pending = loadCompletion;
            if (pending == null)
            {
                pending = new UniTaskCompletionSource();
                loadCompletion = pending;
                LoadDataInternalAsync(pending).Forget();
            }

            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, token))
            {
                await pending.Task.AttachExternalCancellation(linked.Token);
            }
        }

        private async UniTask LoadDataInternalAsync(UniTaskCompletionSource completion)
        {
            try
            {
                var loadedLocalData = new DesignLocal.LocalData();
                var loadedTableData = new DataMgr();
                loadedTableData.Init();

                await LoadLocalDataInternalAsync(loadedLocalData, LifetimeToken);
                Global.Instance.Log("Local Data Load Result :  Success");

                await LoadTableDataInternalAsync(loadedTableData, LifetimeToken);
                loadedTableData.SetUpRef();

                LifetimeToken.ThrowIfCancellationRequested();
                Global.Instance.Log("Table Data Load Result :  Success");

                localData = loadedLocalData;
                tableData = loadedTableData;
                isDone = true;

                ClearLoadCompletionInternal(completion);
                completion.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                ClearLoadCompletionInternal(completion);
                completion.TrySetCanceled();
            }
            catch (Exception error)
            {
                ClearLoadCompletionInternal(completion);
                completion.TrySetException(error);
            }
        }

        private void ClearLoadCompletionInternal(UniTaskCompletionSource completion)
        {
            if (ReferenceEquals(loadCompletion, completion))
                loadCompletion = null;
        }

        private async UniTask LoadLocalDataInternalAsync(DesignLocal.LocalData target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            string localPath = "Assets/Automation/Local/";

            switch (Application.systemLanguage)
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

            using (var scope = Global.Resource.CreateScope())
            {
                var textAsset = await Global.Resource.LoadAndGetAsync<TextAsset>(localPath, cancelToken: token, scope: scope);

                target.LoadData(textAsset.bytes);
            }
        }

        private async UniTask LoadTableDataInternalAsync(DataMgr target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            foreach (TableId tableID in System.Enum.GetValues(typeof(DesignTable.TableId)))
            {
                string tablePath = $"Assets/Automation/Table/{tableID}.bytes";

                using (var scope = Global.Resource.CreateScope())
                {
                    var textAsset = await Global.Resource.LoadAndGetAsync<TextAsset>(tablePath, cancelToken: token, scope: scope);
                    target.LoadData(tableID, textAsset.bytes);
                }

                Global.Instance.Log($"[Table] {tableID} Load Complete!!");
            }
        }

        public string GetLocalString(DesignLocal.StringDef stringDef)
        {
            EnsureDataLoadedInternal();
            return localData.localString[stringDef];
        }

        private void EnsureDataLoadedInternal()
        {
            if (!isDone || tableData == null || localData == null)
                throw new InvalidOperationException($"{Name} data is not loaded. Await {nameof(GetTableDatas)} first.");
        }
    }
}
