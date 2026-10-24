using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Controller;
using ProjectT.Pool;
using ProjectT.Scene;
using ProjectT.Skill;
using ProjectT.Skill.LifecycleTests;
using ProjectT.SkillAdapter;
using ProjectT.Stat;
using UnityEngine;
using UnityEngine.InputSystem;

static partial class Program
{
    class ActorProbe : ComBaseActor
    {
        public InputContext Input;
        public int Fires;
        public int SpawnData, AppliedData;
        public readonly List<string> Trace = new();
        public Action InitAction, EnterAction, UpdateAction, EnabledAction, DisabledAction, ReleasedAction;
        public int Updates, LateUpdates;

        public int Releases => Count("release");

        public int Count(string hook) => Trace.FindAll(x => x == hook).Count;

        protected override void OnInit()
        {
            Trace.Add("init");
            if (Input != null)
                RegisterInputContext(Input);
            InitAction?.Invoke();
        }

        protected override void OnEnter()
        {
            Trace.Add("enter");
            AppliedData = SpawnData;
            EnterAction?.Invoke();
        }

        protected override void OnUpdate(float dt)
        {
            Updates++;
            UpdateAction?.Invoke();
        }

        protected override void OnLateUpdate(float dt) => LateUpdates++;

        protected override void Enable()
        {
            Trace.Add("enable");
            EnabledAction?.Invoke();
        }

        protected override void Disable()
        {
            Trace.Add("disable");
            DisabledAction?.Invoke();
        }

        protected override void OnRelease()
        {
            Trace.Add("release");
            SpawnData = AppliedData = 0;
            ReleasedAction?.Invoke();
        }

        public void Register(InputContext context) => RegisterInputContext(context);

        public void Unregister(InputContext context) => UnregisterInputContext(context);

        public SkillAgent AddSkills() => AddSkillAgent();

        public Status AddStats() => AddStatus();
    }

    // Skill and effect hooks record into the actor trace so their order is compared with the actor hooks.
    sealed class SkillActorProbe : ActorProbe
    {
        protected override void OnInit()
        {
            TestSkillAction.Trace = TestBehavior.Trace = Trace;
            AddSkillAgent();
            base.OnInit();
        }
    }

    sealed class ManualTickActorProbe : ActorProbe
    {
        protected override bool AutoTickSkills => false;

        public void Tick(float deltaTime) => TickSkills(deltaTime);

        protected override void OnInit()
        {
            TestSkillAction.Trace = TestBehavior.Trace = Trace;
            AddSkillAgent();
            base.OnInit();
        }
    }

    sealed class StatusActorProbe : ActorProbe
    {
        protected override void OnInit()
        {
            TestSkillAction.Trace = TestBehavior.Trace = Trace;
            AddSkillAgent();
            AddStatus().DefineResource(SkillDefinitions.HpStat, SkillDefinitions.MaxHpStat);
            base.OnInit();
        }
    }

    sealed class ActorSkippingAwakeProbe : ActorProbe
    {
        public bool DerivedAwake;

        protected override void Awake()
        {
            DerivedAwake = true;
        }
    }

    sealed class ActorWithoutBaseProbe : ActorProbe
    {
        protected override void OnEnter() { }

        protected override void OnUpdate(float dt) { }

        protected override void OnLateUpdate(float dt) { }

        protected override void Enable() { }

        protected override void Disable() { }

        protected override void OnRelease() { }
    }

    sealed class FailingReturnProbe : MonoBehaviour, IPoolable
    {
        public Exception Failure;
        public int Returns;

        public void OnGet() { }

        public void OnReturn()
        {
            Returns++;
            if (Failure != null)
                throw Failure;
        }
    }

    sealed class ActorSceneProbe : SceneBase
    {
        protected override void OnInitialize() { }

        protected override void OnFinalize() { }
    }

    static GameObjectPool CreateActorPool() => CreateActorPool<ActorProbe>();

    static GameObjectPool CreateActorPool<T>() where T : ActorProbe
    {
        var prefab = new GameObject("ActorPrefab");
        prefab.AddComponent<T>();
        var pool = new GameObjectPool(prefab, 0);
        return pool;
    }

    // Get's SetActive(true) delivers Awake (first borrow only) and OnEnable in Unity.
    static ActorProbe BorrowActor(GameObjectPool pool, bool first)
    {
        var actor = pool.Get(null).GetComponent<ActorProbe>();
        if (first)
        {
            actor.Input = CreateFireContext(actor);
            ActorMessage(actor, "Awake");
        }
        ActorMessage(actor, "OnEnable");
        return actor;
    }

    static ActorProbe SpawnFirstActor(GameObjectPool pool)
    {
        var actor = BorrowActor(pool, true);
        actor.Spawn();
        return actor;
    }

    // FinishReturn's SetActive(false) delivers OnDisable in Unity; the stub only records the active flag.
    static void ReturnActor(GameObjectPool pool, ActorProbe actor)
    {
        try
        {
            pool.Return(actor.gameObject);
        }
        finally
        {
            if (!actor.gameObject.activeSelf)
                ActorMessage(actor, "OnDisable");
        }
    }

    static void DestroyPooledObject(GameObject obj)
    {
        typeof(PooledObject).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj.GetComponent<PooledObject>(), null);
    }

    static string TraceOf(ActorProbe actor) => string.Join(",", actor.Trace);

    static Exception CaptureSync(Action work)
    {
        return Capture(() =>
        {
            work();
            return UniTask.CompletedTask;
        }).GetAwaiter().GetResult();
    }

    // ObjectDisposedException derives from InvalidOperationException, so the quarantine rejection is compared by exact type.
    static bool IsRejection(Exception error) => error != null && error.GetType() == typeof(InvalidOperationException);

    static void CheckQuarantined(ActorProbe actor)
    {
        int enters = actor.Count("enter"), enables = actor.Count("enable"), updates = actor.Updates;
        var caught = CaptureSync(() => actor.Spawn());
        Check(IsRejection(caught) && actor.Count("enter") == enters, "Faulted actor accepted a spawn");
        actor.enabled = true;
        ActorMessage(actor, "OnEnable");
        ActorMessage(actor, "Update");
        Check(actor.Count("enable") == enables && actor.Updates == updates && !IsRegistered(actor.Input) && !Dispatches(actor), "Faulted actor was reactivated by a later enable");
    }

    static void CheckReturnRejected(GameObjectPool pool, ActorProbe actor)
    {
        string trace = TraceOf(actor);
        var caught = CaptureSync(() => ReturnActor(pool, actor));
        Check(IsRejection(caught) && TraceOf(actor) == trace, "Faulted actor was returned or cleaned up again");
        Check(pool.ActiveCount == 1 && pool.InactiveCount == 0 && actor.gameObject.activeSelf && actor.transform.parent != pool.Root, "Rejected return changed pool tracking or deactivated the object");
    }

    // Dispose calls OnReturn on borrowed items, so the rejection is aggregated at the pool boundary while the object is still destroyed.
    static void DisposeFaultedPool(GameObjectPool pool, ActorProbe actor)
    {
        var caught = CaptureSync(pool.Dispose);
        Check(caught is AggregateException aggregate && aggregate.InnerExceptions.Count == 1 && IsRejection(aggregate.InnerExceptions[0]), "Dispose did not report the rejected return once");
        Check(pool.ActiveCount == 0 && UnityEngine.Object.Destroyed.Contains(actor.gameObject), "Dispose kept the faulted actor");
        ActorMessage(actor, "OnDisable");
        ActorMessage(actor, "OnDestroy");
        Check(!IsRegistered(actor.Input), "Destruction kept the faulted actor's contexts");
    }

    // Unity message order is supplied explicitly; these tests do not simulate the PlayerLoop.
    static void ActorMessage(ComBaseActor component, string message)
    {
        var method = typeof(ComBaseActor).GetMethod(message, BindingFlags.Instance | BindingFlags.NonPublic);
        Check(method != null, "Missing Unity message: " + message);
        try
        {
            method.Invoke(component, null);
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
        }
    }

    // A component added to an active object receives Awake and OnEnable immediately.
    static ActorProbe AttachActor(ActorProbe actor, bool spawn = true)
    {
        ActorMessage(actor, "Awake");
        ActorMessage(actor, "OnEnable");
        if (spawn)
            actor.Spawn();
        return actor;
    }

    static InputContext CreateFireContext(ActorProbe actor)
    {
        return new InputContext("actor", 0).Bind("Player/Fire", _ => actor.Fires++, eInputEvent.Performed, consume: false);
    }

    static ActorProbe CreateInputActor()
    {
        var actor = new GameObject("Actor").AddComponent<ActorProbe>();
        actor.Input = CreateFireContext(actor);
        return actor;
    }

    static ActorProbe BorrowSceneActor(SceneBase scene)
    {
        var prefab = new GameObject("ActorPrefab");
        prefab.AddComponent<ActorProbe>();
        var component = Global.Pool.Get(prefab, null, scene.ResourceScope).GetComponent<ActorProbe>();
        component.Input = CreateFireContext(component);
        return AttachActor(component);
    }

    static InputContext CreateHeldContext(Action onReset)
    {
        return new InputContext("held", 0).Bind("Player/Fire", _ => { }, eInputEvent.Start, consume: false, onReset: onReset);
    }

    static InputAction ProjectFire => InputSystem.actions.FindAction("Player/Fire", true);

    static bool IsRegistered(InputContext context) => context.Owner != null;

    static bool Dispatches(ActorProbe actor)
    {
        int before = actor.Fires;
        ProjectFire.Trigger(InputActionPhase.Performed);
        return actor.Fires != before;
    }

    static ManagerHost StartInput(InputActionAsset asset = null)
    {
        InputSystem.actions = asset ?? new InputActionAsset();
        var host = new ManagerHost();
        Global.BindForTests(host);
        host.Register(new InputManager());
        host.Register(new NotificationManager());
        host.InitializeAsync().GetAwaiter().GetResult();
        return host;
    }

    static ManagerHost actorHost;

    static UniTask RunActorCase(string name, Action test)
    {
        return Run("actor: " + name, () =>
        {
            actorHost = StartInput();
            try
            {
                test();
            }
            finally
            {
                actorHost.Shutdown();
                actorHost = null;
            }
            return UniTask.CompletedTask;
        });
    }

    const int TestSkillId = 1, InstantSkillId = 2, AtkEffectId = 4;
    // Like table skills, the test skill is blocked and cancelled by the adapter stun tag.
    static readonly SkillTag[] StunTags = { SkillDefinitions.StunTag };
    static readonly SkillDefinition TestSkill = new SkillDefinition(TestSkillId, 0f, () => new TestSkillAction(), StunTags, StunTags, default, Array.Empty<EffectDefinition>(), Array.Empty<SkillCost>());
    static readonly SkillDefinition InstantSkill = new SkillDefinition(InstantSkillId, 1f, () => new InstantTestAction(), StunTags, StunTags, default, Array.Empty<EffectDefinition>(), Array.Empty<SkillCost>());
    static readonly EffectDefinition LongEffect = TestEffect(1, 5f, 0f), IntervalEffect = TestEffect(2, 5f, 1f), ShortEffect = TestEffect(3, 0.01f, 0f), StunEffect = TestEffect(5, 5f, 0f, new[] { SkillDefinitions.StunTag });

    // Test effects attach a tracing behavior through the definition factory and keep the production modifier, tag and damage handling.
    static EffectDefinition TestEffect(int id, float duration, float period, IReadOnlyList<SkillTag> tags = null, float power = 0f, DamageExecution damageExecution = null)
    {
        return new EffectDefinition(id, EffectDurationPolicy.Duration, duration, period, Array.Empty<EffectModifier>(), tags ?? Array.Empty<SkillTag>(), power, damageExecution, SkillDefinitions.HpStat, EffectStackPolicy.Refresh, 1, () => new TestBehavior());
    }

    // Global.Table resolves through a ready DataManager, so skill cases start it with the input manager.
    static UniTask RunSkillActorCase(string name, Action test)
    {
        return Run("actor skill: " + name, () =>
        {
            InputSystem.actions = new InputActionAsset();
            actorHost = new ManagerHost();
            Global.BindForTests(actorHost);
            actorHost.Register(new InputManager());
            var data = actorHost.Register(new DataManager());
            actorHost.InitializeAsync().GetAwaiter().GetResult();
            data.Table.BuffInfos.Rows[AtkEffectId] = new DesignTable.buffInfo { buff_Id = AtkEffectId, buff_type = (sbyte)DesignEnum.BuffType.AddATK, buff_duration = 5, buff_usePercent = 50 };
            TestSkillAction.TickAction = null;
            TestSkillAction.CancelFailure = null;
            TestSkillAction.CancelAction = null;
            TestBehavior.RemoveFailure = null;
            TestBehavior.RemoveAction = null;
            try
            {
                test();
            }
            finally
            {
                actorHost.Shutdown();
                actorHost = null;
            }
            return UniTask.CompletedTask;
        });
    }

    static async UniTask RunActorTests()
    {
        await RunActorInputTests();

        await RunActorSpawnTests();

        await RunActorReturnTests();

        await RunActorSkillTests();
    }
}

namespace ProjectT.Skill.LifecycleTests
{
    public sealed class TestSkillAction : BaseSkillAction
    {
        public static List<string> Trace;
        public static Action TickAction;
        public static Exception CancelFailure;
        public static Action CancelAction;

        protected override void Activate() => Trace.Add("skill-activate");

        public override void OnUpdate(float deltaTime)
        {
            Trace.Add("skill-tick");
            TickAction?.Invoke();
        }

        protected override void OnEnd(SkillEndReason reason)
        {
            if (reason != SkillEndReason.Cancelled)
                return;

            Trace.Add("skill-cancel");
            CancelAction?.Invoke();
            if (CancelFailure != null)
                throw CancelFailure;
        }
    }

    public sealed class InstantTestAction : BaseSkillAction
    {
        protected override void Activate()
        {
            TestSkillAction.Trace.Add("instant-activate");
            End();
        }
    }

    public sealed class TestBehavior : EffectBehavior
    {
        public static List<string> Trace;
        public static Exception RemoveFailure;
        public static Action RemoveAction;

        public override void OnApply() => Trace.Add("effect-apply:" + Definition.Id);

        public override void OnExecute() => Trace.Add("effect-execute:" + Definition.Id);

        public override void OnRemove(EffectRemoveReason reason)
        {
            Trace.Add("effect-remove:" + Definition.Id + ":" + reason);
            RemoveAction?.Invoke();
            if (RemoveFailure != null)
                throw RemoveFailure;
        }
    }

    public sealed class SourceRecordingExecution : DamageExecution
    {
        public readonly List<Status> Sources = new List<Status>();

        public override float Execute(Status source, Status target, float power)
        {
            Sources.Add(source);
            return power;
        }
    }
}
