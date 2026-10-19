using System;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Controller;
using UnityEngine;
using UnityEngine.InputSystem;

static partial class Program
{
    static async UniTask RunActorSpawnTests()
    {
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
            Check(actor.Releases == 0, "Destruction released a spawn that never entered");
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
            Check(actor.Trace.Count == 0 && actor.PivotAgent == null, "Inactive spawn ran OnInit or OnEnter before Awake");
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
            Check(!IsRegistered(actor.Input) && TraceOf(actor) == "init", "Omitting base.OnRelease bypassed final cleanup");
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
            Check(!IsRegistered(actor.Input) && actor.Releases == 0 && actor.Count("enter") == 1, "Destruction callback spawned, ran the release hook or retained input");
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
            Check(Dispatches(first) && IsRegistered(second.Input), "Rejected sharing changed either registration");
            var context = first.Input;
            first.Unregister(context);
            Check(!IsRegistered(context), "Unregister kept the actor registration");
            second.Register(context);
            Check(IsRegistered(context), "Unregistered context could not be registered again");
            var shared = new InputContext("shared", 0).Bind("Player/Fire", _ => { }, eInputEvent.Performed, consume: false);
            first.Register(shared);
            ThrowsSync<InvalidOperationException>(() => second.Register(shared)).GetAwaiter().GetResult();
            ActorMessage(first, "OnDestroy");
            second.Register(shared);
            Check(IsRegistered(shared), "Context of a destroyed actor could not be registered again");
            ActorMessage(second, "OnDestroy");
        });
    }
}
