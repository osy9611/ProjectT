using ProjectT;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneNavigator : MonoBehaviour
{
    public void GoTitle()
    {
        Global.Scene.Transition<TitleScene>("TitleScene", LoadSceneMode.Single, _ => { });
    }

    public void GoDownload()
    {
        Global.Scene.Transition<DownloadScene>("DownloadScene", LoadSceneMode.Single, result =>
        {
            if(result==eSceneTransitionResult.Success)
            {
                Global.Log("[TEST] GoDownload");
            }
        });
    }
}
