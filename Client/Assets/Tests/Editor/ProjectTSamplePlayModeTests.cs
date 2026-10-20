using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ProjectT;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

// 샘플 씬을 열고 Play Mode에서 프로브를 끝까지 실행한 뒤, Play Mode를 빠져나와 결과 수를 단언한다.
public class ProjectTSamplePlayModeTests
{
    private const float ReadyTimeout = 60f;
    private const float ProbeTimeout = 300f;

    // EnterPlayMode·ExitPlayMode는 도메인을 다시 로드하고, UTF는 열거자의 위치와 JsonUtility로 저장한 이 객체의 필드만 복원한다.
    // 그래서 EnterPlayMode·ExitPlayMode는 테스트 메서드 최상위에만 두고, 리로드를 넘는 값은 직렬화 필드로만 넘긴다.
    // JsonUtility가 null 문자열을 ""로 되돌리므로 null을 상태로 쓰지 않는다. 지역 변수를 캡처하는 람다도 쓰지 않는다.
    [SerializeField] private string failure;
    [SerializeField] private string summary;
    [SerializeField] private string failedChecks;

    [UnityTest, Timeout(600000)]
    public IEnumerator P0FeatureSample()
    {
        Begin("Assets/Scenes/Sample/FeatureSample.unity");
        yield return new EnterPlayMode();
        yield return RunP0Internal();
        yield return new ExitPlayMode();
        AssertSummary("pass=103 fail=0");
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator TurnBattleSample()
    {
        Begin("Assets/Scenes/Sample/TurnBattleSample.unity");
        yield return new EnterPlayMode();
        yield return RunTurnBattleInternal();
        yield return new ExitPlayMode();
        AssertSummary("pass=61 fail=0");
    }

    private void Begin(string scenePath)
    {
        failure = string.Empty;
        summary = string.Empty;
        failedChecks = string.Empty;
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
    }

    // Play Mode 안에서는 리로드가 없으므로 중첩 열거자와 지역 변수를 써도 된다.
    private IEnumerator RunP0Internal()
    {
        float deadline = Time.realtimeSinceStartup + ReadyTimeout;
        while (!Global.IsReady && Global.State != ManagerState.Failed && Time.realtimeSinceStartup < deadline)
            yield return null;

        if (!Global.IsReady)
        {
            failure = $"Global not ready ({Global.State})";
            yield break;
        }

        // Double Request 거부와 Enter Fail은 의도한 실패를 Global.LogException으로 보고한다.
        LogAssert.Expect(LogType.Exception, new Regex(@"Scene transition is unavailable\."));
        LogAssert.Expect(LogType.Exception, new Regex(@"\[Sample\] SampleSceneB OnEnter failure test"));
        ProjectTP0PlayProbe.Run();
        deadline = Time.realtimeSinceStartup + ProbeTimeout;
        while (ProjectTP0PlayProbe.Running && Time.realtimeSinceStartup < deadline)
            yield return null;

        Capture(ProjectTP0PlayProbe.Running, ProjectTP0PlayProbe.Summary, ProjectTP0PlayProbe.Results);
    }

    private IEnumerator RunTurnBattleInternal()
    {
        // 프로브가 TurnBattleRunner 준비를 직접 기다린다.
        ProjectTTurnBattleProbe.Run();
        float deadline = Time.realtimeSinceStartup + ProbeTimeout;
        while (ProjectTTurnBattleProbe.Running && Time.realtimeSinceStartup < deadline)
            yield return null;

        Capture(ProjectTTurnBattleProbe.Running, ProjectTTurnBattleProbe.Summary, ProjectTTurnBattleProbe.Results);
    }

    private void Capture(bool running, string probeSummary, IReadOnlyList<string> results)
    {
        if (running)
            failure = "Probe timed out";

        summary = probeSummary;
        var failed = new List<string>();
        foreach (var line in results)
        {
            if (line.Contains("] FAIL ") || line.Contains("] ERROR LOG "))
                failed.Add(line);
        }

        failedChecks = string.Join("\n", failed);
    }

    private void AssertSummary(string expected)
    {
        Assert.That(failure, Is.Empty);
        Assert.That(summary, Is.EqualTo(expected), failedChecks);
    }
}
