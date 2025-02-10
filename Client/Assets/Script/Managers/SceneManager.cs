using Cysharp.Threading.Tasks;
using ProjectT.Scene;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

namespace ProjectT
{
    public enum eSceneTransitionErrorCode
    {
        Success = 0,
        Failure = 1
    }

    public class SceneManager : ManagerBase
    {
        private List<KeyValuePair<string, SceneBase>> pages = new List<KeyValuePair<string, SceneBase>>();

        private SceneBase currentScene = null;

        public SceneBase CurrentScene { get => currentScene; }

        private string prevResourceName = string.Empty;
        private string prevPageTypeName = string.Empty;

        public string PrevSceneName { get => prevPageTypeName; }

        private bool isTrainsioning = false;
        private UniTask m_transitionTask = UniTask.CompletedTask;

        public List<string> SubScenes { get; private set; } = new List<string>();

        private Transform sceneRoot;

#if UNITY_EDITOR
        public List<KeyValuePair<string, SceneBase>> GetPages { get => pages; }
#endif

        #region ManagerBase
        public override void OnEnter()
        {
            CreateRootObject(Global.Instance.transform, "SceneRoot");
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

        public override void OnAppStart()
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

        protected void AddPage(SceneBase scene)
        {
            if (scene == null)
                return;

            pages.Add(new KeyValuePair<string, SceneBase>(scene.GetType().ToString(), scene));
        }

        public SceneBase FindPage(string key)
        {
            if (pages == null || pages.Any() == false)
                return null;

            return pages.FirstOrDefault(x => (x.Key.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0)).Value;
        }

        public T GetScene<T>() where T : SceneBase
        {
            return currentScene as T;
        }

        public bool IsHaveScene<T>() where T : SceneBase
        {
            if (currentScene == null)
                return false;

            return currentScene is T;
        }

        public void Transition<T>(string resourceName, float startLoadingGage, float fadeOutDuration, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode, System.Action<eSceneTransitionErrorCode> completed, params object[] data) where T : SceneBase
        {
            //Check Resource
            UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (activeScene.name.Equals(resourceName, System.StringComparison.CurrentCultureIgnoreCase) == true)
                resourceName = string.Empty;

            //Check Page
            if (currentScene != null)
            {
                if (currentScene.GetType() == typeof(T))
                {
                    completed(eSceneTransitionErrorCode.Failure);
                    return;
                }
            }

            if (loadSceneMode == UnityEngine.SceneManagement.LoadSceneMode.Single)
                OnTransitionTask<T>(resourceName, startLoadingGage, fadeOutDuration, completed, data).Forget();
            else
                OnTransitionTaskAdditive<T>(resourceName, startLoadingGage, fadeOutDuration, completed, true, data).Forget();
        }

        private async UniTask OnTransitionTask<T>(string sceneName, float fadeInDuration, float fadeOutDuration, System.Action<eSceneTransitionErrorCode> completed, params object[] data) where T : SceneBase
        {
            string currentPageType = currentScene != null ? currentScene.GetType().ToString() : string.Empty;
            string nextPageType = typeof(T).ToString();

            isTrainsioning = true;

            Global.Instance.Log("Prev Scene Exit");

            if (currentScene != null)
            {
                prevPageTypeName = currentPageType;

                if (currentScene.SubScenes != null && currentScene.SubScenes.Count > 0)
                {
                    for (int i = 0; i < currentScene.SubScenes.Count; ++i)
                    {
                        currentScene.SubScenes[i].OnFinalize();
                        currentScene.SubScenes[i].OnExit();
                    }

                    currentScene.SubScenes.Clear();
                }

                currentScene.OnFinalize();
                currentScene.OnExit();
                GameObject.Destroy(currentScene);
            }

            await Global.Resource.ReleaseAllAsync();
            await Resources.UnloadUnusedAssets();
            await UniTask.Yield();

            float currentProgress = 0.0f;
            const float sceneLoadingProgressRate = 0.0f;

            Global.Instance.Log("Next SceneTransition Enter");
            if (!string.IsNullOrEmpty(sceneName))
            {
                UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (activeScene != null)
                    prevResourceName = activeScene.name;

                Global.Instance.Log($"{sceneName} Scene File Inner Object Load");

                await Global.Resource.LoadSceneAsync(sceneName, UnityEngine.SceneManagement.LoadSceneMode.Single,
                    (progress) =>
                    {
                        if (progress == 1.0f)
                            Global.Instance.LogWarning("OnTransitionTask => Completed LoadScene.");
                        else
                            Global.Instance.LogWarning($"OnTransitionTask => LoadScene {progress * 100}%");
                    });
            }
            else
                currentProgress = sceneLoadingProgressRate;

            currentScene = FindPage(typeof(T).ToString());

            if (currentScene == null)
            {
                currentScene = RootObject.GetOrAddComponent<T>();
                AddPage(currentScene);
            }

            if (currentScene != null)
            {
                await currentScene.OnEnter(currentProgress, data);
                currentScene.OnInitialize();
            }

            await currentScene.LoadAdditiveScene(() =>
            {
                completed?.Invoke(eSceneTransitionErrorCode.Success);
            });

            for (int i = 0; i < 3; ++i)
                await UniTask.NextFrame(cancellationToken: RootObject.GetCancellationTokenOnDestroy());
        }

        private async UniTask OnTransitionTaskAdditive<T>(string sceneName, float fadeInDuration, float fadeOutDuration, System.Action<eSceneTransitionErrorCode> completed, bool hideLoading = true, params object[] data) where T : SceneBase
        {
            float currentProgress = 0.0f;
            const float sceneLoadingProgressRate = 0.0f;

            if (!string.IsNullOrEmpty(sceneName))
            {
                UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (activeScene != null)
                    prevResourceName = activeScene.name;

                await Global.Resource.LoadSceneAsync(sceneName, UnityEngine.SceneManagement.LoadSceneMode.Additive,
                    (progress) =>
                    {
                        if (progress == 1.0f)
                            Debug.LogWarning("OnTransitionTask => Compelted LoadScene.");
                        else
                            Debug.LogWarning($"OnTransitionTask => LoadScene {progress * 100}%");
                    });
            }
            else
                currentProgress = sceneLoadingProgressRate;


            var currentAdditiveScene = RootObject.GetOrAddComponent<T>();
            if (currentAdditiveScene != null)
            {
                await currentAdditiveScene.OnEnter(currentProgress, data);
                currentAdditiveScene.OnInitialize();

                currentScene.SubScenes.Add(currentAdditiveScene);
            }

            completed?.Invoke(eSceneTransitionErrorCode.Success);

        }

        public void GoTitle()
        {
            float percent = 0.0f;
            Transition<TitleScene>("TitleScene", percent, 1.0f, UnityEngine.SceneManagement.LoadSceneMode.Single,
            (result) =>
            {
                Debug.Log(result);

                Global.LocalStorage.LoadAllData();
            }, null);
        }
    }
}
