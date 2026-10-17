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
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

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
            if (!actor.gameObject.Active)
                ActorMessage(actor, "OnDisable");
        }
    }

    static void DestroyPooledObject(GameObject obj)
    {
        typeof(PooledObject).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj.GetComponent<PooledObject>(), null);
    }

    static string TraceOf(ActorProbe actor) => string.Join(",", actor.Trace);

    static int RegisteredContextCount(ComBaseActor actor)
    {
        var field = typeof(ComBaseActor).GetField("inputContexts", BindingFlags.Instance | BindingFlags.NonPublic);
        return ((List<InputContext>)field.GetValue(actor)).Count;
    }

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
        Check(pool.ActiveCount == 1 && pool.InactiveCount == 0 && actor.gameObject.Active && actor.transform.parent != pool.Root, "Rejected return changed pool tracking or deactivated the object");
    }

    // Dispose calls OnReturn on borrowed items, so the rejection is aggregated at the pool boundary while the object is still destroyed.
    static void DisposeFaultedPool(GameObjectPool pool, ActorProbe actor)
    {
        var caught = CaptureSync(pool.Dispose);
        Check(caught is AggregateException aggregate && aggregate.InnerExceptions.Count == 1 && IsRejection(aggregate.InnerExceptions[0]), "Dispose did not report the rejected return once");
        Check(pool.ActiveCount == 0 && UnityEngine.Object.Destroyed.Contains(actor.gameObject), "Dispose kept the faulted actor");
        ActorMessage(actor, "OnDisable");
        ActorMessage(actor, "OnDestroy");
        Check(!IsRegistered(actor.Input) && actor.Input.Actor == null && RegisteredContextCount(actor) == 0, "Destruction kept the faulted actor's contexts");
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
        BindGlobal(host);
        host.Register(new InputManager());
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

    const int TestSkillId = 1, AtkEffectId = 4;
    // Like table skills, the test skill is blocked and cancelled by the adapter stun tag.
    static readonly SkillTag[] StunTags = { SkillDefinitions.StunTag };
    static readonly SkillDefinition TestSkill = new SkillDefinition(TestSkillId, 0f, () => new TestSkillAction(), StunTags, StunTags, default, Array.Empty<EffectDefinition>(), Array.Empty<SkillCost>());
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
            BindGlobal(actorHost);
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
        await RunActorCase("actor hooks run once without recursion and actors without input work", () =>
        {
            var actor = AttachActor(new GameObject("Actor").AddComponent<ActorProbe>());
            ActorMessage(actor, "Update");
            ActorMessage(actor, "LateUpdate");
            Check(actor.Updates == 1 && actor.LateUpdates == 1, "Update was not forwarded exactly once");
            Check(TraceOf(actor) == "init,enter,enable", "Initialization was repeated, skipped or reordered");
            actor.enabled = false;
            ActorMessage(actor, "OnDisable");
            ActorMessage(actor, "Update");
            Check(TraceOf(actor) == "init,enter,enable,disable" && actor.Updates == 1, "Disable was not forwarded or updates continued while disabled");
            ActorMessage(actor, "OnDestroy");
            Check(TraceOf(actor) == "init,enter,enable,disable,release", "Destruction skipped or repeated the release hook");
        });

        await RunActorCase("contexts register on spawn of an active actor and share the project-wide actions without cloning", () =>
        {
            var first = AttachActor(CreateInputActor(), false);
            Check(!IsRegistered(first.Input) && !first.Trace.Contains("enter"), "OnEnable before Spawn entered or registered input");
            first.Spawn();
            Check(IsRegistered(first.Input) && TraceOf(first) == "init,enter,enable", "Spawn of an active actor did not enter and register input");
            var second = AttachActor(CreateInputActor());
            Check(ReferenceEquals(first.Input.Owner, Global.Input.Controller) && ReferenceEquals(second.Input.Owner, Global.Input.Controller), "Actor contexts were not registered to the shared controller");
            Check(ReferenceEquals(Global.Input.Controller.ResolveActionInternal("Player/Fire"), ProjectFire), "Controller did not use the project-wide actions");
            ProjectFire.Trigger(InputActionPhase.Performed);
            Check(first.Fires == 1 && second.Fires == 1, "Shared dispatch skipped an actor");
            ActorMessage(first, "OnDestroy");
            ActorMessage(second, "OnDestroy");
            Check(!IsRegistered(first.Input) && !IsRegistered(second.Input), "Destruction retained actor contexts");
            Check(!UnityEngine.Object.Destroyed.Exists(x => x is InputActionAsset) && ProjectFire.enabled, "Actor cleanup destroyed or disabled the shared asset");
        });

        await RunActorCase("pool disable retains bindings and reenable respects the transition lock", () =>
        {
            var actor = AttachActor(CreateInputActor());
            actor.enabled = false;
            ActorMessage(actor, "OnDisable");
            Check(!IsRegistered(actor.Input) && !Dispatches(actor), "Pool disable kept the context registered");
            Global.Input.SetInputAllowed(false);
            actor.enabled = true;
            ActorMessage(actor, "OnEnable");
            Check(IsRegistered(actor.Input) && !Dispatches(actor), "Actor enable bypassed the transition lock");
            Global.Input.SetInputAllowed(true);
            Check(Dispatches(actor) && actor.Count("init") == 1, "Pool reuse lost its binding or reinitialized");
            ActorMessage(actor, "OnDestroy");
        });

        await RunActorCase("unlock does not register an inactive actor", () =>
        {
            var actor = AttachActor(CreateInputActor());
            actor.enabled = false;
            ActorMessage(actor, "OnDisable");
            Global.Input.SetInputAllowed(false);
            Global.Input.SetInputAllowed(true);
            Check(!IsRegistered(actor.Input) && !Dispatches(actor), "Inactive actor was revived by unlock");
            ActorMessage(actor, "OnDestroy");
        });

        await RunActorCase("destroy detaches contexts once and keeps the shared controller for other actors", () =>
        {
            var actor = AttachActor(CreateInputActor());
            var other = AttachActor(CreateInputActor());
            var extra = new InputContext("extra", 0).Bind("Player/Fire", _ => { }, eInputEvent.Performed, consume: false);
            actor.Register(extra);
            ActorMessage(actor, "OnDestroy");
            Check(!IsRegistered(actor.Input) && !IsRegistered(extra) && !Dispatches(actor) && Dispatches(other), "Destroyed actor retained input or removed another actor");
            Check(actor.Input.Actor == null && extra.Actor == null && RegisteredContextCount(actor) == 0 && actor.Releases == 1, "Destruction kept context ownership or repeated the release hook");
            ThrowsSync<ObjectDisposedException>(() => actor.Register(new InputContext("late", 0))).GetAwaiter().GetResult();
            ThrowsSync<ObjectDisposedException>(() => actor.Spawn()).GetAwaiter().GetResult();
            other.Register(extra);
            Check(ReferenceEquals(extra.Actor, other) && IsRegistered(extra), "Finally detached context could not be registered again");
            Check(!UnityEngine.Object.Destroyed.Exists(x => x is InputActionAsset), "Actor release destroyed the shared asset");
            ActorMessage(other, "OnDestroy");
        });

        await RunActorCase("registration while enabled is immediate, idempotent and reversible", () =>
        {
            var actor = AttachActor(CreateInputActor());
            int extra = 0;
            var context = new InputContext("extra", 10).Bind("Player/Fire", _ => extra++, eInputEvent.Performed, consume: false);
            actor.Register(context);
            actor.Register(context);
            ProjectFire.Trigger(InputActionPhase.Performed);
            Check(extra == 1 && actor.Fires == 1, "Registration while enabled was delayed or duplicated");
            actor.Unregister(context);
            actor.enabled = false;
            ActorMessage(actor, "OnDisable");
            actor.enabled = true;
            ActorMessage(actor, "OnEnable");
            ProjectFire.Trigger(InputActionPhase.Performed);
            Check(extra == 1 && actor.Fires == 2 && !IsRegistered(context), "Unregistered context returned on reenable");
            ActorMessage(actor, "OnDestroy");
        });

        await RunActorCase("invalid registration leaves no partial state and disabled registration waits for enable", () =>
        {
            var actor = AttachActor(CreateInputActor());
            var invalid = new InputContext("invalid", 0).Bind("Player/Missing", _ => { });
            ThrowsSync<ArgumentException>(() => actor.Register(invalid)).GetAwaiter().GetResult();
            actor.enabled = false;
            ActorMessage(actor, "OnDisable");
            var deferred = new InputContext("deferred", 0).Bind("Player/Fire", _ => { }, eInputEvent.Performed, consume: false);
            actor.Register(deferred);
            Check(!IsRegistered(deferred), "Disabled actor registered input immediately");
            actor.enabled = true;
            ActorMessage(actor, "OnEnable");
            Check(!IsRegistered(invalid) && invalid.Actor == null && IsRegistered(actor.Input) && IsRegistered(deferred), "Rejected or deferred registration state is wrong");
            ActorMessage(actor, "OnDestroy");
        });

        await RunActorCase("input reset failure on plain disable detaches every context, propagates unchanged, skips the disable hook and quarantines until destruction releases once", () =>
        {
            var actor = new GameObject("Actor").AddComponent<ActorProbe>();
            var failure = new InvalidOperationException("Reset failed");
            actor.Input = CreateHeldContext(() => throw failure);
            AttachActor(actor);
            int laterResets = 0;
            var later = CreateHeldContext(() => laterResets++);
            actor.Register(later);
            ProjectFire.Trigger(InputActionPhase.Started);
            actor.enabled = false;
            var caught = CaptureSync(() => ActorMessage(actor, "OnDisable"));
            Check(ReferenceEquals(caught, failure), "Disable wrapped or lost the reset error");
            Check(TraceOf(actor) == "init,enter,enable" && laterResets == 0, "Steps after the reset failure still ran");
            Check(!IsRegistered(actor.Input) && !IsRegistered(later), "A reset failure left a disabled actor receiving input");
            CheckQuarantined(actor);
            Check(!IsRegistered(later), "Faulted actor registered a later context on enable");
            ActorMessage(actor, "OnDestroy");
            Check(TraceOf(actor) == "init,enter,enable,release" && laterResets == 1, "Destruction did not finish the faulted spawn once");
            Check(actor.Input.Actor == null && later.Actor == null && RegisteredContextCount(actor) == 0, "Destroyed actor kept context registrations");
        });

        await RunActorCase("input reset failure during destruction propagates unchanged, skips later resets and the release hook and still unregisters input", () =>
        {
            var actor = new GameObject("Actor").AddComponent<ActorProbe>();
            var first = new InvalidOperationException("Fire reset failed");
            actor.Input = CreateHeldContext(() => throw first);
            AttachActor(actor);
            int laterResets = 0;
            var second = CreateHeldContext(() => laterResets++);
            actor.Register(second);
            ProjectFire.Trigger(InputActionPhase.Started);
            var caught = CaptureSync(() => ActorMessage(actor, "OnDestroy"));
            Check(ReferenceEquals(caught, first) && laterResets == 0, "A later reset ran after the first error or replaced it");
            Check(TraceOf(actor) == "init,enter,enable", "Steps after the reset failure still ran");
            Check(!IsRegistered(actor.Input) && !IsRegistered(second) && actor.Input.Actor == null && second.Actor == null, "Destroyed actor kept context registrations");
            ThrowsSync<ObjectDisposedException>(() => actor.Spawn()).GetAwaiter().GetResult();
        });

        await RunActorCase("disable hook failure during destruction propagates unchanged, skips the release hook and still unregisters input", () =>
        {
            var actor = AttachActor(CreateInputActor());
            var failure = new InvalidOperationException("Disable hook failed");
            actor.DisabledAction = () => throw failure;
            actor.ReleasedAction = () => throw new InvalidOperationException("Release hook failed");
            var later = CreateFireContext(actor);
            actor.Register(later);
            var caught = CaptureSync(() => ActorMessage(actor, "OnDestroy"));
            Check(ReferenceEquals(caught, failure), "Destruction wrapped, lost or replaced the disable error");
            Check(TraceOf(actor) == "init,enter,enable,disable" && !IsRegistered(actor.Input) && !IsRegistered(later) && !Dispatches(actor),
                "Disable failure ran the release hook or retained input");
            Check(actor.Input.Actor == null && later.Actor == null, "Destroyed actor kept context registrations");
        });

        await RunActorCase("return cannot respawn the actor from an input reset or the release hook", () =>
        {
            using var pool = CreateActorPool();
            var actor = SpawnFirstActor(pool);
            bool resetRejected = false, releaseRejected = false;
            actor.Register(CreateHeldContext(() =>
            {
                ThrowsSync<InvalidOperationException>(() => actor.Spawn()).GetAwaiter().GetResult();
                resetRejected = true;
            }));
            actor.ReleasedAction = () =>
            {
                ThrowsSync<InvalidOperationException>(() => actor.Spawn()).GetAwaiter().GetResult();
                releaseRejected = true;
            };
            ProjectFire.Trigger(InputActionPhase.Started);
            ReturnActor(pool, actor);
            Check(resetRejected && releaseRejected, "Return accepted a nested spawn");
            Check(actor.Count("enter") == 1 && actor.Releases == 1 && pool.InactiveCount == 1, "Nested spawn entered or repeated the release");
            Global.Input.SetInputAllowed(false);
            Global.Input.SetInputAllowed(true);
            Check(!IsRegistered(actor.Input) && !Dispatches(actor), "Reset resurrected returned input");
        });

        await RunActorCase("init failure is retried by spawn and a failed retry propagates without entering or releasing", () =>
        {
            var actor = CreateInputActor();
            var retryError = new InvalidOperationException("Actor init retry failed");
            actor.InitAction = () => throw new InvalidOperationException("Actor init failed");
            ThrowsSync<InvalidOperationException>(() => ActorMessage(actor, "Awake")).GetAwaiter().GetResult();
            ActorMessage(actor, "OnEnable");
            Check(!IsRegistered(actor.Input), "OnEnable after a failed initialization registered input");
            actor.InitAction = () => throw retryError;
            var caught = CaptureSync(() => actor.Spawn());
            Check(ReferenceEquals(caught, retryError) && actor.Count("init") == 2, "Spawn did not retry the failed initialization or wrapped its error");
            ActorMessage(actor, "OnEnable");
            ActorMessage(actor, "Update");
            Check(!actor.Trace.Contains("enter") && !actor.Trace.Contains("enable") && !IsRegistered(actor.Input) && actor.Updates == 0, "Failed actor initialization later became active");
            ActorMessage(actor, "OnDestroy");
            Check(actor.Releases == 0 && actor.Input.Actor == null, "Destruction released a spawn that never entered");
        });

        await RunActorCase("failed initialization retries leave the actor unspawned and a later successful retry runs the full lifecycle", () =>
        {
            using var pool = CreateActorPool();
            var actor = pool.Get(null).GetComponent<ActorProbe>();
            actor.Input = CreateFireContext(actor);
            int failures = 3;
            actor.InitAction = () =>
            {
                if (failures-- > 0)
                    throw new InvalidOperationException("Actor init failed");
            };
            ThrowsSync<InvalidOperationException>(() => ActorMessage(actor, "Awake")).GetAwaiter().GetResult();
            ActorMessage(actor, "OnEnable");
            ThrowsSync<InvalidOperationException>(() => actor.Spawn()).GetAwaiter().GetResult();
            ThrowsSync<InvalidOperationException>(() => actor.Spawn()).GetAwaiter().GetResult();
            ActorMessage(actor, "Update");
            Check(TraceOf(actor) == "init,init,init" && !IsRegistered(actor.Input) && actor.Updates == 0, "Failed retry entered, became active or was not retried on the next spawn");
            ReturnActor(pool, actor);
            Check(TraceOf(actor) == "init,init,init" && pool.InactiveCount == 1, "Return released a spawn that never entered or kept the object");
            Check(ReferenceEquals(BorrowActor(pool, false), actor), "Pool did not lend the failed actor again");
            actor.Spawn();
            Check(TraceOf(actor) == "init,init,init,init,enter,enable" && Dispatches(actor), "Successful retry did not run the full lifecycle");
            Check(RegisteredContextCount(actor) == 1 && ReferenceEquals(actor.Input.Actor, actor), "Initialization retries duplicated or lost the input context");
            ReturnActor(pool, actor);
            Check(TraceOf(actor) == "init,init,init,init,enter,enable,disable,release", "Return after a recovered spawn did not release once");
        });

        await RunActorCase("return after an inactive spawn releases once without disable and the next spawn runs the full lifecycle", () =>
        {
            using var pool = CreateActorPool();
            var actor = BorrowActor(pool, true);
            actor.gameObject.SetActive(false);
            ActorMessage(actor, "OnDisable");
            actor.Spawn();
            Check(TraceOf(actor) == "init,enter" && !IsRegistered(actor.Input), "Inactive spawn activated or skipped enter");
            ReturnActor(pool, actor);
            Check(TraceOf(actor) == "init,enter,release" && pool.InactiveCount == 1 && !IsRegistered(actor.Input), "Return of an entered spawn ran disable or did not release once");
            BorrowActor(pool, false);
            actor.Spawn();
            Check(TraceOf(actor) == "init,enter,release,enter,enable" && Dispatches(actor), "Reborrowed spawn did not run the full lifecycle");
            ReturnActor(pool, actor);
            Check(TraceOf(actor) == "init,enter,release,enter,enable,disable,release", "Return after activation did not release once");
        });

        await RunActorCase("enter failure is released once on return and the next spawn enters again", () =>
        {
            using var pool = CreateActorPool();
            var actor = BorrowActor(pool, true);
            actor.EnterAction = () => throw new InvalidOperationException("Actor enter failed");
            ThrowsSync<InvalidOperationException>(() => actor.Spawn()).GetAwaiter().GetResult();
            ReturnActor(pool, actor);
            Check(TraceOf(actor) == "init,enter,release" && pool.InactiveCount == 1, "Failed enter was not released exactly once");
            actor.EnterAction = null;
            BorrowActor(pool, false);
            actor.Spawn();
            Check(TraceOf(actor) == "init,enter,release,enter,enable" && Dispatches(actor), "Spawn after a failed enter did not enter again");
        });

        await RunActorCase("enter failure cannot activate on a later enable", () =>
        {
            var actor = CreateInputActor();
            actor.EnterAction = () => throw new InvalidOperationException("Actor enter failed");
            AttachActor(actor, false);
            ThrowsSync<InvalidOperationException>(() => actor.Spawn()).GetAwaiter().GetResult();
            ActorMessage(actor, "OnDisable");
            ActorMessage(actor, "OnEnable");
            ActorMessage(actor, "Update");
            Check(!IsRegistered(actor.Input) && !actor.Trace.Contains("enable") && actor.Updates == 0, "Failed enter activated input");
            ThrowsSync<InvalidOperationException>(() => actor.Spawn()).GetAwaiter().GetResult();
            Check(actor.Count("enter") == 1, "Failed enter was retried");
            ActorMessage(actor, "OnDestroy");
            Check(actor.Releases == 1, "Destruction did not end the failed spawn");
        });

        await RunActorCase("enable hook failure is cleaned up by the next disable", () =>
        {
            var actor = CreateInputActor();
            actor.EnabledAction = () => throw new InvalidOperationException("Actor enable failed");
            AttachActor(actor, false);
            ThrowsSync<InvalidOperationException>(() => actor.Spawn()).GetAwaiter().GetResult();
            actor.enabled = false;
            ActorMessage(actor, "OnDisable");
            Check(!IsRegistered(actor.Input) && actor.Trace.Contains("disable"), "Disable after a failed enable hook left input registered");
            ActorMessage(actor, "OnDestroy");
        });

        await RunActorCase("derived awake without base call is completed at spawn", () =>
        {
            var obj = new GameObject("Actor");
            var pivot = obj.AddComponent<ProjectT.Pivot.ComPivotAgent>();
            var actor = obj.AddComponent<ActorSkippingAwakeProbe>();
            actor.Input = CreateFireContext(actor);
            AttachActor(actor, false);
            Check(actor.DerivedAwake && actor.Trace.Count == 0 && actor.PivotAgent == null && !IsRegistered(actor.Input), "Actor initialized before the base awake state was completed");
            actor.Spawn();
            Check(TraceOf(actor) == "init,enter,enable" && Dispatches(actor), "Spawn did not complete the skipped base awake once before entering");
            Check(ReferenceEquals(actor.PivotAgent, pivot), "Spawn did not resolve the pivot agent");
            ActorMessage(actor, "OnDestroy");
        });

        await RunActorCase("spawn on an uninitialized actor whose object is inactive is rejected without hooks", () =>
        {
            var actor = CreateInputActor();
            actor.gameObject.SetActive(false);
            ThrowsSync<InvalidOperationException>(() => actor.Spawn()).GetAwaiter().GetResult();
            Check(actor.Trace.Count == 0 && actor.PivotAgent == null && actor.Input.Actor == null, "Inactive spawn ran OnInit or OnEnter before Awake");
            actor.gameObject.SetActive(true);
            ActorMessage(actor, "Awake");
            ActorMessage(actor, "OnEnable");
            actor.Spawn();
            Check(TraceOf(actor) == "init,enter,enable" && Dispatches(actor), "Rejected inactive spawn blocked the later lifecycle");
            ActorMessage(actor, "OnDestroy");
        });

        await RunActorCase("component overrides cannot omit common registration and cleanup", () =>
        {
            var actor = new GameObject("Actor").AddComponent<ActorWithoutBaseProbe>();
            actor.Input = CreateFireContext(actor);
            AttachActor(actor);
            Check(IsRegistered(actor.Input) && Dispatches(actor), "Omitting base.Enable bypassed common registration");
            ActorMessage(actor, "Update");
            ActorMessage(actor, "LateUpdate");
            actor.enabled = false;
            ActorMessage(actor, "OnDisable");
            Check(!IsRegistered(actor.Input), "Omitting base.Disable bypassed context removal");
            actor.enabled = true;
            ActorMessage(actor, "OnEnable");
            Check(IsRegistered(actor.Input), "Omitting base hooks bypassed reactivation");
            ActorMessage(actor, "OnDestroy");
            Check(!IsRegistered(actor.Input) && actor.Input.Actor == null && TraceOf(actor) == "init", "Omitting base.OnRelease bypassed final cleanup");
        });

        await RunActorCase("spawn while inactive enters without activation and a later enable activates", () =>
        {
            foreach (bool deactivateObject in new[] { true, false })
            {
                var actor = AttachActor(CreateInputActor(), false);
                if (deactivateObject)
                    actor.gameObject.SetActive(false);
                else
                    actor.enabled = false;
                ActorMessage(actor, "OnDisable");
                actor.Spawn();
                Global.Input.SetInputAllowed(false);
                Global.Input.SetInputAllowed(true);
                ActorMessage(actor, "Update");
                Check(TraceOf(actor) == "init,enter" && !IsRegistered(actor.Input) && !Dispatches(actor) && actor.Updates == 0, "Inactive spawn activated or skipped enter");
                actor.gameObject.SetActive(true);
                actor.enabled = true;
                ActorMessage(actor, "OnEnable");
                Check(TraceOf(actor) == "init,enter,enable" && Dispatches(actor), "Enable after an inactive spawn did not activate once");
                ActorMessage(actor, "OnDestroy");
            }
        });

        await RunActorCase("input reset reenable wins over the older disable request", () =>
        {
            var actor = AttachActor(CreateInputActor());
            var held = CreateHeldContext(() =>
            {
                actor.enabled = true;
                ActorMessage(actor, "OnEnable");
            });
            actor.Register(held);
            ProjectFire.Trigger(InputActionPhase.Started);
            actor.enabled = false;
            ActorMessage(actor, "OnDisable");
            Check(IsRegistered(actor.Input) && IsRegistered(held) && !actor.Trace.Contains("disable"), "Older disable overwrote reset reenable");
            ActorMessage(actor, "OnDestroy");
        });

        await RunActorCase("destruction rejects spawn from the disable hook, skips the release hook and still unregisters input", () =>
        {
            var actor = AttachActor(CreateInputActor());
            actor.DisabledAction = () => actor.Spawn();
            var caught = CaptureSync(() => ActorMessage(actor, "OnDestroy"));
            Check(caught is ObjectDisposedException, "Destruction callback spawn was not rejected as disposed");
            Check(!IsRegistered(actor.Input) && actor.Input.Actor == null && actor.Releases == 0 && actor.Count("enter") == 1, "Destruction callback spawned, ran the release hook or retained input");
        });

        await RunActorCase("input shutdown detaches contexts and later actor cleanup does not query the stopped manager", () =>
        {
            var enabledActor = AttachActor(CreateInputActor());
            var disabledActor = AttachActor(CreateInputActor());
            disabledActor.enabled = false;
            ActorMessage(disabledActor, "OnDisable");
            actorHost.Shutdown();
            Check(!IsRegistered(enabledActor.Input) && !IsRegistered(disabledActor.Input), "Input shutdown retained contexts");
            ThrowsSync<InvalidOperationException>(() => _ = Global.Input).GetAwaiter().GetResult();
            enabledActor.enabled = false;
            ActorMessage(enabledActor, "OnDisable");
            ActorMessage(enabledActor, "OnDestroy");
            ActorMessage(disabledActor, "OnDestroy");
            Check(enabledActor.Releases == 1 && disabledActor.Releases == 1 && enabledActor.Trace.Contains("disable"), "Actor cleanup after input shutdown was skipped");
        });

        await RunActorCase("one input context cannot be registered by two actors until the first unregisters or is destroyed", () =>
        {
            var first = AttachActor(CreateInputActor());
            var second = AttachActor(CreateInputActor());
            ThrowsSync<InvalidOperationException>(() => second.Register(first.Input)).GetAwaiter().GetResult();
            Check(ReferenceEquals(first.Input.Actor, first) && Dispatches(first) && RegisteredContextCount(second) == 1, "Rejected sharing changed either registration");
            var context = first.Input;
            first.Unregister(context);
            Check(context.Actor == null && !IsRegistered(context), "Unregister kept the actor registration");
            second.Register(context);
            Check(ReferenceEquals(context.Actor, second) && IsRegistered(context), "Unregistered context could not be registered again");
            var shared = new InputContext("shared", 0).Bind("Player/Fire", _ => { }, eInputEvent.Performed, consume: false);
            first.Register(shared);
            ThrowsSync<InvalidOperationException>(() => second.Register(shared)).GetAwaiter().GetResult();
            ActorMessage(first, "OnDestroy");
            second.Register(shared);
            Check(ReferenceEquals(shared.Actor, second) && IsRegistered(shared), "Context of a destroyed actor could not be registered again");
            ActorMessage(second, "OnDestroy");
        });

        await RunActorCase("release hook runs once after disable on destruction and pool return", () =>
        {
            var destroyed = AttachActor(CreateInputActor());
            ActorMessage(destroyed, "OnDestroy");
            Check(TraceOf(destroyed) == "init,enter,enable,disable,release", "Destruction did not disable before a single release");
            var pool = CreateActorPool();
            var returned = SpawnFirstActor(pool);
            ReturnActor(pool, returned);
            Check(TraceOf(returned) == "init,enter,enable,disable,release", "Return did not disable before a single release");
            pool.Dispose();
            ActorMessage(returned, "OnDestroy");
            Check(destroyed.Releases == 1 && returned.Releases == 1 && returned.Count("disable") == 1, "Later cleanup skipped or repeated the release hook");
        });

        await RunActorCase("input reset failure during return propagates unchanged, skips later steps, rejects reuse and dispose destroys without releasing", () =>
        {
            var pool = CreateActorPool();
            var actor = SpawnFirstActor(pool);
            var resetError = new InvalidOperationException("Reset failed");
            actor.Register(CreateHeldContext(() => throw resetError));
            int laterResets = 0;
            var later = CreateHeldContext(() => laterResets++);
            actor.Register(later);
            actor.ReleasedAction = () => throw new InvalidOperationException("Release hook failed");
            ProjectFire.Trigger(InputActionPhase.Started);
            var caught = CaptureSync(() => ReturnActor(pool, actor));
            Check(ReferenceEquals(caught, resetError) && laterResets == 0, "Return replaced the reset error or ran later resets");
            Check(TraceOf(actor) == "init,enter,enable", "Steps after the reset failure still ran");
            Check(!IsRegistered(actor.Input) && !IsRegistered(later), "Reset failure kept input attached");
            Check(ReferenceEquals(later.Actor, actor) && ReferenceEquals(actor.Input.Actor, actor), "Failed return dropped component lifetime registrations");
            Check(pool.ActiveCount == 1 && actor.gameObject.Active, "Failed return changed pool tracking or deactivated the object");
            CheckQuarantined(actor);
            CheckReturnRejected(pool, actor);
            DisposeFaultedPool(pool, actor);
            Check(actor.Releases == 0 && laterResets == 0 && later.Actor == null, "Destruction ran the release hook, reran input resets or kept a context");
        });

        await RunActorCase("input reset failure on plain disable rejects the later pool return and dispose destroys with a single release", () =>
        {
            var pool = CreateActorPool();
            var actor = SpawnFirstActor(pool);
            var resetError = new InvalidOperationException("Reset failed");
            actor.Register(CreateHeldContext(() => throw resetError));
            ProjectFire.Trigger(InputActionPhase.Started);
            actor.enabled = false;
            var caught = CaptureSync(() => ActorMessage(actor, "OnDisable"));
            Check(ReferenceEquals(caught, resetError) && TraceOf(actor) == "init,enter,enable", "Plain disable lost the reset error or ran later steps");
            CheckQuarantined(actor);
            CheckReturnRejected(pool, actor);
            Check(actor.Releases == 0, "Rejected return ran the release hook");
            DisposeFaultedPool(pool, actor);
            Check(TraceOf(actor) == "init,enter,enable,release", "Destruction did not end the faulted spawn once without disabling again");
        });

        await RunActorCase("disable hook failure during return propagates unchanged over a later release error and skips the release hook", () =>
        {
            var pool = CreateActorPool();
            var actor = SpawnFirstActor(pool);
            var disableError = new InvalidOperationException("Disable hook failed");
            actor.DisabledAction = () => throw disableError;
            actor.ReleasedAction = () => throw new InvalidOperationException("Release hook failed");
            var caught = CaptureSync(() => ReturnActor(pool, actor));
            Check(ReferenceEquals(caught, disableError), "Return wrapped, lost or replaced the disable error");
            Check(TraceOf(actor) == "init,enter,enable,disable", "Disable failure still ran the release hook");
            Check(!IsRegistered(actor.Input) && ReferenceEquals(actor.Input.Actor, actor) && pool.ActiveCount == 1, "Disable failure kept input attached or changed pool tracking");
            CheckQuarantined(actor);
            CheckReturnRejected(pool, actor);
            DisposeFaultedPool(pool, actor);
            Check(actor.Releases == 0 && actor.Count("disable") == 1, "Destruction repeated the disable or ran the release hook");
        });

        await RunActorCase("release hook failure during return propagates unchanged after ending the spawn and dispose destroys without releasing again", () =>
        {
            var pool = CreateActorPool();
            var actor = SpawnFirstActor(pool);
            var releaseError = new InvalidOperationException("Release hook failed");
            actor.ReleasedAction = () => throw releaseError;
            var caught = CaptureSync(() => ReturnActor(pool, actor));
            Check(ReferenceEquals(caught, releaseError), "Return wrapped or lost the hook error");
            Check(TraceOf(actor) == "init,enter,enable,disable,release", "Return skipped a step before the release hook");
            Check(!IsRegistered(actor.Input) && !Dispatches(actor) && ReferenceEquals(actor.Input.Actor, actor), "Hook failure left input attached or dropped the registration");
            Check(pool.ActiveCount == 1 && pool.InactiveCount == 0 && actor.gameObject.Active && actor.transform.parent != pool.Root, "Failed return changed pool tracking or deactivated the object");
            actor.ReleasedAction = null;
            CheckQuarantined(actor);
            CheckReturnRejected(pool, actor);
            DisposeFaultedPool(pool, actor);
            Check(actor.Releases == 1 && actor.Count("disable") == 1, "Destruction repeated the spawn end");
        });

        await RunActorCase("a return after a successful plain disable and enable cycle is not faulted and the actor is reused", () =>
        {
            using var pool = CreateActorPool();
            var actor = SpawnFirstActor(pool);
            actor.enabled = false;
            ActorMessage(actor, "OnDisable");
            actor.enabled = true;
            ActorMessage(actor, "OnEnable");
            ReturnActor(pool, actor);
            Check(TraceOf(actor) == "init,enter,enable,disable,enable,disable,release" && pool.ActiveCount == 0 && pool.InactiveCount == 1, "Return after a disable cycle was rejected or skipped a step");
            Check(ReferenceEquals(BorrowActor(pool, false), actor), "Pool did not lend the returned actor again");
            actor.Spawn();
            Check(actor.Count("enter") == 2 && actor.Count("enable") == 3 && Dispatches(actor), "Reused actor did not spawn again");
            ReturnActor(pool, actor);
            Check(actor.Releases == 2 && pool.InactiveCount == 1, "Second return was rejected");
        });

        await RunActorCase("pool return ends the spawn once and the following disable does not repeat it", () =>
        {
            using var pool = CreateActorPool();
            var actor = BorrowActor(pool, true);
            actor.Spawn();
            Check(TraceOf(actor) == "init,enter,enable", "Spawn did not enter and enable the actor");
            ReturnActor(pool, actor);
            Check(TraceOf(actor) == "init,enter,enable,disable,release", "Return did not disable before a single release");
            Check(!actor.gameObject.Active && !IsRegistered(actor.Input) && ReferenceEquals(actor.Input.Actor, actor), "Return kept the object active, kept input attached or dropped the registration");
            Check(pool.ActiveCount == 0 && pool.InactiveCount == 1, "Return did not move the object to the inactive list");
        });

        await RunActorCase("returning during a spawn transition is rejected and keeps the object borrowed", () =>
        {
            using var pool = CreateActorPool();
            var actor = SpawnFirstActor(pool);
            ReturnActor(pool, actor);
            BorrowActor(pool, false);
            actor.EnterAction = () => ThrowsSync<InvalidOperationException>(() => ReturnActor(pool, actor)).GetAwaiter().GetResult();
            actor.Spawn();
            Check(actor.Count("enter") == 2 && actor.Releases == 1, "Return during enter ended the spawn");
            Check(pool.ActiveCount == 1 && pool.InactiveCount == 0, "Return during enter moved the object to the inactive list");
            Check(actor.gameObject.Active && actor.transform.parent != pool.Root && actor.Count("enable") == 2, "Rejected return deactivated the object");
            actor.EnterAction = null;
            ReturnActor(pool, actor);
            Check(actor.Releases == 2 && pool.ActiveCount == 0 && pool.InactiveCount == 1, "Later return did not end the spawn");
        });

        await RunActorCase("returning from an input reset during plain disable is rejected, the disable hook still runs once and a later return releases once", () =>
        {
            using var pool = CreateActorPool();
            var actor = SpawnFirstActor(pool);
            Exception caught = null;
            actor.Register(CreateHeldContext(() => caught = CaptureSync(() => ReturnActor(pool, actor))));
            ProjectFire.Trigger(InputActionPhase.Started);
            actor.enabled = false;
            ActorMessage(actor, "OnDisable");
            Check(IsRejection(caught), "Return during plain disable was not rejected");
            Check(TraceOf(actor) == "init,enter,enable,disable" && pool.ActiveCount == 1 && actor.gameObject.Active, "Rejected return ended the spawn, skipped the disable hook or changed pool tracking");
            actor.enabled = true;
            ActorMessage(actor, "OnEnable");
            Check(actor.Count("enable") == 2 && Dispatches(actor), "Actor did not keep the spawn after the rejected return");
            ReturnActor(pool, actor);
            Check(TraceOf(actor) == "init,enter,enable,disable,enable,disable,release" && pool.InactiveCount == 1, "Later return was rejected or repeated a step");
        });

        await RunActorCase("returning from the disable hook during plain disable is rejected and a later return releases once", () =>
        {
            using var pool = CreateActorPool();
            var actor = SpawnFirstActor(pool);
            Exception caught = null;
            actor.DisabledAction = () => caught = CaptureSync(() => ReturnActor(pool, actor));
            actor.enabled = false;
            ActorMessage(actor, "OnDisable");
            Check(IsRejection(caught), "Return from the disable hook was not rejected");
            Check(TraceOf(actor) == "init,enter,enable,disable" && pool.ActiveCount == 1 && actor.gameObject.Active, "Rejected return ended the spawn or changed pool tracking");
            actor.DisabledAction = null;
            actor.enabled = true;
            ActorMessage(actor, "OnEnable");
            Check(actor.Count("enable") == 2 && Dispatches(actor), "Actor did not keep the spawn after the rejected return");
            ReturnActor(pool, actor);
            Check(TraceOf(actor) == "init,enter,enable,disable,enable,disable,release" && pool.InactiveCount == 1, "Later return was rejected or repeated a step");
        });

        await RunActorCase("a nested disable from an input reset reenable keeps returns rejected for the rest of the outer disable", () =>
        {
            using var pool = CreateActorPool();
            var actor = SpawnFirstActor(pool);
            Exception caught = null;
            actor.Register(CreateHeldContext(() =>
            {
                actor.EnabledAction = () =>
                {
                    actor.EnabledAction = null;
                    actor.enabled = false;
                    ActorMessage(actor, "OnDisable");
                    actor.DisabledAction = () => caught = CaptureSync(() => ReturnActor(pool, actor));
                };
                actor.enabled = true;
                ActorMessage(actor, "OnEnable");
            }));
            ProjectFire.Trigger(InputActionPhase.Started);
            actor.enabled = false;
            ActorMessage(actor, "OnDisable");
            Check(IsRejection(caught), "Nested disable unlocked returns for the outer disable");
            Check(TraceOf(actor) == "init,enter,enable,enable,disable,disable" && pool.ActiveCount == 1, "Rejected return ended the spawn or changed pool tracking");
            actor.DisabledAction = null;
            ReturnActor(pool, actor);
            Check(TraceOf(actor) == "init,enter,enable,enable,disable,disable,release" && pool.InactiveCount == 1, "Later return was rejected or repeated a step");
        });

        await RunActorCase("plain disable keeps the spawn until the pool return", () =>
        {
            using var pool = CreateActorPool();
            var actor = SpawnFirstActor(pool);
            actor.enabled = false;
            ActorMessage(actor, "OnDisable");
            Check(TraceOf(actor) == "init,enter,enable,disable", "Plain disable ended the spawn");
            actor.enabled = true;
            ActorMessage(actor, "OnEnable");
            Check(TraceOf(actor) == "init,enter,enable,disable,enable" && Dispatches(actor), "Reenable did not reactivate the kept spawn");
            ReturnActor(pool, actor);
            Check(actor.Releases == 1 && actor.Count("enter") == 1, "Return after a plain disable did not release once");
        });

        await RunActorCase("reborrow enters immediately on spawn and rejects spawning twice", () =>
        {
            using var pool = CreateActorPool();
            var actor = SpawnFirstActor(pool);
            ReturnActor(pool, actor);
            var again = BorrowActor(pool, false);
            Check(ReferenceEquals(again, actor) && TraceOf(actor) == "init,enter,enable,disable,release" && !IsRegistered(actor.Input), "Reborrow entered without a spawn");
            actor.Spawn();
            Check(TraceOf(actor) == "init,enter,enable,disable,release,enter,enable" && Dispatches(actor), "Reborrow spawn did not enter immediately");
            ThrowsSync<InvalidOperationException>(() => actor.Spawn()).GetAwaiter().GetResult();
            Check(actor.Count("enter") == 2 && actor.Releases == 1 && Dispatches(actor), "Rejected spawn changed the current spawn");
            ReturnActor(pool, actor);
            Check(actor.Releases == 2, "Second return did not end the spawn once");
        });

        await RunActorCase("scene pool cleanup, return and destruction in any order release the actor once", () =>
        {
            using (var pool = CreateActorPool())
            {
                var actor = SpawnFirstActor(pool);
                pool.Dispose();
                Check(TraceOf(actor) == "init,enter,enable,disable,release" && UnityEngine.Object.Destroyed.Contains(actor.gameObject), "Pool cleanup did not end the borrowed spawn");
                ActorMessage(actor, "OnDisable");
                ActorMessage(actor, "OnDestroy");
                Check(actor.Releases == 1 && !IsRegistered(actor.Input) && actor.Input.Actor == null, "Destruction after pool cleanup released again or kept input");
            }

            using (var pool = CreateActorPool())
            {
                var actor = SpawnFirstActor(pool);
                ReturnActor(pool, actor);
                pool.Dispose();
                ActorMessage(actor, "OnDestroy");
                DestroyPooledObject(actor.gameObject);
                Check(actor.Releases == 1 && UnityEngine.Object.Destroyed.Contains(actor.gameObject), "Destruction after return released again");
            }

            foreach (bool componentFirst in new[] { true, false })
            {
                using var pool = CreateActorPool();
                var actor = SpawnFirstActor(pool);
                if (componentFirst)
                {
                    ActorMessage(actor, "OnDestroy");
                    DestroyPooledObject(actor.gameObject);
                }
                else
                {
                    DestroyPooledObject(actor.gameObject);
                    ActorMessage(actor, "OnDestroy");
                }
                Check(TraceOf(actor) == "init,enter,enable,disable,release", "Destruction order changed the release count");
                Check(pool.ActiveCount == 0 && pool.InactiveCount == 0, "Destroyed object stayed tracked");
            }

            using (var pool = CreateActorPool())
            {
                var actor = SpawnFirstActor(pool);
                Check(pool.Remove(actor.gameObject), "Remove rejected the borrowed object");
                DestroyPooledObject(actor.gameObject);
                ActorMessage(actor, "OnDestroy");
                Check(actor.Releases == 1 && UnityEngine.Object.Destroyed.Contains(actor.gameObject), "Remove and destruction released again");
            }
        });

        await RunActorCase("first borrow enters immediately at spawn after awake and enable", () =>
        {
            using var pool = CreateActorPool();
            var actor = BorrowActor(pool, true);
            Check(TraceOf(actor) == "init" && !IsRegistered(actor.Input) && !Dispatches(actor), "OnEnable before Spawn entered or registered input");
            actor.Spawn();
            Check(TraceOf(actor) == "init,enter,enable" && Dispatches(actor), "First borrow spawn did not enter immediately");
        });

        await RunActorCase("reborrow does not keep the previous spawn state after the release hook", () =>
        {
            using var pool = CreateActorPool();
            var actor = BorrowActor(pool, true);
            actor.SpawnData = 5;
            actor.Spawn();
            Check(actor.AppliedData == 5, "Enter did not apply the spawn data set before Spawn");
            ReturnActor(pool, actor);
            Check(actor.SpawnData == 0 && actor.AppliedData == 0, "Return kept the previous spawn state");
            BorrowActor(pool, false);
            Check(actor.AppliedData == 0 && actor.Count("enter") == 1, "Reborrow entered before the spawner set new data");
            actor.SpawnData = 7;
            actor.Spawn();
            Check(actor.AppliedData == 7 && actor.Count("enter") == 2, "Reborrow spawn did not apply the new data");
        });

        await RunActorCase("repeated spawns do not duplicate input registration", () =>
        {
            using var pool = CreateActorPool();
            var actor = SpawnFirstActor(pool);
            for (int i = 0; i < 3; i++)
            {
                Check(RegisteredContextCount(actor) == 1 && ReferenceEquals(actor.Input.Owner, Global.Input.Controller), "Spawn duplicated or skipped the input registration");
                int before = actor.Fires;
                ProjectFire.Trigger(InputActionPhase.Performed);
                Check(actor.Fires == before + 1, "Spawn duplicated or skipped input dispatch");
                ReturnActor(pool, actor);
                Check(!IsRegistered(actor.Input) && RegisteredContextCount(actor) == 1 && ReferenceEquals(actor.Input.Actor, actor), "Return dropped or kept the controller registration");
                BorrowActor(pool, false);
                actor.Spawn();
            }
            Check(actor.Count("init") == 1 && actor.Count("enter") == 4 && actor.Releases == 3, "Repeated spawns reinitialized or skipped hooks");
        });

        await RunActorCase("init first failure is completed by the spawn retry without duplicate contexts", () =>
        {
            var actor = CreateInputActor();
            int failures = 1;
            actor.InitAction = () =>
            {
                if (failures-- > 0)
                    throw new InvalidOperationException("Actor init failed");
            };
            ThrowsSync<InvalidOperationException>(() => ActorMessage(actor, "Awake")).GetAwaiter().GetResult();
            ActorMessage(actor, "OnEnable");
            Check(!actor.Trace.Contains("enter") && !IsRegistered(actor.Input), "Failed initialization entered before the retry");
            actor.Spawn();
            Check(TraceOf(actor) == "init,init,enter,enable", "Spawn did not complete the failed initialization");
            Check(RegisteredContextCount(actor) == 1 && ReferenceEquals(actor.Input.Actor, actor) && Dispatches(actor), "Initialization retry duplicated or lost the input context");
            ActorMessage(actor, "OnDestroy");
            Check(RegisteredContextCount(actor) == 0 && actor.Input.Actor == null, "Destruction kept the retried context");
        });

        await RunActorCase("input reset during return cannot reactivate input or enable through disable and reenable", () =>
        {
            using var pool = CreateActorPool();
            var actor = SpawnFirstActor(pool);
            var held = CreateHeldContext(() =>
            {
                ActorMessage(actor, "OnDisable");
                ActorMessage(actor, "OnEnable");
            });
            actor.Register(held);
            ProjectFire.Trigger(InputActionPhase.Started);
            ReturnActor(pool, actor);
            ActorMessage(actor, "Update");
            Check(TraceOf(actor) == "init,enter,enable,disable,release" && actor.Updates == 0, "Reset reenable reactivated the returning actor");
            Check(!IsRegistered(actor.Input) && !IsRegistered(held) && pool.InactiveCount == 1, "Reset reenable registered input during return");
        });

        await RunActorCase("per-spawn contexts registered in enter and released in release do not accumulate across reborrows", () =>
        {
            using var pool = CreateActorPool();
            var actor = BorrowActor(pool, true);
            int spawnFires = 0;
            InputContext spawnContext = null;
            actor.EnterAction = () =>
            {
                spawnContext = new InputContext("spawn", 0).Bind("Player/Fire", _ => spawnFires++, eInputEvent.Performed, consume: false);
                actor.Register(spawnContext);
            };
            actor.ReleasedAction = () => actor.Unregister(spawnContext);
            actor.Spawn();
            for (int i = 0; i < 3; i++)
            {
                Check(RegisteredContextCount(actor) == 2 && IsRegistered(spawnContext), "Per-spawn context was not activated");
                int before = spawnFires;
                ProjectFire.Trigger(InputActionPhase.Performed);
                Check(spawnFires == before + 1, "Previous per-spawn contexts still received input");
                var released = spawnContext;
                ReturnActor(pool, actor);
                Check(RegisteredContextCount(actor) == 1 && released.Actor == null && !IsRegistered(released), "Release hook did not remove the per-spawn context");
                BorrowActor(pool, false);
                actor.Spawn();
            }
        });

        await RunActorCase("spawn serial changes on every spawn start and end, on destruction and on destruction without a spawn", () =>
        {
            using var pool = CreateActorPool();
            var actor = BorrowActor(pool, true);
            var serials = new List<int> { actor.SpawnSerial };
            actor.EnterAction = () => serials.Add(actor.SpawnSerial);
            actor.Spawn();
            ReturnActor(pool, actor);
            serials.Add(actor.SpawnSerial);
            BorrowActor(pool, false);
            actor.Spawn();
            ReturnActor(pool, actor);
            serials.Add(actor.SpawnSerial);
            Check(new HashSet<int>(serials).Count == serials.Count, "Spawn start or end reused a serial: " + string.Join(",", serials));
            var destroyed = AttachActor(new GameObject("Actor").AddComponent<ActorProbe>());
            int spawned = destroyed.SpawnSerial;
            ActorMessage(destroyed, "OnDestroy");
            Check(destroyed.SpawnSerial != spawned, "Destruction did not change the serial");
            var unspawned = AttachActor(new GameObject("Actor").AddComponent<ActorProbe>(), false);
            int initial = unspawned.SpawnSerial;
            ActorMessage(unspawned, "OnDestroy");
            Check(unspawned.SpawnSerial != initial, "Destruction without a spawn did not change the serial");
        });

        await RunActorCase("actor without a skill agent keeps the lifecycle and exposes no agent", () =>
        {
            var actor = AttachActor(CreateInputActor());
            ActorMessage(actor, "Update");
            ActorMessage(actor, "OnDestroy");
            Check(actor.SkillAgent == null && actor.Updates == 1, "Actor without skills created an agent or skipped the update hook");
            Check(TraceOf(actor) == "init,enter,enable,disable,release", "Actor without skills changed the lifecycle order");
        });

        await RunSkillActorCase("adding a skill agent twice is rejected and keeps the first agent", () =>
        {
            var actor = new GameObject("Actor").AddComponent<ActorProbe>();
            SkillAgent first = null;
            actor.InitAction = () =>
            {
                first = actor.AddSkills();
                ThrowsSync<InvalidOperationException>(() => actor.AddSkills()).GetAwaiter().GetResult();
            };
            AttachActor(actor);
            Check(first != null && ReferenceEquals(actor.SkillAgent, first) && TraceOf(actor) == "init,enter,enable", "Second add replaced the agent or broke the lifecycle");
            ActorMessage(actor, "OnDestroy");
        });

        await RunSkillActorCase("an init that adds a skill agent and fails is retried by spawn with a fresh agent", () =>
        {
            var actor = new GameObject("Actor").AddComponent<ActorProbe>();
            SkillAgent failed = null;
            actor.InitAction = () =>
            {
                failed = actor.AddSkills();
                throw new InvalidOperationException("Actor init failed");
            };
            ThrowsSync<InvalidOperationException>(() => ActorMessage(actor, "Awake")).GetAwaiter().GetResult();
            actor.InitAction = () => actor.AddSkills();
            actor.EnterAction = () => actor.SkillAgent.GiveSkill(TestSkill);
            ActorMessage(actor, "OnEnable");
            actor.Spawn();
            Check(failed != null && actor.SkillAgent != null && !ReferenceEquals(actor.SkillAgent, failed) && TraceOf(actor) == "init,init,enter,enable", "Spawn retry was blocked by the agent added in the failed init");
            ActorMessage(actor, "OnDestroy");
        });

        await RunSkillActorCase("registration is closed before spawn, opens at enter and a failed enter is still released", () =>
        {
            var actor = AttachActor(new GameObject("Actor").AddComponent<SkillActorProbe>(), false);
            ThrowsSync<InvalidOperationException>(() => actor.SkillAgent.GiveSkill(TestSkill)).GetAwaiter().GetResult();
            ThrowsSync<InvalidOperationException>(() => actor.SkillAgent.ApplyEffect(LongEffect, actor)).GetAwaiter().GetResult();
            var failure = new InvalidOperationException("Actor enter failed");
            actor.EnterAction = () =>
            {
                actor.SkillAgent.GiveSkill(TestSkill);
                actor.SkillAgent.ActivateSkill(TestSkillId);
                actor.SkillAgent.ApplyEffect(LongEffect, actor);
                throw failure;
            };
            var caught = CaptureSync(() => actor.Spawn());
            Check(ReferenceEquals(caught, failure) && TraceOf(actor) == "init,enter,skill-activate,effect-apply:1", "Enter could not register skills or the error was wrapped");
            ActorMessage(actor, "OnDestroy");
            Check(TraceOf(actor) == "init,enter,skill-activate,effect-apply:1,effect-remove:1:Released,skill-cancel,release", "Failed enter was not released before the release hook");
        });

        await RunSkillActorCase("skill tick runs before the update hook and a tick that deactivates skips it", () =>
        {
            var actor = new GameObject("Actor").AddComponent<SkillActorProbe>();
            actor.EnterAction = () =>
            {
                actor.SkillAgent.GiveSkill(TestSkill);
                actor.SkillAgent.ActivateSkill(TestSkillId);
                actor.SkillAgent.ApplyEffect(ShortEffect, actor);
            };
            actor.UpdateAction = () => actor.Trace.Add("update");
            AttachActor(actor);
            ActorMessage(actor, "Update");
            Check(TraceOf(actor) == "init,enter,skill-activate,effect-apply:3,enable,effect-remove:3:Expired,skill-tick,update", "Effect expiry or skill tick did not run before the update hook");
            TestSkillAction.TickAction = () =>
            {
                actor.enabled = false;
                ActorMessage(actor, "OnDisable");
            };
            ActorMessage(actor, "Update");
            Check(actor.Updates == 1 && TraceOf(actor).EndsWith(",update,skill-tick,skill-cancel,disable"), "Update hook ran after the tick deactivated the actor");
            ActorMessage(actor, "OnDestroy");
            Check(actor.Releases == 1 && actor.Count("skill-cancel") == 1, "Destruction after a tick deactivation repeated the cancel or skipped the release hook");
        });

        await RunSkillActorCase("deactivation cancels active skills before the disable hook and a cancel failure skips the disable hook and quarantines the actor", () =>
        {
            var actor = new GameObject("Actor").AddComponent<SkillActorProbe>();
            actor.Input = CreateFireContext(actor);
            actor.EnterAction = () => actor.SkillAgent.GiveSkill(TestSkill);
            AttachActor(actor);
            actor.SkillAgent.ActivateSkill(TestSkillId);
            actor.gameObject.SetActive(false);
            ActorMessage(actor, "OnDisable");
            Check(TraceOf(actor) == "init,enter,enable,skill-activate,skill-cancel,disable", "Deactivation did not cancel the active skill before the disable hook");
            actor.gameObject.SetActive(true);
            ActorMessage(actor, "OnEnable");
            actor.SkillAgent.ActivateSkill(TestSkillId);
            var failure = new InvalidOperationException("Skill cancel failed");
            TestSkillAction.CancelFailure = failure;
            actor.gameObject.SetActive(false);
            var caught = CaptureSync(() => ActorMessage(actor, "OnDisable"));
            Check(ReferenceEquals(caught, failure) && TraceOf(actor).EndsWith(",disable,enable,skill-activate,skill-cancel") && actor.Count("disable") == 1, "A cancel failure was wrapped or the disable hook still ran");
            Check(!IsRegistered(actor.Input), "A cancel failure kept input registered");
            TestSkillAction.CancelFailure = null;
            actor.gameObject.SetActive(true);
            CheckQuarantined(actor);
            ActorMessage(actor, "OnDestroy");
            Check(actor.Releases == 1 && actor.Count("skill-cancel") == 2 && actor.Count("disable") == 1, "Destruction canceled a canceled skill again, disabled again or skipped the release hook");
        });

        await RunSkillActorCase("returning from a skill cancel during plain disable is rejected, the disable hook still runs once and a later return releases once", () =>
        {
            using var pool = CreateActorPool<SkillActorProbe>();
            var actor = BorrowActor(pool, true);
            actor.EnterAction = () => actor.SkillAgent.GiveSkill(TestSkill);
            actor.Spawn();
            actor.SkillAgent.ActivateSkill(TestSkillId);
            Exception caught = null;
            TestSkillAction.CancelAction = () => caught = CaptureSync(() => ReturnActor(pool, actor));
            actor.enabled = false;
            ActorMessage(actor, "OnDisable");
            Check(IsRejection(caught), "Return from a skill cancel was not rejected");
            Check(TraceOf(actor) == "init,enter,enable,skill-activate,skill-cancel,disable" && pool.ActiveCount == 1 && actor.gameObject.Active, "Rejected return ended the spawn, skipped the disable hook or changed pool tracking");
            TestSkillAction.CancelAction = null;
            actor.enabled = true;
            ActorMessage(actor, "OnEnable");
            Check(actor.Count("enable") == 2 && Dispatches(actor), "Actor did not keep the spawn after the rejected return");
            ReturnActor(pool, actor);
            Check(TraceOf(actor).EndsWith(",disable,enable,disable,release") && actor.Releases == 1 && pool.InactiveCount == 1, "Later return was rejected or repeated a step");
        });

        await RunSkillActorCase("input reset failure on plain disable skips the skill cancel and the disable hook", () =>
        {
            var actor = new GameObject("Actor").AddComponent<SkillActorProbe>();
            var resetError = new InvalidOperationException("Reset failed");
            actor.Input = CreateHeldContext(() => throw resetError);
            actor.EnterAction = () => actor.SkillAgent.GiveSkill(TestSkill);
            AttachActor(actor);
            actor.SkillAgent.ActivateSkill(TestSkillId);
            ProjectFire.Trigger(InputActionPhase.Started);
            actor.enabled = false;
            var caught = CaptureSync(() => ActorMessage(actor, "OnDisable"));
            Check(ReferenceEquals(caught, resetError) && TraceOf(actor) == "init,enter,enable,skill-activate", "A reset failure was wrapped or later deactivation steps still ran");
            CheckQuarantined(actor);
            ActorMessage(actor, "OnDestroy");
            Check(actor.Releases == 1 && actor.Count("disable") == 0 && !IsRegistered(actor.Input), "Destruction after a reset failure skipped the release hook, disabled or kept input");
        });

        await RunSkillActorCase("pool return removes every effect once before the release hook and a respawn starts with an empty agent", () =>
        {
            using var pool = CreateActorPool<SkillActorProbe>();
            var actor = BorrowActor(pool, true);
            actor.EnterAction = () =>
            {
                actor.SkillAgent.GiveSkill(TestSkill);
                actor.SkillAgent.ActivateSkill(TestSkillId);
                actor.SkillAgent.ApplyEffect(LongEffect, actor);
                actor.SkillAgent.ApplyEffect(IntervalEffect, actor);
            };
            actor.Spawn();
            ReturnActor(pool, actor);
            Check(TraceOf(actor) == "init,enter,skill-activate,effect-apply:1,effect-apply:2,effect-execute:2,enable,skill-cancel,disable,effect-remove:1:Released,effect-remove:2:Released,release",
                "Return did not cancel before disable or remove every effect once before the release hook");
            ThrowsSync<InvalidOperationException>(() => actor.SkillAgent.GiveSkill(TestSkill)).GetAwaiter().GetResult();
            ThrowsSync<InvalidOperationException>(() => actor.SkillAgent.ApplyEffect(LongEffect, actor)).GetAwaiter().GetResult();
            actor.Trace.Clear();
            BorrowActor(pool, false);
            actor.Spawn();
            ActorMessage(actor, "Update");
            Check(TraceOf(actor) == "enter,skill-activate,effect-apply:1,effect-apply:2,effect-execute:2,enable,skill-tick", "Respawn kept the previous registrations or could not register again");
            ReturnActor(pool, actor);
            Check(actor.Count("effect-remove:1:Released") == 1 && actor.Count("effect-remove:2:Released") == 1 && actor.Releases == 1, "Second return skipped or repeated an effect removal");
        });

        await RunSkillActorCase("destruction removes effects before the release hook and closes registration", () =>
        {
            var actor = AttachActor(new GameObject("Actor").AddComponent<SkillActorProbe>());
            actor.SkillAgent.ApplyEffect(LongEffect, actor);
            ActorMessage(actor, "OnDestroy");
            Check(TraceOf(actor) == "init,enter,enable,effect-apply:1,disable,effect-remove:1:Released,release", "Destruction did not remove the effect before the release hook");
            ThrowsSync<InvalidOperationException>(() => actor.SkillAgent.ApplyEffect(LongEffect, actor)).GetAwaiter().GetResult();
        });

        await RunSkillActorCase("a stun applied in enter cancels the active skill and blocks activation, pool return removes it as released and a respawn starts without the state", () =>
        {
            using var pool = CreateActorPool<SkillActorProbe>();
            var actor = BorrowActor(pool, true);
            Action<SkillTag, bool> tagChanged = (tag, active) => actor.Trace.Add("tag:" + tag.Name + ":" + active);
            actor.EnterAction = () =>
            {
                actor.SkillAgent.OnTagChanged += tagChanged;
                actor.SkillAgent.GiveSkill(TestSkill);
                actor.SkillAgent.ActivateSkill(TestSkillId);
                actor.SkillAgent.ApplyEffect(StunEffect, actor);
                actor.SkillAgent.ActivateSkill(TestSkillId);
            };
            actor.DisabledAction = () => actor.SkillAgent.OnTagChanged -= tagChanged;
            actor.Spawn();
            Check(TraceOf(actor) == "init,enter,skill-activate,skill-cancel,tag:State.Stun:True,effect-apply:5,enable" && actor.SkillAgent.HasTag(SkillDefinitions.StunTag), "The stun did not cancel the active skill before notifying or did not block activation");
            ReturnActor(pool, actor);
            Check(TraceOf(actor).EndsWith(",enable,disable,effect-remove:5:Released,release") && !actor.SkillAgent.HasTag(SkillDefinitions.StunTag), "Return did not remove the stun as released or kept the state");
            actor.Trace.Clear();
            actor.EnterAction = () =>
            {
                actor.SkillAgent.GiveSkill(TestSkill);
                actor.SkillAgent.ActivateSkill(TestSkillId);
            };
            BorrowActor(pool, false);
            actor.Spawn();
            Check(!actor.SkillAgent.HasTag(SkillDefinitions.StunTag) && TraceOf(actor) == "enter,skill-activate,enable", "Respawn kept the stun or blocked activation");
            ReturnActor(pool, actor);
        });

        await RunSkillActorCase("an actor without a status has none and adding a status twice is rejected and keeps the first status", () =>
        {
            var plain = AttachActor(new GameObject("Actor").AddComponent<ActorProbe>());
            Check(plain.Status == null, "Actor without AddStatus created a status");
            ActorMessage(plain, "OnDestroy");
            var actor = new GameObject("Actor").AddComponent<ActorProbe>();
            Status first = null;
            actor.InitAction = () =>
            {
                first = actor.AddStats();
                ThrowsSync<InvalidOperationException>(() => actor.AddStats()).GetAwaiter().GetResult();
            };
            AttachActor(actor);
            Check(first != null && ReferenceEquals(actor.Status, first) && TraceOf(actor) == "init,enter,enable", "Second add replaced the status or broke the lifecycle");
            ActorMessage(actor, "OnDestroy");
        });

        await RunSkillActorCase("an init that adds a status and fails is retried by spawn with a fresh status", () =>
        {
            var actor = new GameObject("Actor").AddComponent<ActorProbe>();
            Status failed = null;
            actor.InitAction = () =>
            {
                failed = actor.AddStats();
                throw new InvalidOperationException("Actor init failed");
            };
            ThrowsSync<InvalidOperationException>(() => ActorMessage(actor, "Awake")).GetAwaiter().GetResult();
            actor.InitAction = () => actor.AddStats();
            actor.EnterAction = () => actor.Status.SetBase(SkillDefinitions.AtkStat, 10f);
            ActorMessage(actor, "OnEnable");
            actor.Spawn();
            Check(failed != null && actor.Status != null && !ReferenceEquals(actor.Status, failed) && actor.Status.Get(SkillDefinitions.AtkStat) == 10f && TraceOf(actor) == "init,init,enter,enable", "Spawn retry was blocked by the status added in the failed init");
            ActorMessage(actor, "OnDestroy");
        });

        await RunSkillActorCase("pool return removes effects, then clears the status silently before the release hook and a respawn starts clean", () =>
        {
            using var pool = CreateActorPool<StatusActorProbe>();
            var actor = BorrowActor(pool, true);
            var source = new object();
            int events = 0;
            actor.Status.OnStatChanged += (type, previous, current) => events++;
            actor.EnterAction = () =>
            {
                actor.Status.SetBase(SkillDefinitions.AtkStat, 10f);
                actor.Status.AddModifier(SkillDefinitions.AtkStat, StatModOp.Add, 5f, source);
                actor.SkillAgent.ApplyEffect(LongEffect, actor);
            };
            TestBehavior.RemoveAction = () => actor.Trace.Add("remove-atk:" + actor.Status.Get(SkillDefinitions.AtkStat));
            actor.ReleasedAction = () => actor.Trace.Add("release-atk:" + actor.Status.Get(SkillDefinitions.AtkStat) + "/" + actor.Status.GetBase(SkillDefinitions.AtkStat));
            actor.Spawn();
            Check(actor.Status.Get(SkillDefinitions.AtkStat) == 15f && events == 2, "Enter could not set the base value or add a modifier");
            ReturnActor(pool, actor);
            Check(TraceOf(actor) == "init,enter,effect-apply:1,enable,disable,effect-remove:1:Released,remove-atk:15,release,release-atk:0/0", "Return did not remove effects before clearing the status or the release hook saw stale values");
            Check(events == 2, "Clearing the status raised change events");
            actor.Trace.Clear();
            actor.EnterAction = () => actor.Status.SetBase(SkillDefinitions.AtkStat, 10f);
            BorrowActor(pool, false);
            actor.Spawn();
            Check(actor.Status.Get(SkillDefinitions.AtkStat) == 10f && events == 3, "Respawn kept a previous modifier or could not set the base value");
            ReturnActor(pool, actor);
        });

        await RunSkillActorCase("a stat effect raises the stat, pool return removes its modifier in the agent release before the status is cleared and a respawn starts clean", () =>
        {
            using var pool = CreateActorPool<StatusActorProbe>();
            var actor = BorrowActor(pool, true);
            actor.Status.OnStatChanged += (type, previous, current) => actor.Trace.Add(type.Name + ":" + previous + ">" + current);
            actor.EnterAction = () =>
            {
                actor.Status.SetBase(SkillDefinitions.AtkStat, 10f);
                actor.SkillAgent.ApplyEffect(SkillDefinitions.GetEffect(AtkEffectId), actor);
            };
            actor.ReleasedAction = () => actor.Trace.Add("release-atk:" + actor.Status.Get(SkillDefinitions.AtkStat) + "/" + actor.Status.GetBase(SkillDefinitions.AtkStat));
            actor.Spawn();
            Check(actor.Status.Get(SkillDefinitions.AtkStat) == 15f, "The stat effect did not raise the stat");
            ReturnActor(pool, actor);
            Check(TraceOf(actor) == "init,enter,Atk:0>10,Atk:10>15,enable,disable,Atk:15>10,release,release-atk:0/0", "Return did not remove the stat effect modifier before clearing the status");
            actor.Trace.Clear();
            actor.EnterAction = () => actor.Status.SetBase(SkillDefinitions.AtkStat, 10f);
            BorrowActor(pool, false);
            actor.Spawn();
            Check(actor.Status.Get(SkillDefinitions.AtkStat) == 10f && TraceOf(actor) == "enter,Atk:0>10,enable", "Respawn kept the stat effect modifier");
            ReturnActor(pool, actor);
        });

        await RunSkillActorCase("a dot lowers the hp set in enter on every update, pool return removes it as released and clears the hp before the release hook and a respawn starts clean", () =>
        {
            var dot = TestEffect(6, 1f, Time.deltaTime, null, 10f, new FlatDamageExecution());
            using var pool = CreateActorPool<StatusActorProbe>();
            var actor = BorrowActor(pool, true);
            actor.Status.OnResourceChanged += (resource, previous, current) => actor.Trace.Add("hp:" + current);
            actor.EnterAction = () =>
            {
                actor.Status.SetBase(SkillDefinitions.MaxHpStat, 100f);
                actor.Status.SetResource(SkillDefinitions.HpStat, 100f);
                actor.SkillAgent.ApplyEffect(dot, actor);
            };
            actor.ReleasedAction = () => actor.Trace.Add("release-hp:" + actor.Status.GetResource(SkillDefinitions.HpStat));
            actor.Spawn();
            ActorMessage(actor, "Update");
            ActorMessage(actor, "Update");
            Check(actor.Status.GetResource(SkillDefinitions.HpStat) == 70f, "The dot did not lower the hp on apply and on every update");
            ReturnActor(pool, actor);
            Check(TraceOf(actor) == "init,enter,hp:100,effect-apply:6,hp:90,effect-execute:6,enable,hp:80,effect-execute:6,hp:70,effect-execute:6,disable,effect-remove:6:Released,release,release-hp:0", "Return did not remove the dot as released or the release hook saw a stale hp");
            actor.Trace.Clear();
            actor.EnterAction = () => actor.Status.SetBase(SkillDefinitions.MaxHpStat, 100f);
            BorrowActor(pool, false);
            actor.Spawn();
            ActorMessage(actor, "Update");
            Check(actor.Status.GetResource(SkillDefinitions.HpStat) == 0f && TraceOf(actor) == "enter,enable", "Respawn kept the dot or the previous hp");
            ReturnActor(pool, actor);
        });

        await RunSkillActorCase("effect remove failure during return propagates unchanged, skips the status release and the release hook and dispose destroys without releasing", () =>
        {
            var pool = CreateActorPool<StatusActorProbe>();
            var actor = BorrowActor(pool, true);
            actor.EnterAction = () =>
            {
                actor.Status.SetBase(SkillDefinitions.AtkStat, 10f);
                actor.Status.AddModifier(SkillDefinitions.AtkStat, StatModOp.Override, 50f, actor);
                actor.SkillAgent.ApplyEffect(LongEffect, actor);
            };
            actor.ReleasedAction = () => actor.Trace.Add("release-atk:" + actor.Status.Get(SkillDefinitions.AtkStat));
            actor.Spawn();
            var failure = new InvalidOperationException("Effect remove failed");
            TestBehavior.RemoveFailure = failure;
            var caught = CaptureSync(() => ReturnActor(pool, actor));
            Check(ReferenceEquals(caught, failure), "Return wrapped or lost the effect remove error");
            Check(TraceOf(actor) == "init,enter,effect-apply:1,enable,disable,effect-remove:1:Released", "Remove failure still ran the release hook");
            Check(actor.Status.Get(SkillDefinitions.AtkStat) == 50f && actor.Status.GetBase(SkillDefinitions.AtkStat) == 10f, "Remove failure still released the status");
            Check(!IsRegistered(actor.Input) && pool.ActiveCount == 1, "Remove failure kept input attached or changed pool tracking");
            ThrowsSync<InvalidOperationException>(() => actor.SkillAgent.ApplyEffect(LongEffect, actor)).GetAwaiter().GetResult();
            TestBehavior.RemoveFailure = null;
            CheckQuarantined(actor);
            CheckReturnRejected(pool, actor);
            DisposeFaultedPool(pool, actor);
            Check(actor.Releases == 0 && actor.Count("effect-remove:1:Released") == 1, "Destruction repeated the effect removal or ran the release hook");
        });

        await RunSkillActorCase("a dot passes its caster's status as the source until the pooled caster is returned and not after the caster respawns", () =>
        {
            var execution = new SourceRecordingExecution();
            using var pool = CreateActorPool<StatusActorProbe>();
            var caster = SpawnFirstActor(pool);
            var target = AttachActor(new GameObject("Target").AddComponent<StatusActorProbe>());
            target.Status.SetBase(SkillDefinitions.MaxHpStat, 100f);
            target.Status.SetResource(SkillDefinitions.HpStat, 100f);
            target.SkillAgent.ApplyEffect(TestEffect(6, 5f, Time.deltaTime / 2f, null, 1f, execution), caster);
            ActorMessage(target, "Update");
            int beforeReturn = execution.Sources.Count;
            Check(beforeReturn > 1 && execution.Sources.TrueForAll(source => ReferenceEquals(source, caster.Status)), "The live caster's status was not the source");
            ReturnActor(pool, caster);
            ActorMessage(target, "Update");
            int afterReturn = execution.Sources.Count;
            BorrowActor(pool, false);
            caster.Spawn();
            ActorMessage(target, "Update");
            Check(afterReturn > beforeReturn && execution.Sources.Count > afterReturn && execution.Sources.FindIndex(beforeReturn, source => source != null) < 0, "A returned or respawned caster's status was still the source");
            ActorMessage(target, "OnDestroy");
            ReturnActor(pool, caster);
        });

        await Run("actor: scene scope pooled actor is released and stops input and updates right after scene stop", async () =>
        {
            var manager = StartScenes(out var host, out _);
            ResetFlowScenes();
            await Finish(manager.TransitionAsync<FlowScene<A>>("A", LoadSceneMode.Single));
            var scene = FlowScene<A>.Last;
            var actor = scene.Actor;
            var scope = scene.ResourceScope;
            Check(Dispatches(actor), "Scene actor did not receive input");
            var transition = manager.TransitionAsync<FlowScene<B>>("B", LoadSceneMode.Single);
            ActorMessage(actor, "Update");
            ActorMessage(actor, "LateUpdate");
            Check(scene.State == SceneState.Stopped && !scope.Token.IsCancellationRequested, "Scene was not stopped before scope disposal");
            Check(actor.Releases == 1 && !IsRegistered(actor.Input) && actor.Updates == 0 && actor.LateUpdates == 0, "Scene pool actor kept running after stop");
            await Finish(transition);
            Check(scope.Token.IsCancellationRequested && actor.Releases == 1, "Scope disposal released the actor again");
            host.Shutdown();
        });

        // 계약: 씬에 배치한 Actor는 씬 정지로 멈추지 않는다. 언로드가 객체를 파괴할 때까지 갱신은 계속되고 입력만 전환 차단으로 막힌다.
        foreach (bool additive in new[] { false, true })
        {
            await Run($"actor: placed actor keeps updating without input from {(additive ? "additive" : "single")} scene stop until unload destroys it", async () =>
            {
                var manager = StartScenes(out var host, out _);
                ResetFlowScenes();
                await Finish(manager.TransitionAsync<FlowScene<A>>("A", LoadSceneMode.Single));
                if (additive)
                    await Finish(manager.TransitionAsync<FlowScene<B>>("B", LoadSceneMode.Additive));

                SceneBase scene = additive ? FlowScene<B>.Last : FlowScene<A>.Last;
                var scope = scene.ResourceScope;
                var placed = AttachActor(CreateInputActor());
                Check(Dispatches(placed), "Placed actor did not receive input");
                var unload = new FakeOperation();
                Addressables.UnloadFactory = () => unload;
                var closing = additive ? manager.UnloadAdditiveAsync<FlowScene<B>>() : manager.TransitionAsync<FlowScene<C>>("C", LoadSceneMode.Single);
                FakeFrames.Pump();
                FakeFrames.Pump();
                Check(!closing.Status.IsCompleted() && unload.Completed != null && scene.State == SceneState.Stopped && !scope.Token.IsCancellationRequested, "Close did not wait on the unload after stopping the scene");
                ActorMessage(placed, "Update");
                ActorMessage(placed, "LateUpdate");
                Check(TraceOf(placed) == "init,enter,enable" && placed.Updates == 1 && placed.LateUpdates == 1, "Placed actor stopped updating before unload");
                Check(IsRegistered(placed.Input) && !Dispatches(placed), "Placed actor received input during the transition");
                // 언로드가 씬 객체를 파괴할 때 Unity가 보내는 메시지다.
                ActorMessage(placed, "OnDisable");
                ActorMessage(placed, "OnDestroy");
                Check(TraceOf(placed) == "init,enter,enable,disable,release" && !IsRegistered(placed.Input), "Unload destruction did not disable and release the placed actor once");
                unload.Complete();
                await Finish(closing);
                Check(scope.Token.IsCancellationRequested && placed.Releases == 1, "Scope disposal released the placed actor again");
                host.Shutdown();
            });
        }

        await Run("actor: release borrowing from its closing scene scope fails at the close boundary and leaves nothing active", async () =>
        {
            var manager = StartScenes(out var host, out _);
            ResetFlowScenes();
            await Finish(manager.TransitionAsync<FlowScene<A>>("A", LoadSceneMode.Single));
            var scene = FlowScene<A>.Last;
            var actor = scene.Actor;
            var scope = scene.ResourceScope;
            var effect = new GameObject("Effect");
            int pools = PoolCount(Global.Pool, "gameObjectPools");
            actor.ReleasedAction = () => Global.Pool.Get(effect, null, scope);
            var error = await Capture(() => Finish(manager.TransitionAsync<FlowScene<B>>("B", LoadSceneMode.Single)));
            var errors = error is AggregateException aggregate ? new List<Exception>(aggregate.Flatten().InnerExceptions) : new List<Exception> { error };
            Check(errors.Count == 1 && errors[0] is InvalidOperationException, "Closed scope lease did not reach the close boundary");
            Check(actor.Releases == 1 && !IsRegistered(actor.Input) && !actor.gameObject.Active && UnityEngine.Object.Destroyed.Contains(actor.gameObject), "Scene actor stayed active");
            Check(PoolCount(Global.Pool, "gameObjectPools") == pools - 1 && PoolCount(Global.Pool, "closedScopes") == 1, "Rejected lease left a pool or the scope was not closed");
            actor.ReleasedAction = null;
            await Finish(manager.TransitionAsync<FlowScene<B>>("B", LoadSceneMode.Single));
            Check(scope.Token.IsCancellationRequested && PoolCount(Global.Pool, "closedScopes") == 0, "Closed scene scope was not released after the retry");
            host.Shutdown();
            Check(host.ShutdownErrors.Count == 0, "Shutdown failed after a rejected scene lease");
        });

        await Run("pool: object return callback failure propagates unchanged and keeps the object borrowed", () =>
        {
            var prefab = new GameObject("Prefab");
            prefab.AddComponent<FailingReturnProbe>();
            prefab.AddComponent<PoolComponent>();
            using var pool = new GameObjectPool(prefab, 0);
            var obj = pool.Get(null);
            var failing = obj.GetComponent<FailingReturnProbe>();
            var later = obj.GetComponent<PoolComponent>();
            var failure = new InvalidOperationException("Return failed");
            failing.Failure = failure;
            Exception caught = null;
            try
            {
                pool.Return(obj);
            }
            catch (Exception error)
            {
                caught = error;
            }
            Check(ReferenceEquals(caught, failure), "Object return wrapped or lost the callback error");
            Check(failing.Returns == 1 && later.Returns == 0, "Later callback ran after the failure");
            Check(obj.Active && obj.transform.parent != pool.Root && pool.ActiveCount == 1 && pool.InactiveCount == 0, "Failed return deactivated the object or changed tracking");
            failing.Failure = null;
            pool.Dispose();
            Check(failing.Returns == 2 && later.Returns == 1 && UnityEngine.Object.Destroyed.Contains(obj) && pool.ActiveCount == 0, "Dispose skipped callbacks or destruction");
            return UniTask.CompletedTask;
        });

        await Run("actor: activation works once Input is ready while later managers are still initializing", async () =>
        {
            InputSystem.actions = new InputActionAsset();
            var host = new ManagerHost();
            BindGlobal(host);
            host.Register(new InputManager());
            var gate = new UniTaskCompletionSource();
            host.Register(new Probe<A>(new(), "pending") { Gate = gate.Task });
            var initialization = host.InitializeAsync();
            Check(host.State == ManagerState.Initializing && Global.Input.State == ManagerState.Ready, "Input was not ready synchronously");
            var actor = AttachActor(CreateInputActor());
            Check(IsRegistered(actor.Input) && Dispatches(actor), "Early actor did not register or scene input was not allowed by default");
            gate.TrySetResult();
            await initialization;
            ActorMessage(actor, "OnDestroy");
            host.Shutdown();
        });

        await Run("actor: nested transition cannot unlock input with an older notification", async () =>
        {
            var manager = StartScenes(out var host, out _);
            ResetFlowScenes();
            await Finish(manager.TransitionAsync<FlowScene<A>>("A", LoadSceneMode.Single));
            var primary = FlowScene<A>.Last;
            var gate = new UniTaskCompletionSource();
            UniTask nested = UniTask.CompletedTask;
            bool started = false;
            Global.UI.InputAllowedAction = allowed =>
            {
                if (started || !allowed)
                    return;
                started = true;
                SceneTransitionView.ShowGate = gate;
                nested = manager.TransitionAsync<ActorSceneProbe>("Actor", LoadSceneMode.Additive);
            };
            await Finish(manager.TransitionAsync<FlowScene<B>>("B", LoadSceneMode.Additive));
            var additive = FlowScene<B>.Last;
            bool keptLocked = manager.IsTransitioning && !Dispatches(primary.Actor) && !Dispatches(additive.Actor);
            gate.TrySetResult();
            await Finish(nested);
            bool reopened = !manager.IsTransitioning && Dispatches(primary.Actor) && Dispatches(additive.Actor);
            host.Shutdown();
            Check(started && keptLocked && reopened, "Older input unlock reopened input during a nested transition");
        });
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
