using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Scene;
using ProjectT.Server.DB;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class TitleScene : SceneBase
{
    public override async UniTask OnEnter(float progress, params object[] data)
    {
        //Addressables.ResourceManager.ResourceProviders.Add(new FirebaseStorageAssetBundleProvider());
        //Addressables.ResourceManager.ResourceProviders.Add(new FirebaseStorageJsonAssetProvider());
        //Addressables.ResourceManager.ResourceProviders.Add(new FirebaseStorageHashProvider());

        //Addressables.InternalIdTransformFunc += FirebaseAddressablesCache.IdTransformFunc;
        //FirebaseAddressablesCache.PreWarmDependencies(new List<string>() { "default" },
        //    () =>
        //    {
        //        Downloader.GetDownloadSize(new List<string>() { "default" },
        //          (result, size) =>
        //          {
        //              Debug.Log($"Result {result} Size {size}");
        //          }).Forget();

        //    });

        await UniTask.WaitForSeconds(2.0f);

        //float percent = 0.0f;
        //Global.Scene.Transition<DownloadScene>("DownloadScene", percent, 1.0f, UnityEngine.SceneManagement.LoadSceneMode.Additive,
        //    (result) =>
        //    {
        //        Debug.Log(result);
        //    });
    }


    public override void OnInitialize()
    {
        var cachePaths = new List<string>();
        Caching.GetAllCachePaths(cachePaths);
        foreach (var cachePath in cachePaths)
        {
            Debug.Log($"Cach path : {cachePath}");
        }

        Global.Data.GetTableDatas().Forget();
        Test().Forget();
    }

    private async UniTask Test()
    {
#if UNITY_EDITOR
        FirebaseDB firebaseDB = new FirebaseDB();

        string version = string.Empty;
        await firebaseDB.GetBuildVersion(UnityEditor.BuildTarget.Android.ToString()).ContinueWith(task=>
        {
            if (!task.IsCanceled && !task.IsFaulted)
            {
                version = task.Result;
            }
        });
      
        Debug.LogError(version);
#endif
    }

    public override void OnFinalize()
    {
    }


}
