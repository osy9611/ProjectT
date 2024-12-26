using Cysharp.Threading.Tasks;
using Firebase.Extensions;
using Firebase.Storage;
using ProjectT;
using ProjectT.Addressable;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using System.Threading.Tasks;
using ProjectT.UGUI;
using UnityEngine.UI;

public class Test : MonoBehaviour
{
    FirebaseStorage storage;
    StorageReference storageRef;
    public GameObject testObj;

    public AssetLabelReference defaultLabel;
    public AssetLabelReference matLabel;

    private long patchSize;
    public Dictionary<string, long> patchMap = new Dictionary<string, long>();

    public Image sprite;

    private void Awake()
    {
        Tests();
        //string profileName = "FireBaseBuild";
//#if UNITY_EDITOR

//        UploadData data = new UploadData();
//        data.SetData("AOS", "0.0.1");

//        FirebaseUploader uploader = new FirebaseUploader();
//        Task t = uploader.UploadFile(data, null);
//        t.Start();
//        t.Wait();
//#endif
    }

    private void Tests()
    {

        Debug.Log($"Current cach : {Caching.defaultCache.path}");

        var cachePaths = new List<string>();
        Caching.GetAllCachePaths(cachePaths);
        foreach (var cachePath in cachePaths)
        {
            Debug.Log($"Cach path : {cachePath}");
        }


        Addressables.ResourceManager.ResourceProviders.Add(new FirebaseStorageAssetBundleProvider());
        Addressables.ResourceManager.ResourceProviders.Add(new FirebaseStorageJsonAssetProvider());
        Addressables.ResourceManager.ResourceProviders.Add(new FirebaseStorageHashProvider());

        Addressables.InternalIdTransformFunc += FirebaseAddressablesCache.IdTransformFunc;
        const string downloadAssetKey = "default";
        FirebaseAddressablesCache.PreWarmDependencies(downloadAssetKey,
            () =>
            { 

                Downloader.GetDownloadSize(new List<string>() { "default" },
                    (result, size) =>
                    {
                        Debug.Log($"Result {result} Size {size}");
                        //Downloader.Download(new List<string>() { "default" },
                        //    (type, info) =>
                        //    {
                        //        Debug.Log($"Type {type}, Info {info.progress}");
                        //    }).Forget();
                    }).Forget();

                //var handler = Addressables.GetDownloadSizeAsync(downloadAssetKey);

                //handler.Completed += handle =>
                //{
                //    if (handle.Status == AsyncOperationStatus.Failed)
                //    {
                //        Debug.LogError($"Get Download size failed because of error: {handle.OperationException}");
                //    }
                //    else
                //    {
                //        Debug.Log($"Got download size of: {handle.Result}");
                //    }

                //    Addressables.DownloadDependenciesAsync(downloadAssetKey).Completed +=
                //        operationHandle =>
                //        {
                //            var dependencyList = (List<IAssetBundleResource>)operationHandle.Result;
                //            foreach (IAssetBundleResource resource in dependencyList)
                //            {
                //                AssetBundle assetBundle = resource.GetAssetBundle();
                //                Debug.Log($"Downloaded dependency: {assetBundle}");
                //            }
                //        };
                //};
            });

        // Make sure to continue on MAIN THREAD for addressables initialization
        Firebase.FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => {
            var dependencyStatus = task.Result;
            if (dependencyStatus == Firebase.DependencyStatus.Available)
            {
                // Create and hold a reference to your FirebaseApp,
                // where app is a Firebase.FirebaseApp property of your application class.
                //   app = Firebase.FirebaseApp.DefaultInstance;

                Debug.Log("FIREBASE INIT FINISHED");
                FirebaseAddressablesManager.IsFirebaseSetupFinished = true;

                // Set a flag here to indicate whether Firebase is ready to use by your app.
            }
            else
            {
                UnityEngine.Debug.LogError(System.String.Format(
                    "Could not resolve all Firebase dependencies: {0}", dependencyStatus));
                // Firebase Unity SDK is not safe to use here.
            }
        });

        Debug.Log("Getting download size");
    }


    // Start is called before the first frame update
    void Start()
    {
        //OpenUI();
        //if (Application.isPlaying)
        //{
        //    // Firebase 초기화 및 관련 작업을 Start에서 수행
        //    FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        //    {
        //        FirebaseStorage storage = FirebaseStorage.DefaultInstance;

        //        StorageReference storageRef = storage.GetReferenceFromUrl("gs://projectt-1179c.appspot.com/Android");
        //        Debug.Log("Bucket : " + storageRef.Bucket);
        //        Debug.Log("Path : " + storageRef.Path);
        //        Debug.Log("Storage : " + storageRef.Storage);
        //        // Firebase와 관련된 작업 수행

        //        StartCoroutine(InitAddressable());
        //        StartCoroutine(CheckUpdateFile());
        //    });
        //}        //if (Application.isPlaying)
        //{
        //    // Firebase 초기화 및 관련 작업을 Start에서 수행
        //    FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        //    {
        //        FirebaseStorage storage = FirebaseStorage.DefaultInstance;

        //        StorageReference storageRef = storage.GetReferenceFromUrl("gs://projectt-1179c.appspot.com/Android");
        //        Debug.Log("Bucket : " + storageRef.Bucket);
        //        Debug.Log("Path : " + storageRef.Path);
        //        Debug.Log("Storage : " + storageRef.Storage);
        //        // Firebase와 관련된 작업 수행

        //        StartCoroutine(InitAddressable());
        //        StartCoroutine(CheckUpdateFile());
        //    });
        //}
        //FireBaseInit();


    }
    IEnumerator InitAddressable()
    {
        var init = Addressables.InitializeAsync();
        yield return init;
    }

    IEnumerator CheckUpdateFile()
    {
        var labels = new List<string>() { "default" };

        patchSize = default;

        foreach (var label in labels)
        {
            var handle = Addressables.GetDownloadSizeAsync(label);

            yield return handle;

            patchSize += handle.Result;
        }

        Debug.Log(patchSize);
    }

    public void StartScene()
    {
        float percent = 0.0f;

        Global.Scene.Transition<TestScene2>("TestScene2", percent, 1.0f, UnityEngine.SceneManagement.LoadSceneMode.Single,
            (result) =>
            {
                Debug.Log(result);
            }, null);
    }

    public void OpenUI()
    {
        Global.UI.CreateWidget<TestUI>(UIDefine.eUIType.Test);
    }


    public void AddSpriteAtlas()
    {
        //Global.Atlas.Add("Assets/BundleRes/SpriteAtlas/NewSpriteAtlas.spriteatlas");
    }

    public void SetSprite()
    {
        UtilFunc.LoadAtlasAndImage(sprite, DesignEnum.AtlasType.Common,"Skill3");

        //Global.Atlas.LoadAtlasAndImage(sprite, "Skill3");
    }

}
