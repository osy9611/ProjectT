using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public class ProjectTUIPlayModeTests
{
    // EnterPlayMode·ExitPlayMode는 도메인을 다시 로드하고, UTF는 열거자의 위치와 JsonUtility로 저장한 이 객체의 필드만 복원한다.
    // 그래서 리로드를 넘는 값은 직렬화 필드로만 넘기고, JsonUtility가 null 문자열을 ""로 되돌리므로 null을 상태로 쓰지 않는다.
    // 람다로 지역 변수를 캡처하면 첫 yield 전에 만든 클로저 객체가 리로드 뒤 null이 되므로 이 메서드에서는 캡처하지 않는다.
    [SerializeField] private string failure;
    [SerializeField] private bool verified;

    [UnityTest]
    public IEnumerator UIContainerStackAndNativeActivation()
    {
        failure = string.Empty;
        verified = false;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();

        // UIContainer가 포커스 변경을 Global.Input으로 알리므로 실제 매니저를 원격 리소스·데이터 로드 없이 먼저 준비한다.
        var globalObject = new GameObject("Global");
        globalObject.SetActive(false);
        var global = globalObject.AddComponent<ProjectT.Global>();
        global.UseRemoteResource = false;
        global.LoadData = false;
        globalObject.SetActive(true);

        yield return ProjectT.Global.WhenReadyAsync().ToCoroutine(OnInitFailed);
        if (string.IsNullOrEmpty(failure))
        {
            LogAssert.Expect(LogType.Log, "PROJECTT_UI_PLAYMODE_PASS");
            // Play Mode를 빠져나간 뒤 판정해야 하므로 예외를 단언 대신 필드에 남긴다.
            var run = ProjectTUIPlayProbe.Verify();
            while (true)
            {
                bool more;
                try { more = run.MoveNext(); }
                catch (Exception error) { failure = error.ToString(); break; }
                if (!more)
                {
                    verified = true;
                    break;
                }
                yield return run.Current;
            }
        }

        yield return new ExitPlayMode();

        Assert.That(failure, Is.Empty);
        Assert.That(verified, Is.True, "ProjectTUIPlayProbe.Verify did not run to completion");
    }

    private void OnInitFailed(Exception error)
    {
        failure = "Global init: " + error;
    }
}
