using System;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Controller;
using UnityEngine;
using UnityEngine.InputSystem;

static partial class Program
{
    static async UniTask RunActorInputTests()
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
            Check(!Holds(actor, actor.Input) && !Holds(actor, extra) && RegisteredContextCount(actor) == 0 && actor.Releases == 1, "Destruction kept context ownership or repeated the release hook");
            ThrowsSync<ObjectDisposedException>(() => actor.Register(new InputContext("late", 0))).GetAwaiter().GetResult();
            ThrowsSync<ObjectDisposedException>(() => actor.Spawn()).GetAwaiter().GetResult();
            other.Register(extra);
            Check(Holds(other, extra) && IsRegistered(extra), "Finally detached context could not be registered again");
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
            Check(!IsRegistered(invalid) && !Holds(actor, invalid) && IsRegistered(actor.Input) && IsRegistered(deferred), "Rejected or deferred registration state is wrong");
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
            Check(!Holds(actor, actor.Input) && !Holds(actor, later) && RegisteredContextCount(actor) == 0, "Destroyed actor kept context registrations");
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
            Check(!IsRegistered(actor.Input) && !IsRegistered(second) && !Holds(actor, actor.Input) && !Holds(actor, second), "Destroyed actor kept context registrations");
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
            Check(!Holds(actor, actor.Input) && !Holds(actor, later), "Destroyed actor kept context registrations");
        });
    }
}
