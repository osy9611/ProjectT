using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectT.Sample
{
    // SceneData 테이블의 SampleSceneA/B로 Single·Additive 전환, 진입 실패, 전환 중 재요청을 확인한다.
    public class SceneSampleRunner : MonoBehaviour
    {
        private const float Width = 330f;
        private const float Top = 360f;
        private const float EnterDelay = 1f;

        private bool ready;

        private void Awake()
        {
            // 샘플 패널이 Single 전환으로 언로드되지 않도록 Global처럼 씬 밖에 둔다.
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            InitializeInternalAsync().Forget(Global.LogException);
        }

        private async UniTask InitializeInternalAsync()
        {
            var token = this.GetCancellationTokenOnDestroy();
            if (await Global.Instance.WhenReadyAsync(token).SuppressCancellationThrow())
                return;

            ready = true;
        }

        private void OnGUI()
        {
            if (!ready)
                return;

            var scene = Global.Scene;
            GUILayout.BeginArea(new Rect(Screen.width - Width - 10, Top, Width, Screen.height - Top - 10), GUI.skin.box);
            GUILayout.Label("[Scene]");
            GUI.enabled = !scene.IsTransitioning;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Single A"))
                TransitionInternal<SampleSceneA>("SampleSceneA", LoadSceneMode.Single, false);

            if (GUILayout.Button("Single B"))
                TransitionInternal<SampleSceneB>("SampleSceneB", LoadSceneMode.Single, false);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Additive B"))
                TransitionInternal<SampleSceneB>("SampleSceneB", LoadSceneMode.Additive, false);

            if (GUILayout.Button("Unload Additive B"))
                UnloadAdditiveInternalAsync().Forget(Global.LogException);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Single A (Enter Fail)"))
                TransitionInternal<SampleSceneA>("SampleSceneA", LoadSceneMode.Single, true);

            if (GUILayout.Button("Double Request"))
                DoubleRequestInternal();
            GUILayout.EndHorizontal();

            GUI.enabled = true;
            var current = scene.CurrentScene;
            GUILayout.Label($"Transitioning: {scene.IsTransitioning}, Input: {scene.IsInputAllowed}");
            GUILayout.Label($"Current: {(current == null ? "None" : current.GetType().Name)}, Prev: {scene.PrevSceneName}");
            GUILayout.Label($"Additive B: {scene.IsHaveScene<SampleSceneB>() && !(current is SampleSceneB)}");
            GUILayout.Label($"Unity active: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, loaded {UnityEngine.SceneManagement.SceneManager.sceneCount}");
            GUILayout.EndArea();
        }

        private void TransitionInternal<T>(string name, LoadSceneMode mode, bool fail) where T : Scene.SceneBase
        {
            FeatureSampleRunner.Write($"Transition {name} {mode}{(fail ? " (fail)" : "")}");
            Global.Scene.Transition<T>(name, mode, result => FeatureSampleRunner.Write($"Transition {name} result: {result}"), EnterDelay, fail);
        }

        // 전환 중 두 번째 요청은 거부되어야 하며, 거부 결과는 완료 콜백의 Failure로 전달된다.
        private void DoubleRequestInternal()
        {
            TransitionInternal<SampleSceneA>("SampleSceneA", LoadSceneMode.Single, false);
            TransitionInternal<SampleSceneB>("SampleSceneB", LoadSceneMode.Single, false);
        }

        private async UniTask UnloadAdditiveInternalAsync()
        {
            FeatureSampleRunner.Write("UnloadAdditive SampleSceneB");
            await Global.Scene.UnloadAdditiveAsync<SampleSceneB>();
            FeatureSampleRunner.Write("UnloadAdditive SampleSceneB done");
        }
    }
}
