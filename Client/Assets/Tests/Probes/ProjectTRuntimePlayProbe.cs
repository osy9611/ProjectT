#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Controller;
using ProjectT.SkillAdapter;
using ProjectT.UGUI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

// 샘플 없이 프로브가 만든 픽스처(Actor 템플릿, 직접 등록한 위젯, 테스트 전용 씬 TestSceneA/B)로 Actor·Pool, Skill, Input, UI, Scene 경로를 Play Mode에서 실행한다.
// 입력은 가상 Gamepad 이벤트로 넣는다.
public class ProjectTRuntimePlayProbe : MonoBehaviour
{
    public const string EnterFailureMessage = "[Probe] TestSceneB OnEnter failure test";

    private const int SkillId = 201;
    private const float BaseHp = 100f;
    private const float BaseAtk = 10f;

    private static readonly List<string> s_results = new List<string>();
    private static readonly List<string> s_lines = new List<string>();
    private readonly List<ProbeActor> spawned = new List<ProbeActor>();
    private readonly List<string> errors = new List<string>();
    private readonly List<string> expectedErrors = new List<string>();
    private int pass;
    private int fail;
    private GameObject template;
    private InputContext blocker;
    private Gamepad pad;
    private InputSettings originalSettings;

    // SceneManager는 씬을 타입으로 등록하므로 Additive로 함께 올릴 A·B를 별도 타입으로 둔다.
    // data[0]: 진입 지연(초), data[1]: true이면 진입 실패
    private abstract class TestScene : ProjectT.Scene.SceneBase
    {
        protected override void OnInitialize()
        {
        }

        protected override async UniTask OnEnter(System.Threading.CancellationToken token, params object[] data)
        {
            float delay = data.Length > 0 ? (float)data[0] : 0f;
            bool failEnter = data.Length > 1 && (bool)data[1];
            await UniTask.WaitForSeconds(delay, cancellationToken: token);
            if (failEnter)
                throw new InvalidOperationException(EnterFailureMessage);
        }

        protected override void OnFinalize()
        {
            Write($"{GetType().Name} OnFinalize");
        }
    }

    private sealed class TestSceneA : TestScene
    {
    }

    private sealed class TestSceneB : TestScene
    {
    }

    // 여러 Actor가 같은 입력을 함께 받도록 consume을 끈 입력 컨텍스트와, 테이블 스킬 201을 가진다.
    private sealed class ProbeActor : ComBaseActor
    {
        private static int s_nextId;

        private InputContext context;

        public InputContext Context => context;
        public int Id { get; private set; }
        public Vector2 MoveDirection { get; private set; }
        public int JumpCount { get; private set; }

        protected override void OnInit()
        {
            context ??= new InputContext($"ProbeActor{GetInstanceID()}", 0)
                .Bind("Player/Move", callback => MoveDirection = callback.ReadValue<Vector2>(), eInputEvent.Performed | eInputEvent.Cancel, consume: false, onReset: () => MoveDirection = Vector2.zero)
                .Bind("Player/Jump", _ => JumpCount++, eInputEvent.Performed, consume: false);

            RegisterInputContext(context);
            AddSkillAgent();
            AddStatus().DefineResource(SkillDefinitions.HpStat, SkillDefinitions.MaxHpStat);
        }

        protected override void OnEnter()
        {
            Id = ++s_nextId;
            // 반납 때 Status 값과 등록된 스킬이 비워지므로 스폰마다 다시 채운다.
            Status.SetBase(SkillDefinitions.MaxHpStat, BaseHp);
            Status.SetBase(SkillDefinitions.AtkStat, BaseAtk);
            Status.SetResource(SkillDefinitions.HpStat, BaseHp);
            SkillAgent.GiveSkill(SkillDefinitions.GetSkill(SkillId));
        }

        protected override void Enable()
        {
            SkillAgent.OnSkillActivated += OnSkillActivatedInternal;
            SkillAgent.OnSkillEnded += OnSkillEndedInternal;
            SkillAgent.OnTagChanged += OnTagChangedInternal;
            SkillAgent.OnEffectApplied += OnEffectAppliedInternal;
            SkillAgent.OnEffectRemoved += OnEffectRemovedInternal;
            Write($"Actor{Id} Enable");
        }

        protected override void Disable()
        {
            SkillAgent.OnSkillActivated -= OnSkillActivatedInternal;
            SkillAgent.OnSkillEnded -= OnSkillEndedInternal;
            SkillAgent.OnTagChanged -= OnTagChangedInternal;
            SkillAgent.OnEffectApplied -= OnEffectAppliedInternal;
            SkillAgent.OnEffectRemoved -= OnEffectRemovedInternal;
            Write($"Actor{Id} Disable");
        }

        protected override void OnRelease()
        {
            Write($"Actor{Id} OnRelease");
            MoveDirection = Vector2.zero;
            JumpCount = 0;
        }

        private void OnSkillActivatedInternal(ProjectT.Skill.SkillDefinition definition)
        {
            Write($"Actor{Id} Skill {definition.Id} Activated");
        }

        private void OnSkillEndedInternal(ProjectT.Skill.SkillDefinition definition, ProjectT.Skill.SkillEndReason reason)
        {
            Write($"Actor{Id} Skill {definition.Id} Ended {reason}");
        }

        private void OnTagChangedInternal(ProjectT.Skill.SkillTag tag, bool added)
        {
            Write($"Actor{Id} Tag {tag.Name} {(added ? "Added" : "Removed")}");
        }

        private void OnEffectAppliedInternal(ProjectT.Skill.EffectHandle handle, ProjectT.Skill.EffectDefinition definition)
        {
            Write($"Actor{Id} Effect {definition.Id} Applied");
        }

        private void OnEffectRemovedInternal(ProjectT.Skill.EffectHandle handle, ProjectT.Skill.EffectDefinition definition, ProjectT.Skill.EffectRemoveReason reason)
        {
            Write($"Actor{Id} Effect {definition.Id} Removed {reason}");
        }
    }

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
        s_lines.Clear();
        Summary = "running";
        Running = true;
        var host = new GameObject("ProjectTRuntimePlayProbe");
        DontDestroyOnLoad(host);
        host.AddComponent<ProjectTRuntimePlayProbe>();
    }

    private static void Write(string message)
    {
        s_lines.Add(message);
    }

    private IEnumerator Start()
    {
        Application.logMessageReceived += OnLogInternal;
        try
        {
            Check(Global.IsReady, "setup", $"Global Ready ({Global.State})");
            var token = this.GetCancellationTokenOnDestroy();
            Global.Notify.Subscribe(NotificationId.InputControlsChanged, () => Write("Notify InputControlsChanged"), token);
            Global.Notify.Subscribe(NotificationId.InputDeviceLost, () => Write("Notify InputDeviceLost"), token);
            Global.Notify.Subscribe(NotificationId.InputDeviceRegained, () => Write("Notify InputDeviceRegained"), token);
            blocker = new InputContext("ProbeBlocker", 100)
                .Bind("Player/Jump", _ => Write("Blocker consumed Jump"), eInputEvent.Performed);

            // 비활성 템플릿에 붙여 두면 풀이 만든 복제본이 대여될 때 처음 Awake된다.
            template = new GameObject("ProbeActor");
            template.SetActive(false);
            template.transform.SetParent(transform, false);
            template.AddComponent<ProbeActor>();

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
            Record($"[Runtime] ERROR LOG {error}");

        Check(errors.Count == 0, "console", $"no Error/Exception logs during probe ({errors.Count})");
        Summary = $"pass={pass} fail={fail}";
        Debug.Log($"PROJECTT_RUNTIME_PLAYMODE_RESULT {Summary}");
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
        SpawnInternal();
        SpawnInternal();
        SpawnInternal();
        var actors = Spawned();
        Check(actors.Count == 3, "actor", $"Spawn x3 -> spawned {actors.Count}");
        foreach (var actor in actors)
            Check(actor.gameObject.activeSelf && ContextOwned(actor), "actor", $"Actor{actor.Id} Active with registered input context");

        Check(RegisteredContexts(actors) == 3, "actor", $"registered actor contexts after spawn = {RegisteredContexts(actors)}");
        Check(actors[0].Id != actors[1].Id && actors[1].Id != actors[2].Id, "actor", "each spawn gets a distinct Id");

        var last = actors[2];
        ToggleLastInternal();
        Check(!last.gameObject.activeSelf && !HasLine($"Actor{last.Id} OnRelease") && !ContextOwned(last), "actor", "Toggle off -> inactive without release, context detached");
        Check(RegisteredContexts(actors) == 2, "actor", $"registered actor contexts after toggle off = {RegisteredContexts(actors)}");
        Check(HasLine($"Actor{last.Id} Disable"), "actor", "Toggle off calls Disable");

        ToggleLastInternal();
        Check(last.gameObject.activeSelf && ContextOwned(last), "actor", "Toggle on -> Active, context re-registered");
        Check(RegisteredContexts(actors) == 3, "actor", $"registered actor contexts after toggle on = {RegisteredContexts(actors)}");

        int oldId = last.Id;
        RespawnInternal();
        actors = Spawned();
        var respawned = actors[actors.Count - 1];
        Check(HasLine($"Actor{oldId} OnRelease"), "actor", "Respawn returns last (OnRelease)");
        Check(actors.Count == 3 && respawned.gameObject.activeSelf && ContextOwned(respawned) && respawned.Id > oldId, "actor", $"Respawn rents new spawn Actor{respawned.Id} Active (reused instance: {ReferenceEquals(respawned, last)})");
        Check(RegisteredContexts(actors) == 3, "actor", $"registered actor contexts after respawn = {RegisteredContexts(actors)}");

        var returned = actors[actors.Count - 1];
        int returnedId = returned.Id;
        ReturnLastInternal();
        Check(!returned.gameObject.activeSelf && HasLine($"Actor{returnedId} OnRelease") && !ContextOwned(returned), "actor", "Return Last -> inactive, released, context detached");
        Check(Spawned().Count == 2 && RegisteredContexts(Spawned()) == 2, "actor", $"after Return Last spawned {Spawned().Count}, contexts {RegisteredContexts(Spawned())}");

        SpawnInternal();
        Check(ReferenceEquals(Spawned()[2], returned) && returned.gameObject.activeSelf && ContextOwned(returned), "actor", "Spawn after Return reuses pooled instance");
        yield return null;
        Check(errors.Count == 0, "actor", "no errors during actor steps");
    }

    // 테이블 스킬 201(쿨다운 3초), 버프 1(공격력 +20%, 10초), 버프 5(스턴 5초)를 마지막 Actor에 사용한다.
    private IEnumerator SkillInternal()
    {
        var actor = Spawned()[Spawned().Count - 1];
        var status = actor.Status;
        int id = actor.Id;
        string activatedLine = $"Actor{id} Skill {SkillId} Activated";
        Check(status.GetResource(SkillDefinitions.HpStat) == BaseHp && status.Get(SkillDefinitions.MaxHpStat) == BaseHp && status.Get(SkillDefinitions.AtkStat) == BaseAtk,
            "skill", $"Spawn sets Hp {status.GetResource(SkillDefinitions.HpStat)}/{status.Get(SkillDefinitions.MaxHpStat)}, Atk {status.Get(SkillDefinitions.AtkStat)}");

        int activated = CountLines(activatedLine);
        actor.SkillAgent.ActivateSkill(SkillId);
        Check(CountLines(activatedLine) == activated + 1 && HasLine($"Actor{id} Skill {SkillId} Ended Completed"), "skill", "Use Skill activates and completes");

        actor.SkillAgent.ActivateSkill(SkillId);
        Check(CountLines(activatedLine) == activated + 1, "skill", "Use Skill during cooldown is ignored");

        yield return new WaitForSeconds(3.2f);
        actor.SkillAgent.ActivateSkill(SkillId);
        Check(CountLines(activatedLine) == activated + 2, "skill", "Use Skill after cooldown activates");

        actor.SkillAgent.ApplyEffect(SkillDefinitions.GetEffect(1), actor);
        Check(HasLine($"Actor{id} Effect 1 Applied") && Mathf.Approximately(status.Get(SkillDefinitions.AtkStat), BaseAtk * 1.2f), "skill", $"Atk Buff applies modifier (Atk {status.Get(SkillDefinitions.AtkStat)})");

        actor.SkillAgent.ApplyEffect(SkillDefinitions.GetEffect(5), actor);
        Check(actor.SkillAgent.HasTag(SkillDefinitions.StunTag) && HasLine($"Actor{id} Tag State.Stun Added"), "skill", "Stun grants State.Stun");

        yield return new WaitForSeconds(3.2f);
        activated = CountLines(activatedLine);
        actor.SkillAgent.ActivateSkill(SkillId);
        Check(CountLines(activatedLine) == activated, "skill", "Use Skill while stunned is blocked");

        yield return new WaitForSeconds(2.2f);
        Check(!actor.SkillAgent.HasTag(SkillDefinitions.StunTag) && HasLine($"Actor{id} Effect 5 Removed Expired") && HasLine($"Actor{id} Tag State.Stun Removed"), "skill", "Stun expires and removes State.Stun");

        actor.SkillAgent.ActivateSkill(SkillId);
        Check(CountLines(activatedLine) == activated + 1, "skill", "Use Skill after stun activates");

        // Atk 버프(10초)가 남은 상태로 반납·재스폰해 이전 스폰의 효과와 스탯이 넘어가지 않는지 본다.
        RespawnInternal();
        var respawned = Spawned()[Spawned().Count - 1];
        Check(ReferenceEquals(respawned, actor) && respawned.Id != id, "skill", $"Respawn reuses instance with new Actor{respawned.Id}");
        Check(status.Get(SkillDefinitions.AtkStat) == BaseAtk && status.GetResource(SkillDefinitions.HpStat) == BaseHp && !respawned.SkillAgent.HasTag(SkillDefinitions.StunTag),
            "skill", $"Respawn resets Atk {status.Get(SkillDefinitions.AtkStat)}, Hp {status.GetResource(SkillDefinitions.HpStat)}, no tags");

        respawned.SkillAgent.ActivateSkill(SkillId);
        Check(HasLine($"Actor{respawned.Id} Skill {SkillId} Activated"), "skill", "Respawned actor re-registers skill without cooldown carry-over");
        yield return null;
        Check(errors.Count == 0, "skill", "no errors during skill steps");
    }

    private IEnumerator InputInternal()
    {
        var input = Global.Input;
        var actors = Spawned();
        pad = InputSystem.AddDevice<Gamepad>("RuntimeProbePad");
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

        input.AddContext(blocker);
        before = JumpCounts(actors);
        int blockerLines = CountLines("Blocker consumed Jump");
        yield return PressSouthInternal();
        Check(SameCounts(before, JumpCounts(actors)) && CountLines("Blocker consumed Jump") == blockerLines + 1, "input", "priority 100 blocker consumes Jump");
        input.RemoveContext(blocker);
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
        Check(!PlayerMapEnabled(), "input", "device lost disables the Player action map");

        InputSystem.AddDevice(pad);
        yield return null;
        Check(!input.IsDeviceLost && ReferenceEquals(input.CurrentDevice, pad), "input", $"re-adding pad -> regained ({input.CurrentDevice?.displayName})");
        Check(HasLine("Notify InputDeviceRegained"), "input", "InputDeviceRegained published");
        before = JumpCounts(actors);
        yield return PressSouthInternal();
        Check(AllIncreased(before, JumpCounts(actors)), "input", "Jump works after regain");
    }

    // 위젯은 프리팹 로드 대신 Global.UI의 컨테이너에 직접 등록한다. 프리팹 로드와 비동기 생성 공유는 콘솔 UITests가 검사한다.
    private IEnumerator UIInternal()
    {
        var ui = Global.UI;
        var actors = Spawned();
        var container = (UIContainer)typeof(UIManager).GetField("uiContainer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
        var a = RegisterWidgetInternal(container, 0, eUIContainerType.Static);
        var b = RegisterWidgetInternal(container, 1, eUIContainerType.Static);
        var dynamic = RegisterWidgetInternal(container, 2, eUIContainerType.Dynamic);
        var system = RegisterWidgetInternal(container, 3, eUIContainerType.System);

        a.Show();
        Check(a.IsActive && ReferenceEquals(ui.TopWidget, a), "ui", "Static A shown and on top");
        yield return null;
        Check(!PlayerMapEnabled(), "ui", "focused Static UI blocks game input");
        int[] before = JumpCounts(actors);
        yield return PressSouthInternal();
        Check(SameCounts(before, JumpCounts(actors)), "ui", "Jump not delivered while Static UI focused");

        b.Show();
        Check(b.IsActive && ReferenceEquals(ui.TopWidget, b) && !a.gameObject.activeSelf, "ui", "Static B opens on top and hides A");

        b.Hide();
        Check(ReferenceEquals(ui.TopWidget, a) && a.gameObject.activeSelf, "ui", "Hide B restores A");

        a.Hide();
        Check(ui.TopWidget == null || !ui.TopWidget.gameObject.activeSelf, "ui", $"Hide A empties visible stack (top {ui.TopWidget?.name ?? "None"})");
        yield return null;
        yield return null;
        Check(PlayerMapEnabled(), "ui", "game input allowed after Static UI closed");
        before = JumpCounts(actors);
        yield return PressSouthInternal();
        Check(AllIncreased(before, JumpCounts(actors)), "ui", "Jump delivered after Static UI closed");

        dynamic.Show();
        system.Show();
        Check(dynamic.IsActive && system.IsActive, "ui", "Dynamic and System shown");

        ui.ClearTransientWidgets();
        Check(ui.FindWidget<ProjectTUIPlayProbe>((UIDefine.eUIType)2) == null, "ui", "ClearTransientWidgets removes Dynamic");
        Check(ui.FindWidget<ProjectTUIPlayProbe>((UIDefine.eUIType)3) != null, "ui", "ClearTransientWidgets keeps System");

        ui.RemoveWidget((UIDefine.eUIType)0);
        Check(ui.FindWidget<ProjectTUIPlayProbe>((UIDefine.eUIType)0) == null, "ui", "RemoveWidget removes Static A");
        ui.RemoveWidget((UIDefine.eUIType)1);
        ui.RemoveWidget((UIDefine.eUIType)3);
        yield return null;
        Check(PlayerMapEnabled(), "ui", "game input allowed after UI cleanup");
    }

    private IEnumerator SceneInternal()
    {
        var scene = Global.Scene;
        int appActors = Spawned().Count;

        yield return TransitionInternal<TestSceneA>("TestSceneA", LoadSceneMode.Single, false);
        Check(scene.CurrentScene is TestSceneA, "scene", $"Single A -> current {scene.CurrentScene?.GetType().Name}");
        Check(Spawned().Count == appActors, "scene", $"App scope actors survive Single transition ({Spawned().Count}/{appActors})");

        var sceneActor = SpawnInternal();
        int sceneActorId = sceneActor.Id;
        Check(sceneActor.gameObject.activeSelf && ContextOwned(sceneActor), "scene", "Spawn in TestSceneA scope");
        var placedA = PlaceActorInternal(scene.GetScene<TestSceneA>(), UnitySceneManager.GetActiveScene(), "TestSceneA");

        int unityScenes = UnitySceneManager.sceneCount;
        yield return TransitionInternal<TestSceneB>("TestSceneB", LoadSceneMode.Additive, false);
        Check(scene.HasScene<TestSceneB>() && UnitySceneManager.sceneCount == unityScenes + 1, "scene", $"Additive B loaded (unity scenes {UnitySceneManager.sceneCount})");
        Check(scene.CurrentScene is TestSceneA, "scene", $"current stays A during additive ({scene.CurrentScene?.GetType().Name})");
        var placedB = PlaceActorInternal(scene.GetScene<TestSceneB>(), UnitySceneManager.GetSceneAt(UnitySceneManager.sceneCount - 1), "TestSceneB");

        Exception unloadError = null;
        yield return scene.UnloadAdditiveAsync<TestSceneB>().ToCoroutine(error => unloadError = error);
        Check(unloadError == null, "scene", $"UnloadAdditiveAsync completes ({unloadError?.Message})");
        Check(!scene.HasScene<TestSceneB>() && UnitySceneManager.sceneCount == unityScenes, "scene", $"Additive B unloaded (unity scenes {UnitySceneManager.sceneCount})");
        Check(HasLine("TestSceneB OnFinalize"), "scene", "TestSceneB OnFinalize called");
        Check(!scene.IsTransitioning && scene.IsInputAllowed && scene.CurrentScene is TestSceneA, "scene", "after unload: not transitioning, input allowed, current A");
        Check(sceneActor != null && sceneActor.gameObject.activeSelf && ContextOwned(sceneActor), "scene", "A scope actor unaffected by additive unload");
        CheckPlacedInternal(placedB, "Additive B unload");
        Check(placedA != null && placedA.UpdatesAfterStop == 0 && string.Join(",", placedA.Trace) == "enter,enable", "scene", "placed actor in A unaffected by additive unload");

        yield return TransitionInternal<TestSceneB>("TestSceneB", LoadSceneMode.Single, false);
        Check(scene.CurrentScene is TestSceneB, "scene", $"Single B -> current {scene.CurrentScene?.GetType().Name}");
        CheckPlacedInternal(placedA, "Single B transition");
        Check(HasLine($"Actor{sceneActorId} OnRelease"), "scene", "A scope actor released on scene stop");
        Check(sceneActor == null, "scene", "A scope actor destroyed with scene scope pool");
        Check(Spawned().Count == appActors, "scene", "App scope actors still alive");

        // 전환 중 두 번째 요청은 거부되어야 하며, 거부 결과는 완료 콜백의 Failure로 전달된다.
        int failures = CountLines("Transition TestSceneB result: Failure");
        RequestTransitionInternal<TestSceneA>("TestSceneA", 1f);
        RequestTransitionInternal<TestSceneB>("TestSceneB", 1f);
        float deadline = Time.realtimeSinceStartup + 30f;
        while (CountLines("Transition TestSceneA result:") == 0 && Time.realtimeSinceStartup < deadline)
            yield return null;

        yield return WaitIdleInternal();
        Check(CountLines("Transition TestSceneB result: Failure") == failures + 1, "scene", "Double Request: second request fails");
        Check(HasLine("Transition TestSceneA result: Success") && scene.CurrentScene is TestSceneA, "scene", $"Double Request: first request completes ({scene.CurrentScene?.GetType().Name})");

        failures = CountLines("Transition TestSceneB result: Failure");
        yield return TransitionInternal<TestSceneB>("TestSceneB", LoadSceneMode.Single, true);
        Check(CountLines("Transition TestSceneB result: Failure") == failures + 1, "scene", "Enter Fail reports Failure");
        Note("scene", $"after Enter Fail: current {scene.CurrentScene?.GetType().Name ?? "None"}, transitioning {scene.IsTransitioning}, input {scene.IsInputAllowed}, active {UnitySceneManager.GetActiveScene().name}");
    }

    private IEnumerator CleanupInternal()
    {
        var returned = new List<ProbeActor>(Spawned());
        while (Spawned().Count > 0)
            ReturnLastInternal();

        yield return null;
        Check(Spawned().Count == 0 && RegisteredContexts(returned) == 0, "cleanup", $"Return All -> spawned {Spawned().Count}, contexts {RegisteredContexts(returned)}");
    }

    // 활성 씬이 있으면 씬 Scope 풀에서 대여해, 씬 정지 시 풀 정리와 Actor 반납을 함께 확인한다.
    private ProbeActor SpawnInternal()
    {
        var scene = Global.Scene.CurrentScene;
        var obj = Global.Pool.Get(template, null, scene == null ? Global.Resource.AppScope : scene.ResourceScope);
        var actor = obj.GetComponent<ProbeActor>();
        actor.Spawn();
        spawned.Add(actor);
        return actor;
    }

    private void ReturnLastInternal()
    {
        var actor = spawned[spawned.Count - 1];
        spawned.RemoveAt(spawned.Count - 1);
        Global.Pool.Return(actor.gameObject);
    }

    private void ToggleLastInternal()
    {
        var obj = spawned[spawned.Count - 1].gameObject;
        obj.SetActive(!obj.activeSelf);
    }

    private void RespawnInternal()
    {
        ReturnLastInternal();
        SpawnInternal();
    }

    private static ProjectTUIPlayProbe RegisterWidgetInternal(UIContainer container, int key, eUIContainerType type)
    {
        var canvas = container.HudRoot.parent;
        var go = new GameObject("Widget " + key, typeof(RectTransform));
        go.SetActive(false);
        go.transform.SetParent(canvas, false);
        var widget = go.AddComponent<ProjectTUIPlayProbe>();
        typeof(UIBase).GetField("type", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(widget, type);
        widget.InitializeInternal(container, (UIDefine.eUIType)key);
        var registry = (Dictionary<UIDefine.eUIType, UIBase>)typeof(UIContainer).GetField("uiDatas", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(container);
        registry.Add((UIDefine.eUIType)key, widget);
        return widget;
    }

    private IEnumerator TransitionInternal<T>(string name, LoadSceneMode mode, bool failEnter) where T : ProjectT.Scene.SceneBase
    {
        bool done = false;
        string result = null;
        Global.Scene.Transition<T>(name, mode, code =>
        {
            done = true;
            result = code.ToString();
            Write($"Transition {name} result: {result}");
        }, 0f, failEnter);

        float deadline = Time.realtimeSinceStartup + 30f;
        while (!done && Time.realtimeSinceStartup < deadline)
            yield return null;

        Check(done, "scene", $"Transition {name} {mode} completed ({result ?? "timeout"})");
        yield return WaitIdleInternal();
    }

    private static void RequestTransitionInternal<T>(string name, float delay) where T : ProjectT.Scene.SceneBase
    {
        Global.Scene.Transition<T>(name, LoadSceneMode.Single, code => Write($"Transition {name} result: {code}"), delay, false);
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

    // 씬 Scope 풀의 인스턴스는 Single 전환에서 파괴되므로 목록에서 뺀다.
    private IReadOnlyList<ProbeActor> Spawned()
    {
        spawned.RemoveAll(actor => actor == null);
        return spawned;
    }

    private static int RegisteredContexts(IReadOnlyList<ProbeActor> actors)
    {
        int count = 0;
        foreach (var actor in actors)
        {
            if (ContextOwned(actor))
                count++;
        }

        return count;
    }

    private static bool ContextOwned(ProbeActor actor)
    {
        return actor.Context.Owner != null;
    }

    // Controller는 입력 허용 상태에 맞춰 프로젝트 전역 에셋의 Player 맵을 켜고 끈다.
    private static bool PlayerMapEnabled()
    {
        return InputSystem.actions.FindActionMap("Player", true).enabled;
    }

    private static int[] JumpCounts(IReadOnlyList<ProbeActor> actors)
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

    private static bool HasLine(string text)
    {
        return CountLines(text) > 0;
    }

    private static int CountLines(string text)
    {
        int count = 0;
        foreach (var line in s_lines)
        {
            if (line.Contains(text))
                count++;
        }

        return count;
    }

    private void OnLogInternal(string message, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            return;

        // Enter Fail과 Double Request는 의도한 실패를 Global.LogException으로 보고한다.
        if (message.Contains(EnterFailureMessage) || message.Contains("Scene transition is unavailable."))
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

        Record($"[Runtime] {(valid ? "PASS" : "FAIL")} {group}: {name}");
    }

    private static void Note(string group, string text)
    {
        Record($"[Runtime] NOTE {group}: {text}");
    }

    private static void Record(string line)
    {
        s_results.Add(line);
        Debug.Log(line);
    }
}
#endif
