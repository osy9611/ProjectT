#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Sample;
using ProjectT.SkillAdapter;
using ProjectT.UGUI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;
using UnityEngine.UI;

// FeatureSample 씬의 샘플 버튼 경로(Actor·Pool, Input, UI, Scene)를 실제 Play Mode에서 순서대로 실행하고 결과를 콘솔에 남긴다.
// 샘플 버튼 함수는 private이므로 리플렉션으로 같은 함수를 호출한다. 입력은 가상 Gamepad 이벤트로 넣는다.
public class ProjectTP0PlayProbe : MonoBehaviour
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly List<string> s_results = new List<string>();
    private readonly List<string> sampleLines = new List<string>();
    private readonly List<string> errors = new List<string>();
    private readonly List<string> expectedErrors = new List<string>();
    private int pass;
    private int fail;
    private FeatureSampleRunner runner;
    private Gamepad pad;
    private InputSettings originalSettings;

    // 씬 배치 Actor 계약: 씬 정지 뒤에도 언로드가 파괴할 때까지 갱신되고, 입력은 전환 차단으로 막힌다.
    private sealed class PlacedActorProbe : ComBaseActor
    {
        public ProjectT.Scene.SceneBase Watched;
        public readonly List<string> Trace = new List<string>();
        public int UpdatesAfterStop;
        public bool InputAllowedAfterStop;

        // 정지된 SceneBase는 같은 프레임 끝에 파괴되므로 null도 정지 뒤로 본다.
        private bool SceneStopped => Watched == null || Watched.State == ProjectT.Scene.SceneState.Stopped;

        protected override void OnEnter() => Trace.Add("enter");

        protected override void OnUpdate(float dt)
        {
            if (!SceneStopped)
                return;

            UpdatesAfterStop++;
            InputAllowedAfterStop |= Global.Scene.IsInputAllowed;
        }

        protected override void Enable() => Trace.Add("enable");

        protected override void Disable() => Trace.Add(SceneStopped ? "disable-after-stop" : "disable");

        protected override void OnRelease() => Trace.Add("release");
    }

    public static bool Running { get; private set; }
    public static string Summary { get; private set; } = "not started";
    public static IReadOnlyList<string> Results => s_results;

    public static void Run()
    {
        if (Running)
            throw new InvalidOperationException("Probe is already running.");

        s_results.Clear();
        Summary = "running";
        Running = true;
        var host = new GameObject("ProjectTP0PlayProbe");
        DontDestroyOnLoad(host);
        host.AddComponent<ProjectTP0PlayProbe>();
    }

    private IEnumerator Start()
    {
        Application.logMessageReceived += OnLogInternal;
        try
        {
            runner = FindAnyObjectByType<FeatureSampleRunner>();
            Check(runner != null, "setup", "FeatureSampleRunner exists");
            Check(Global.IsReady, "setup", $"Global Ready ({Global.State})");

            // 게임 뷰 포커스가 없으면 ResetAndDisableNonBackgroundDevices가 가상 장치를 끄고, 동기 InputSystem.Update()가 Editor 업데이트로 실행되어 Gamepad 이벤트를 처리하지 않는다.
            // 포커스는 실행 중에도 바뀌므로 시작 시점과 관계없이 프로브 동안 두 설정을 모두 바꾼 복제본을 쓴다.
            originalSettings = InputSystem.settings;
            var copy = ScriptableObject.Instantiate(originalSettings);
            copy.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            copy.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = copy;

            yield return Step("actor", ActorInternal());
            yield return Step("skill", SkillInternal());
            yield return Step("input", InputInternal());
            yield return Step("ui", UIInternal());
            yield return Step("scene", SceneInternal());
            yield return Step("cleanup", CleanupInternal());
            yield return Step("ui-probe", UIProbeInternal());
        }
        finally
        {
            Application.logMessageReceived -= OnLogInternal;
            if (pad != null && pad.added)
                InputSystem.RemoveDevice(pad);

            if (originalSettings != null && !ReferenceEquals(InputSystem.settings, originalSettings))
                InputSystem.settings = originalSettings;
        }

        foreach (var error in expectedErrors)
            Note("console", $"expected error log: {error}");

        foreach (var error in errors)
            Record($"[P0] ERROR LOG {error}");

        Check(errors.Count == 0, "console", $"no Error/Exception logs during probe ({errors.Count})");
        Summary = $"pass={pass} fail={fail}";
        Debug.Log($"PROJECTT_P0_PLAYMODE_RESULT {Summary}");
        Running = false;
        Destroy(gameObject);
    }

    // 한 단계가 예외로 끝나도 다음 단계를 계속 실행해 전체 결과를 남긴다.
    private IEnumerator Step(string group, IEnumerator routine)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(routine);
        while (stack.Count > 0)
        {
            object current;
            try
            {
                if (!stack.Peek().MoveNext())
                {
                    stack.Pop();
                    continue;
                }

                current = stack.Peek().Current;
            }
            catch (Exception error)
            {
                Check(false, group, $"step threw {error.GetType().Name}: {error.Message}");
                break;
            }

            if (current is IEnumerator nested)
            {
                stack.Push(nested);
                continue;
            }

            yield return current;
        }
    }

    private IEnumerator ActorInternal()
    {
        Call(runner, "SpawnInternal");
        Call(runner, "SpawnInternal");
        Call(runner, "SpawnInternal");
        var actors = Spawned();
        Check(actors.Count == 3, "actor", $"Spawn x3 -> spawned {actors.Count}");
        foreach (var actor in actors)
            Check(State(actor) == "Active" && actor.gameObject.activeSelf && ContextOwned(actor), "actor", $"{actor.name} Id{actor.Id} Active with registered input context ({State(actor)})");

        Check(ControllerContextCount() == 3, "actor", $"controller contexts after spawn = {ControllerContextCount()}");
        Check(actors[0].Id != actors[1].Id && actors[1].Id != actors[2].Id, "actor", "OnEnter assigns distinct Id");

        var last = actors[2];
        Call(runner, "ToggleLastInternal");
        Check(!last.gameObject.activeSelf && State(last) == "Entered" && !ContextOwned(last), "actor", $"Toggle off -> Entered, context detached ({State(last)})");
        Check(ControllerContextCount() == 2, "actor", $"controller contexts after toggle off = {ControllerContextCount()}");
        Check(HasLine($"Actor{last.Id} Disable"), "actor", "Toggle off calls Disable");

        Call(runner, "ToggleLastInternal");
        Check(last.gameObject.activeSelf && State(last) == "Active" && ContextOwned(last), "actor", $"Toggle on -> Active, context re-registered ({State(last)})");
        Check(ControllerContextCount() == 3, "actor", $"controller contexts after toggle on = {ControllerContextCount()}");

        int oldId = last.Id;
        Call(runner, "RespawnInternal");
        actors = Spawned();
        var respawned = actors[actors.Count - 1];
        Check(HasLine($"Actor{oldId} OnRelease"), "actor", "Respawn returns last (OnRelease)");
        Check(actors.Count == 3 && State(respawned) == "Active" && respawned.Id > oldId, "actor", $"Respawn rents new spawn Id{respawned.Id} Active (reused instance: {ReferenceEquals(respawned, last)})");
        Check(ControllerContextCount() == 3, "actor", $"controller contexts after respawn = {ControllerContextCount()}");

        var returned = actors[actors.Count - 1];
        Call(runner, "ReturnLastInternal");
        Check(!returned.gameObject.activeSelf && State(returned) == "None" && !ContextOwned(returned), "actor", $"Return Last -> inactive, None, context detached ({State(returned)})");
        Check(Spawned().Count == 2 && ControllerContextCount() == 2, "actor", $"after Return Last spawned {Spawned().Count}, contexts {ControllerContextCount()}");

        Call(runner, "SpawnInternal");
        Check(ReferenceEquals(Spawned()[2], returned) && State(returned) == "Active", "actor", "Spawn after Return reuses pooled instance");
        yield return null;
        Check(errors.Count == 0, "actor", "no errors during actor steps");
    }

    // 테이블 스킬 201(쿨다운 3초), 버프 1(공격력 +20%, 10초), 버프 5(스턴 5초)를 마지막 Actor에 사용한다.
    private IEnumerator SkillInternal()
    {
        var actor = Spawned()[Spawned().Count - 1];
        var status = actor.Status;
        int id = actor.Id;
        string activatedLine = $"Actor{id} Skill {SampleActor.SkillId} Activated";
        Check(status.GetResource(SkillDefinitions.HpStat) == SampleActor.BaseHp && status.Get(SkillDefinitions.MaxHpStat) == SampleActor.BaseHp && status.Get(SkillDefinitions.AtkStat) == SampleActor.BaseAtk,
            "skill", $"Spawn sets Hp {status.GetResource(SkillDefinitions.HpStat)}/{status.Get(SkillDefinitions.MaxHpStat)}, Atk {status.Get(SkillDefinitions.AtkStat)}");

        int activated = CountLines(activatedLine);
        Call(runner, "UseSkillLastInternal");
        Check(CountLines(activatedLine) == activated + 1 && HasLine($"Actor{id} Skill {SampleActor.SkillId} Ended Completed"), "skill", "Use Skill activates and completes");

        Call(runner, "UseSkillLastInternal");
        Check(CountLines(activatedLine) == activated + 1, "skill", "Use Skill during cooldown is ignored");

        yield return new WaitForSeconds(3.2f);
        Call(runner, "UseSkillLastInternal");
        Check(CountLines(activatedLine) == activated + 2, "skill", "Use Skill after cooldown activates");

        Call(runner, "ApplyEffectLastInternal", 1);
        Check(HasLine($"Actor{id} Effect 1 Applied") && Mathf.Approximately(status.Get(SkillDefinitions.AtkStat), SampleActor.BaseAtk * 1.2f), "skill", $"Atk Buff applies modifier (Atk {status.Get(SkillDefinitions.AtkStat)})");

        Call(runner, "ApplyEffectLastInternal", 5);
        Check(actor.SkillAgent.HasTag(SkillDefinitions.StunTag) && HasLine($"Actor{id} Tag State.Stun Added"), "skill", "Stun grants State.Stun");

        yield return new WaitForSeconds(3.2f);
        activated = CountLines(activatedLine);
        Call(runner, "UseSkillLastInternal");
        Check(CountLines(activatedLine) == activated, "skill", "Use Skill while stunned is blocked");

        yield return new WaitForSeconds(2.2f);
        Check(!actor.SkillAgent.HasTag(SkillDefinitions.StunTag) && HasLine($"Actor{id} Effect 5 Removed Expired") && HasLine($"Actor{id} Tag State.Stun Removed"), "skill", "Stun expires and removes State.Stun");

        Call(runner, "UseSkillLastInternal");
        Check(CountLines(activatedLine) == activated + 1, "skill", "Use Skill after stun activates");

        // Atk 버프(10초)가 남은 상태로 반납·재스폰해 이전 스폰의 효과와 스탯이 넘어가지 않는지 본다.
        Call(runner, "RespawnInternal");
        var respawned = Spawned()[Spawned().Count - 1];
        Check(ReferenceEquals(respawned, actor) && respawned.Id != id, "skill", $"Respawn reuses instance with new Id{respawned.Id}");
        Check(status.Get(SkillDefinitions.AtkStat) == SampleActor.BaseAtk && status.GetResource(SkillDefinitions.HpStat) == SampleActor.BaseHp && !respawned.SkillAgent.HasTag(SkillDefinitions.StunTag),
            "skill", $"Respawn resets Atk {status.Get(SkillDefinitions.AtkStat)}, Hp {status.GetResource(SkillDefinitions.HpStat)}, no tags");

        Call(runner, "UseSkillLastInternal");
        Check(HasLine($"Actor{respawned.Id} Skill {SampleActor.SkillId} Activated"), "skill", "Respawned actor re-registers skill without cooldown carry-over");
        yield return null;
        Check(errors.Count == 0, "skill", "no errors during skill steps");
    }

    private IEnumerator InputInternal()
    {
        var input = Global.Input;
        var actors = Spawned();
        pad = InputSystem.AddDevice<Gamepad>("P0ProbePad");
        yield return null;

        // 페어링되지 않은 장치의 버튼 입력은 장치 전환으로 처리되고, 전환 프레임의 게임 입력은 막힌다.
        int[] before = JumpCounts(actors);
        yield return PressSouthInternal();
        Check(ReferenceEquals(input.CurrentDevice, pad), "input", $"South press on unpaired pad switches device ({input.CurrentDevice?.displayName})");
        Check(HasLine("Notify InputControlsChanged"), "input", "InputControlsChanged published");
        Check(SameCounts(before, JumpCounts(actors)), "input", "switching press is not delivered as Jump");

        before = JumpCounts(actors);
        yield return PressSouthInternal();
        Check(AllIncreased(before, JumpCounts(actors)), "input", $"Jump delivered to all {actors.Count} actors (consume=false)");

        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(0f, 1f) });
        yield return null;
        yield return null;
        bool moving = true;
        foreach (var actor in actors)
            moving &= actor.MoveDirection.y > 0.5f;
        Check(moving, "input", "Move stick delivered to actors");

        var last = actors[actors.Count - 1];
        last.gameObject.SetActive(false);
        Check(last.MoveDirection == Vector2.zero, "input", $"Disable resets Move via onReset ({last.MoveDirection})");
        last.gameObject.SetActive(true);
        yield return null;
        // 계약: 등록 시점에 이미 진행 중인 입력은 다시 전달하지 않고 값이 바뀔 때부터 전달한다.
        Check(last.MoveDirection == Vector2.zero, "input", $"Re-enable while stick held waits for the next stick change ({last.MoveDirection})");
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(0f, 0.6f) });
        yield return null;
        yield return null;
        Check(last.MoveDirection.y > 0.4f, "input", $"Next stick change reaches the re-enabled actor ({last.MoveDirection})");
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return null;
        yield return null;
        bool stopped = true;
        foreach (var actor in actors)
            stopped &= actor.MoveDirection == Vector2.zero;
        Check(stopped, "input", "stick release cancels Move");

        var blocker = (ProjectT.Controller.InputContext)Get(runner, "blocker");
        input.Controller.AddContext(blocker);
        before = JumpCounts(actors);
        int blockerLines = CountLines("Blocker consumed Jump");
        yield return PressSouthInternal();
        Check(SameCounts(before, JumpCounts(actors)) && CountLines("Blocker consumed Jump") == blockerLines + 1, "input", "priority 100 blocker consumes Jump");
        input.Controller.RemoveContext(blocker);
        before = JumpCounts(actors);
        yield return PressSouthInternal();
        Check(AllIncreased(before, JumpCounts(actors)), "input", "Jump reaches actors after blocker removed");

        input.SetInputAllowed(false);
        before = JumpCounts(actors);
        yield return PressSouthInternal();
        Check(SameCounts(before, JumpCounts(actors)), "input", "SetInputAllowed(false) blocks Jump");
        input.SetInputAllowed(true);
        before = JumpCounts(actors);
        yield return PressSouthInternal();
        Check(AllIncreased(before, JumpCounts(actors)), "input", "SetInputAllowed(true) restores Jump");

        InputSystem.RemoveDevice(pad);
        yield return null;
        Check(input.IsDeviceLost, "input", "removing paired pad -> IsDeviceLost");
        Check(HasLine("Notify InputDeviceLost"), "input", "InputDeviceLost published");
        Check(!(bool)Get(input.Controller, "inputAllowed"), "input", "device lost blocks controller input");

        InputSystem.AddDevice(pad);
        yield return null;
        Check(!input.IsDeviceLost && ReferenceEquals(input.CurrentDevice, pad), "input", $"re-adding pad -> regained ({input.CurrentDevice?.displayName})");
        Check(HasLine("Notify InputDeviceRegained"), "input", "InputDeviceRegained published");
        before = JumpCounts(actors);
        yield return PressSouthInternal();
        Check(AllIncreased(before, JumpCounts(actors)), "input", "Jump works after regain");
    }

    private IEnumerator UIInternal()
    {
        var ui = Global.UI;
        var actors = Spawned();

        ui.CreateWidget<SampleUI>(UIDefine.eUIType.SampleStaticA).Show();
        var a = ui.FindWidget<SampleUI>(UIDefine.eUIType.SampleStaticA);
        Check(a != null && a.IsActive && ReferenceEquals(ui.TopWidget, a), "ui", "Static A shown and on top");
        yield return null;
        Check(!(bool)Get(Global.Input.Controller, "inputAllowed"), "ui", "focused Static UI blocks game input");
        int[] before = JumpCounts(actors);
        yield return PressSouthInternal();
        Check(SameCounts(before, JumpCounts(actors)), "ui", "Jump not delivered while Static UI focused");
        // 포커스된 Static UI에서는 같은 South 입력이 UI/Submit으로 선택된 버튼을 누른다.
        var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        Note("ui", $"after South on focused A: selected {selected?.name ?? "None"}, top {ui.TopWidget?.name ?? "None"}, A active {a.gameObject.activeSelf}");
        if (!ReferenceEquals(ui.TopWidget, a) || !a.gameObject.activeSelf)
        {
            ui.RemoveWidget(UIDefine.eUIType.SampleStaticB);
            a.Show();
        }

        Check(ReferenceEquals(ui.TopWidget, a) && a.gameObject.activeSelf, "ui", "Static A on top before Next");

        ClickInternal(a, "NextButton");
        var b = ui.FindWidget<SampleUI>(UIDefine.eUIType.SampleStaticB);
        Check(b != null && b.IsActive && ReferenceEquals(ui.TopWidget, b) && !a.gameObject.activeSelf, "ui", "Next opens Static B and hides A");

        ClickInternal(b, "CloseButton");
        Check(ReferenceEquals(ui.TopWidget, a) && a.gameObject.activeSelf, "ui", "Close B restores A");

        ClickInternal(a, "CloseButton");
        Check(ui.TopWidget == null || !ui.TopWidget.gameObject.activeSelf, "ui", $"Close A empties visible stack (top {ui.TopWidget?.name ?? "None"})");
        yield return null;
        yield return null;
        Check((bool)Get(Global.Input.Controller, "inputAllowed"), "ui", "game input allowed after Static UI closed");
        before = JumpCounts(actors);
        yield return PressSouthInternal();
        Check(AllIncreased(before, JumpCounts(actors)), "ui", "Jump delivered after Static UI closed");

        ui.CreateWidget<SampleUI>(UIDefine.eUIType.SampleDynamic).Show();
        ui.CreateWidget<SampleUI>(UIDefine.eUIType.SampleSystem).Show();
        Check(ui.FindWidget<SampleUI>(UIDefine.eUIType.SampleDynamic).IsActive, "ui", "Dynamic shown");
        Check(ui.FindWidget<SampleUI>(UIDefine.eUIType.SampleSystem).IsActive, "ui", "System shown");

        ui.RemoveWidget(UIDefine.eUIType.SampleDynamic);
        SampleUI first = null, second = null;
        yield return UniTask.WhenAll(
            ui.CreateWidgetAsync<SampleUI>(UIDefine.eUIType.SampleDynamic),
            ui.CreateWidgetAsync<SampleUI>(UIDefine.eUIType.SampleDynamic)).ToCoroutine(pair => (first, second) = pair);
        Check(first != null && ReferenceEquals(first, second), "ui", "Async x2 same type returns one instance");

        var token = this.GetCancellationTokenOnDestroy();
        SampleUI asyncB = null;
        yield return ui.CreateWidgetAsync<SampleUI>(UIDefine.eUIType.SampleStaticB, token).ToCoroutine(widget => asyncB = widget);
        asyncB.Show();
        Check(asyncB.IsActive && ReferenceEquals(ui.TopWidget, asyncB), "ui", "Static B (Async) shown on top");
        asyncB.Hide();

        ui.ClearTransientWidgets();
        Check(ui.FindWidget<SampleUI>(UIDefine.eUIType.SampleDynamic) == null, "ui", "ClearTransientWidgets removes Dynamic");
        Note("ui", $"after ClearTransient: System {Describe(UIDefine.eUIType.SampleSystem)}, StaticA {Describe(UIDefine.eUIType.SampleStaticA)}, StaticB {Describe(UIDefine.eUIType.SampleStaticB)}");

        ui.RemoveWidget(UIDefine.eUIType.SampleStaticA);
        Check(ui.FindWidget<SampleUI>(UIDefine.eUIType.SampleStaticA) == null, "ui", "RemoveWidget(StaticA) removes it");
        ui.RemoveWidget(UIDefine.eUIType.SampleStaticB);
        ui.RemoveWidget(UIDefine.eUIType.SampleSystem);
        yield return null;
        Check((bool)Get(Global.Input.Controller, "inputAllowed"), "ui", "game input allowed after UI cleanup");
    }

    private IEnumerator SceneInternal()
    {
        var scene = Global.Scene;
        int appActors = Spawned().Count;

        yield return TransitionInternal<SampleSceneA>("SampleSceneA", LoadSceneMode.Single, false);
        Check(scene.CurrentScene is SampleSceneA, "scene", $"Single A -> current {scene.CurrentScene?.GetType().Name}");
        Check(Spawned().Count == appActors, "scene", $"App scope actors survive Single transition ({Spawned().Count}/{appActors})");

        Call(runner, "SpawnInternal");
        var sceneActor = Spawned()[Spawned().Count - 1];
        int sceneActorId = sceneActor.Id;
        Check(State(sceneActor) == "Active" && HasLine("(SampleSceneA scope)"), "scene", "Spawn in SampleSceneA scope");
        var placedA = PlaceActorInternal(scene.GetScene<SampleSceneA>(), UnitySceneManager.GetActiveScene(), "SampleSceneA");

        int unityScenes = UnitySceneManager.sceneCount;
        yield return TransitionInternal<SampleSceneB>("SampleSceneB", LoadSceneMode.Additive, false);
        Check(scene.HasScene<SampleSceneB>() && UnitySceneManager.sceneCount == unityScenes + 1, "scene", $"Additive B loaded (unity scenes {UnitySceneManager.sceneCount})");
        Check(scene.CurrentScene is SampleSceneA, "scene", $"current stays A during additive ({scene.CurrentScene?.GetType().Name})");
        var placedB = PlaceActorInternal(scene.GetScene<SampleSceneB>(), UnitySceneManager.GetSceneAt(UnitySceneManager.sceneCount - 1), "SampleSceneB");

        Exception unloadError = null;
        yield return scene.UnloadAdditiveAsync<SampleSceneB>().ToCoroutine(error => unloadError = error);
        Check(unloadError == null, "scene", $"UnloadAdditiveAsync completes ({unloadError?.Message})");
        Check(!scene.HasScene<SampleSceneB>() && UnitySceneManager.sceneCount == unityScenes, "scene", $"Additive B unloaded (unity scenes {UnitySceneManager.sceneCount})");
        Check(HasLine("SampleSceneB OnFinalize"), "scene", "SampleSceneB OnFinalize called");
        Check(!scene.IsTransitioning && scene.IsInputAllowed && scene.CurrentScene is SampleSceneA, "scene", "after unload: not transitioning, input allowed, current A");
        Check(sceneActor != null && State(sceneActor) == "Active", "scene", "A scope actor unaffected by additive unload");
        CheckPlacedInternal(placedB, "Additive B unload");
        Check(placedA != null && placedA.UpdatesAfterStop == 0 && string.Join(",", placedA.Trace) == "enter,enable", "scene", "placed actor in A unaffected by additive unload");

        yield return TransitionInternal<SampleSceneB>("SampleSceneB", LoadSceneMode.Single, false);
        Check(scene.CurrentScene is SampleSceneB, "scene", $"Single B -> current {scene.CurrentScene?.GetType().Name}");
        CheckPlacedInternal(placedA, "Single B transition");
        Check(HasLine($"Actor{sceneActorId} OnRelease"), "scene", "A scope actor released on scene stop");
        Check(sceneActor == null, "scene", "A scope actor destroyed with scene scope pool");
        Check(Spawned().FindAll(actor => actor != null).Count == appActors, "scene", "App scope actors still alive");

        int failures = CountLines("Transition SampleSceneB result: Failure");
        Call(FindAnyObjectByType<SceneSampleRunner>(), "DoubleRequestInternal");
        float deadline = Time.realtimeSinceStartup + 30f;
        while (CountLines("Transition SampleSceneA result:") == 0 && Time.realtimeSinceStartup < deadline)
            yield return null;

        yield return WaitIdleInternal();
        Check(CountLines("Transition SampleSceneB result: Failure") == failures + 1, "scene", "Double Request: second request fails");
        Check(HasLine("Transition SampleSceneA result: Success") && scene.CurrentScene is SampleSceneA, "scene", $"Double Request: first request completes ({scene.CurrentScene?.GetType().Name})");

        failures = CountLines("Transition SampleSceneB result: Failure");
        yield return TransitionInternal<SampleSceneB>("SampleSceneB", LoadSceneMode.Single, true);
        Check(CountLines("Transition SampleSceneB result: Failure") == failures + 1, "scene", "Enter Fail reports Failure");
        Note("scene", $"after Enter Fail: current {scene.CurrentScene?.GetType().Name ?? "None"}, transitioning {scene.IsTransitioning}, input {scene.IsInputAllowed}, active {UnitySceneManager.GetActiveScene().name}");
    }

    private IEnumerator CleanupInternal()
    {
        int lines = sampleLines.Count;
        while (Spawned().Count > 0)
            Call(runner, "ReturnLastInternal");

        yield return null;
        Check(Spawned().Count == 0 && ControllerContextCount() == 0, "cleanup", $"Return All -> spawned {Spawned().Count}, contexts {ControllerContextCount()}");
        Note("cleanup", $"lines after cleanup {sampleLines.Count - lines}");
    }

    private IEnumerator UIProbeInternal()
    {
        yield return ProjectTUIPlayProbe.Verify();
        Check(true, "ui-probe", "ProjectTUIPlayProbe.Verify passed");
    }

    private IEnumerator TransitionInternal<T>(string name, LoadSceneMode mode, bool failEnter) where T : ProjectT.Scene.SceneBase
    {
        bool done = false;
        string result = null;
        sampleLines.Add($"Transition {name} {mode}{(failEnter ? " (fail)" : "")}");
        Global.Scene.Transition<T>(name, mode, code =>
        {
            done = true;
            result = code.ToString();
            sampleLines.Add($"Transition {name} result: {result}");
        }, 0f, failEnter);

        float deadline = Time.realtimeSinceStartup + 30f;
        while (!done && Time.realtimeSinceStartup < deadline)
            yield return null;

        Check(done, "scene", $"Transition {name} {mode} completed ({result ?? "timeout"})");
        yield return WaitIdleInternal();
    }

    // 씬 파일에 배치한 Actor와 같게 해당 Unity 씬에 속한 오브젝트로 만들고 배치 담당 스크립트처럼 Spawn한다.
    private PlacedActorProbe PlaceActorInternal(ProjectT.Scene.SceneBase watched, Scene unityScene, string sceneName)
    {
        var obj = new GameObject("PlacedActorProbe");
        UnitySceneManager.MoveGameObjectToScene(obj, unityScene);
        var actor = obj.AddComponent<PlacedActorProbe>();
        actor.Watched = watched;
        actor.Spawn();
        Check(obj.scene.name == sceneName && string.Join(",", actor.Trace) == "enter,enable", "scene", $"placed actor spawned in {sceneName} ({obj.scene.name})");
        return actor;
    }

    private void CheckPlacedInternal(PlacedActorProbe actor, string label)
    {
        Check(actor == null, "scene", $"{label}: placed actor destroyed by unload");
        Check(actor.UpdatesAfterStop > 0, "scene", $"{label}: placed actor kept updating after scene stop ({actor.UpdatesAfterStop} frames)");
        Check(!actor.InputAllowedAfterStop, "scene", $"{label}: input blocked while placed actor updated after stop");
        Check(string.Join(",", actor.Trace) == "enter,enable,disable-after-stop,release", "scene", $"{label}: unload disabled then released placed actor once ({string.Join(",", actor.Trace)})");
    }

    private IEnumerator WaitIdleInternal()
    {
        float deadline = Time.realtimeSinceStartup + 30f;
        while (Global.Scene.IsTransitioning && Time.realtimeSinceStartup < deadline)
            yield return null;

        yield return null;
    }

    private IEnumerator PressSouthInternal()
    {
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return null;
        yield return null;
    }

    private static void ClickInternal(SampleUI widget, string buttonName)
    {
        foreach (var button in widget.GetComponentsInChildren<Button>(true))
        {
            if (button.name == buttonName)
            {
                button.onClick.Invoke();
                return;
            }
        }

        throw new InvalidOperationException($"{widget.name} has no {buttonName}.");
    }

    private string Describe(UIDefine.eUIType type)
    {
        var widget = Global.UI.FindWidget<SampleUI>(type);
        return widget == null ? "-" : widget.IsActive ? "Shown" : "Hidden";
    }

    private List<SampleActor> Spawned()
    {
        var list = (List<SampleActor>)Get(runner, "spawned");
        list.RemoveAll(actor => actor == null);
        return list;
    }

    private static int ControllerContextCount()
    {
        return ((IList)Get(Global.Input.Controller, "contexts")).Count;
    }

    private static string State(SampleActor actor)
    {
        return Get(actor, "spawnState").ToString();
    }

    private static bool ContextOwned(SampleActor actor)
    {
        return Get(Get(actor, "context"), "Owner") != null;
    }

    private static int[] JumpCounts(List<SampleActor> actors)
    {
        var counts = new int[actors.Count];
        for (int i = 0; i < actors.Count; i++)
            counts[i] = actors[i].JumpCount;

        return counts;
    }

    private static bool SameCounts(int[] before, int[] after)
    {
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i] != after[i])
                return false;
        }

        return true;
    }

    private static bool AllIncreased(int[] before, int[] after)
    {
        for (int i = 0; i < before.Length; i++)
        {
            if (after[i] != before[i] + 1)
                return false;
        }

        return before.Length > 0;
    }

    private bool HasLine(string text)
    {
        return CountLines(text) > 0;
    }

    private int CountLines(string text)
    {
        int count = 0;
        foreach (var line in sampleLines)
        {
            if (line.Contains(text))
                count++;
        }

        return count;
    }

    private void OnLogInternal(string message, string stackTrace, LogType type)
    {
        if (message.StartsWith("[Sample] "))
            sampleLines.Add(message.Substring(9));

        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            return;

        // Enter Fail과 Double Request는 의도한 실패를 Global.LogException으로 보고한다.
        if (message.Contains("[Sample] SampleSceneB OnEnter failure test") || message.Contains("Scene transition is unavailable."))
            expectedErrors.Add(message.Split('\n')[0]);
        else
            errors.Add(message.Split('\n')[0]);
    }

    private void Check(bool valid, string group, string name)
    {
        if (valid)
            pass++;
        else
            fail++;

        Record($"[P0] {(valid ? "PASS" : "FAIL")} {group}: {name}");
    }

    private static void Note(string group, string text)
    {
        Record($"[P0] NOTE {group}: {text}");
    }

    private static void Record(string line)
    {
        s_results.Add(line);
        Debug.Log(line);
    }

    private static object Get(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Flags);
            if (field != null)
                return field.GetValue(target);

            var property = type.GetProperty(name, Flags);
            if (property != null)
                return property.GetValue(target);
        }

        throw new MissingMemberException(target.GetType().Name, name);
    }

    private static void Call(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethod(name, Flags);
        if (method == null)
            throw new MissingMethodException(target.GetType().Name, name);

        try
        {
            method.Invoke(target, args.Length == 0 ? null : args);
        }
        catch (TargetInvocationException error)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
        }
    }
}
#endif
