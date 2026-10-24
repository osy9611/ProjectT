using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

// 빈 씬에 Global을 띄우고 Play Mode에서 픽스처 프로브를 끝까지 실행한 뒤, Play Mode를 빠져나와 결과 수를 단언한다.
public class ProjectTRuntimePlayModeTests
{
    private const float ProbeTimeout = 300f;

    // EnterPlayMode·ExitPlayMode는 도메인을 다시 로드하고, UTF는 열거자의 위치와 JsonUtility로 저장한 이 객체의 필드만 복원한다.
    // 그래서 EnterPlayMode·ExitPlayMode는 테스트 메서드 최상위에만 두고, 리로드를 넘는 값은 직렬화 필드로만 넘긴다.
    // JsonUtility가 null 문자열을 ""로 되돌리므로 null을 상태로 쓰지 않는다. 지역 변수를 캡처하는 람다도 쓰지 않는다.
    [SerializeField] private string failure;
    [SerializeField] private string summary;
    [SerializeField] private string failedChecks;

    [UnityTest, Timeout(600000)]
    public IEnumerator RuntimeFixtures()
    {
        failure = string.Empty;
        summary = string.Empty;
        failedChecks = string.Empty;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        yield return RunInternal();
        yield return new ExitPlayMode();
        Assert.That(failure, Is.Empty);
        Assert.That(summary, Is.EqualTo("pass=98 fail=0"), failedChecks);
    }

    // Play Mode 안에서는 리로드가 없으므로 중첩 열거자와 지역 변수를 써도 된다.
    private IEnumerator RunInternal()
    {
        // 스킬 테이블과 SceneData가 필요하므로 데이터는 읽고, 씬과 리소스는 에디터의 Addressables로 읽는다.
        var globalObject = new GameObject("Global");
        globalObject.SetActive(false);
        var global = globalObject.AddComponent<ProjectT.Global>();
        global.UseRemoteResource = false;
        global.LoadData = true;
        globalObject.SetActive(true);

        yield return ProjectT.Global.WhenReadyAsync().ToCoroutine(OnInitFailed);
        if (!string.IsNullOrEmpty(failure))
            yield break;

        // Double Request 거부와 Enter Fail은 의도한 실패를 Global.LogException으로 보고한다.
        LogAssert.Expect(LogType.Exception, new Regex(@"Scene transition is unavailable\."));
        LogAssert.Expect(LogType.Exception, new Regex(Regex.Escape(ProjectTRuntimePlayProbe.EnterFailureMessage)));
        ProjectTRuntimePlayProbe.Run();
        float deadline = Time.realtimeSinceStartup + ProbeTimeout;
        while (ProjectTRuntimePlayProbe.Running && Time.realtimeSinceStartup < deadline)
            yield return null;

        if (ProjectTRuntimePlayProbe.Running)
            failure = "Probe timed out";

        summary = ProjectTRuntimePlayProbe.Summary;
        var failed = new List<string>();
        foreach (var line in ProjectTRuntimePlayProbe.Results)
        {
            if (line.Contains("] FAIL ") || line.Contains("] ERROR LOG "))
                failed.Add(line);
        }

        failedChecks = string.Join("\n", failed);
    }

    private void OnInitFailed(Exception error)
    {
        failure = "Global init: " + error;
    }
}
