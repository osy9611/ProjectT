using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Controller;
using UnityEngine;
using UnityEngine.InputSystem;

static partial class Program
{
    static async UniTask RunActorReturnTests()
    {
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
            Check(pool.ActiveCount == 1 && actor.gameObject.activeSelf, "Failed return changed pool tracking or deactivated the object");
            CheckQuarantined(actor);
            CheckReturnRejected(pool, actor);
            DisposeFaultedPool(pool, actor);
            Check(actor.Releases == 0 && laterResets == 0, "Destruction ran the release hook or reran input resets");
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

        await RunActorCase("a return after destroying a faulted actor does nothing", () =>
        {
            var actor = AttachActor(CreateInputActor());
            actor.Register(CreateHeldContext(() => throw new InvalidOperationException("Reset failed")));
            ProjectFire.Trigger(InputActionPhase.Started);
            actor.enabled = false;
            CaptureSync(() => ActorMessage(actor, "OnDisable"));
            ActorMessage(actor, "OnDestroy");
            ((ProjectT.Pool.IPoolable)actor).OnReturn();
            Check(TraceOf(actor) == "init,enter,enable,release", "Return after destruction cleaned up the faulted actor again");
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
            Check(!IsRegistered(actor.Input) && pool.ActiveCount == 1, "Disable failure kept input attached or changed pool tracking");
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
            Check(!IsRegistered(actor.Input) && !Dispatches(actor), "Hook failure left input attached or dropped the registration");
            Check(pool.ActiveCount == 1 && pool.InactiveCount == 0 && actor.gameObject.activeSelf && actor.transform.parent != pool.Root, "Failed return changed pool tracking or deactivated the object");
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
            Check(!actor.gameObject.activeSelf && !IsRegistered(actor.Input), "Return kept the object active, kept input attached or dropped the registration");
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
            Check(actor.gameObject.activeSelf && actor.transform.parent != pool.Root && actor.Count("enable") == 2, "Rejected return deactivated the object");
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
            Check(TraceOf(actor) == "init,enter,enable,disable" && pool.ActiveCount == 1 && actor.gameObject.activeSelf, "Rejected return ended the spawn, skipped the disable hook or changed pool tracking");
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
            Check(TraceOf(actor) == "init,enter,enable,disable" && pool.ActiveCount == 1 && actor.gameObject.activeSelf, "Rejected return ended the spawn or changed pool tracking");
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
                Check(actor.Releases == 1 && !IsRegistered(actor.Input), "Destruction after pool cleanup released again or kept input");
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
                Check(ReferenceEquals(actor.Input.Owner, Global.Input.Controller), "Spawn duplicated or skipped the input registration");
                int before = actor.Fires;
                ProjectFire.Trigger(InputActionPhase.Performed);
                Check(actor.Fires == before + 1, "Spawn duplicated or skipped input dispatch");
                ReturnActor(pool, actor);
                Check(!IsRegistered(actor.Input), "Return dropped or kept the controller registration");
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
            Check(IsRegistered(actor.Input) && Dispatches(actor), "Initialization retry duplicated or lost the input context");
            ActorMessage(actor, "OnDestroy");
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
                Check(IsRegistered(actor.Input) && IsRegistered(spawnContext), "Per-spawn context was not activated");
                int before = spawnFires;
                ProjectFire.Trigger(InputActionPhase.Performed);
                Check(spawnFires == before + 1, "Previous per-spawn contexts still received input");
                var released = spawnContext;
                ReturnActor(pool, actor);
                Check(!IsRegistered(released), "Release hook did not remove the per-spawn context");
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
    }
}
