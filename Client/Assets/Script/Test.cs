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
using UnityEngine.SceneManagement;

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

    async UniTask TESTTask()
    {
        await UniTask.Yield();
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


    public void OpenUI()
    {
        OpenUIInternalAsync().Forget(Global.LogException);
    }

    private async UniTask OpenUIInternalAsync()
    {
        var token = this.GetCancellationTokenOnDestroy();
        if (await Global.Instance.WhenReadyAsync(token).SuppressCancellationThrow())
            return;

        var (canceled, widget) = await Global.UI.CreateWidgetAsync<TestUI>(
            UIDefine.eUIType.Test, token).SuppressCancellationThrow();

        if (canceled || token.IsCancellationRequested || widget == null)
            return;

        widget.Show();
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

    public void GoTitle()
    {
        Global.Scene.Transition<TitleScene>("TitleScene", LoadSceneMode.Single, result =>
        {
            if (result == eSceneTransitionErrorCode.Success)
                Global.LocalStorage.LoadAllData();
        });
    }

    public void GoDownload()
    {
        Global.Scene.Transition<DownloadScene>("DownloadScene", LoadSceneMode.Single, result =>
        {
            if(result==eSceneTransitionErrorCode.Success)
            {
                Global.Instance.Log("[TEST] GoDownload");
            }
        });
    }
}
