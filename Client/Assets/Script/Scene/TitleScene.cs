using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Scene;
using ProjectT.Server.DB;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class TitleScene : SceneBase
{
    protected override void OnFinalize()
    {
    }

    protected override async UniTask OnEnter(CancellationToken token, params object[] data)
    {
        await UniTask.WaitForSeconds(2.0f, cancellationToken: token);
        await Global.Data.GetTableDatas(token);
    }

    protected override void OnInitialize()
    {
        var cachePaths = new List<string>();
        Caching.GetAllCachePaths(cachePaths);
        foreach (var cachePath in cachePaths)
        {
            Global.Log($"Cache path : {cachePath}");
        }

        Test(LifetimeToken).Forget(error =>
        {
            if (!(error is System.OperationCanceledException))
                Global.LogException(error);
        });
    }

    private async UniTask Test(CancellationToken token)
    {
#if UNITY_EDITOR
        FirebaseDB firebaseDB = new FirebaseDB();

        string version = await firebaseDB.GetBuildVersion(UnityEditor.BuildTarget.Android.ToString())
            .AsUniTask().AttachExternalCancellation(token);

        Global.Log($"Build version : {version}");
#endif
    }
}
