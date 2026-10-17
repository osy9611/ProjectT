#if UNITY_EDITOR
using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ProjectTUIPlayMode
{
    private const string Pending = "ProjectTUIPlayMode.Pending";
    static ProjectTUIPlayMode()
    {
        EditorApplication.playModeStateChanged += Changed;
    }

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    private static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false))
            return;
        SessionState.SetBool(Pending, false);
        new GameObject("Test driver").AddComponent<ProjectTUIPlayProbe>().StartCoroutine(RunChecked());
    }

    private static IEnumerator RunChecked()
    {
        // UIContainer가 포커스 변경을 Global.Input으로 알리므로 실제 매니저를 먼저 준비한다.
        // Awake가 AddComponent 시점에 실행되므로 비활성 상태로 추가해 원격 리소스·데이터 로드를 끈 뒤 활성화한다.
        var globalObject = new GameObject("Global");
        globalObject.SetActive(false);
        var global = globalObject.AddComponent<ProjectT.Global>();
        global.UseRemoteResource = false;
        global.LoadData = false;
        globalObject.SetActive(true);

        Exception initError = null;
        yield return ProjectT.Global.WhenReadyAsync().ToCoroutine(error => initError = error);
        if (initError != null) { Debug.LogException(initError); Finish(1); yield break; }

        var run = ProjectTUIPlayProbe.Verify();
        while (true)
        {
            bool more;
            try { more = run.MoveNext(); }
            catch (Exception error) { Debug.LogException(error); Finish(1); yield break; }
            if (!more)
                break;
            yield return run.Current;
        }
        Finish(0);
    }

    // batch 실행은 종료 코드로 결과를 넘기고, 열린 에디터에서는 에디터를 닫지 않고 Play Mode만 끝낸다.
    private static void Finish(int code)
    {
        if (Application.isBatchMode)
            EditorApplication.Exit(code);
        else
            EditorApplication.ExitPlaymode();
    }
}
#endif
