using System.Threading;
using Cysharp.Threading.Tasks;
using DesignTable;
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

        protected override UniTask OnInitializeAsync(CancellationToken token)
        {
            CreateRootObject(Context.Root, "SceneRoot");
            return UniTask.CompletedTask;
        }


        protected override void OnShutdown(ShutdownReason reason)
        {
            var errors = new List<System.Exception>();
            var scenes = new HashSet<SceneBase>(pages.Select(p => p.Value));
            if (currentScene != null)
                scenes.Add(currentScene);
            foreach (var scene in scenes.ToArray())
                if (scene != null && scene.SubScenes != null)
                    foreach (var sub in scene.SubScenes) scenes.Add(sub);
            foreach (var scene in scenes)
            {
                if (scene == null)
                    continue;
                try
                {
                    scene.OnFinalize();
                }
                catch (System.Exception error)
                {
                    errors.Add(error);
                }
                try
                {
                    scene.OnExit();
                }
                catch (System.Exception error)
                {
                    errors.Add(error);
                }
            }
            pages.Clear();
            SubScenes.Clear();
            currentScene = null;
            isTrainsioning = false;
            if (errors.Count > 0)
                throw new System.AggregateException(errors);
        }

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
            ThrowIfStopped();
            //Check Resource
            //Unity 6.0으로 넘어오면서 SceneManagement에서 관리하는게 아니라 Addressable에서만 관리하는걸로 변경
            UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (activeScene.name.Equals(resourceName, System.StringComparison.CurrentCultureIgnoreCase) == true)
                resourceName = string.Empty;

            if (!string.IsNullOrEmpty(resourceName))
            {
                SceneDataInfo sceneInfo = Global.Table.SceneDataInfos.Get(resourceName);
                if (sceneInfo == null)
                    Global.Instance.LogError($"[SceneManager] Table Not Found {resourceName}");
                else
                    resourceName = sceneInfo.Path;

            }

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

            await Context.Get<ResourceManager>().ReleaseAllAsync();
            ThrowIfStopped();
            await Resources.UnloadUnusedAssets();
            ThrowIfStopped();
            await UniTask.Yield(cancellationToken: LifetimeToken);

            float currentProgress = 0.0f;
            const float sceneLoadingProgressRate = 0.0f;

            Global.Instance.Log("Next SceneTransition Enter");
            if (!string.IsNullOrEmpty(sceneName))
            {
                UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (activeScene != null)
                    prevResourceName = activeScene.name;

                Global.Instance.Log($"{sceneName} Scene File Inner Object Load");

                await Context.Get<ResourceManager>().LoadSceneAsync(sceneName, UnityEngine.SceneManagement.LoadSceneMode.Single,
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

            ThrowIfStopped();
            currentScene = FindPage(typeof(T).ToString());

            if (currentScene == null)
            {
                currentScene = RootObject.GetOrAddComponent<T>();
                AddPage(currentScene);
            }

            if (currentScene != null)
            {
                await currentScene.OnEnter(currentProgress, data);
                ThrowIfStopped();
                currentScene.OnInitialize();
            }

            await currentScene.LoadAdditiveScene(() =>
            {
                if (State == ManagerState.Ready)
                    completed?.Invoke(eSceneTransitionErrorCode.Success);
            });

            for (int i = 0; i < 3; ++i)
                await UniTask.NextFrame(cancellationToken: LifetimeToken);
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

                await Context.Get<ResourceManager>().LoadSceneAsync(sceneName, UnityEngine.SceneManagement.LoadSceneMode.Additive,
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


            ThrowIfStopped();
            var currentAdditiveScene = RootObject.GetOrAddComponent<T>();
            if (currentAdditiveScene != null)
            {
                await currentAdditiveScene.OnEnter(currentProgress, data);
                ThrowIfStopped();
                currentAdditiveScene.OnInitialize();

                currentScene.SubScenes.Add(currentAdditiveScene);
            }

            if (State == ManagerState.Ready)
                completed?.Invoke(eSceneTransitionErrorCode.Success);

        }

        public void GoTitle()
        {
            float percent = 0.0f;
            Transition<TitleScene>("TitleScene", percent, 1.0f, UnityEngine.SceneManagement.LoadSceneMode.Single,
            (result) =>
            {
                Debug.Log(result);

                Context.Get<ClientLocalStorageManager>().LoadAllData();
            }, null);
        }
    }
}
