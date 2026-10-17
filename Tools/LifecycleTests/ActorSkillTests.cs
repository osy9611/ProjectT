using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ProjectT;
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
    static async UniTask RunActorSkillTests()
    {
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
            Check(TraceOf(actor) == "init,enter,enable,skill-activate,skill-cancel,disable" && pool.ActiveCount == 1 && actor.gameObject.activeSelf, "Rejected return ended the spawn, skipped the disable hook or changed pool tracking");
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
            Check(actor.Releases == 1 && !IsRegistered(actor.Input) && !actor.gameObject.activeSelf && UnityEngine.Object.Destroyed.Contains(actor.gameObject), "Scene actor stayed active");
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
            Check(obj.activeSelf && obj.transform.parent != pool.Root && pool.ActiveCount == 1 && pool.InactiveCount == 0, "Failed return deactivated the object or changed tracking");
            failing.Failure = null;
            pool.Dispose();
            Check(failing.Returns == 2 && later.Returns == 1 && UnityEngine.Object.Destroyed.Contains(obj) && pool.ActiveCount == 0, "Dispose skipped callbacks or destruction");
            return UniTask.CompletedTask;
        });

        await Run("actor: activation works once Input is ready while later managers are still initializing", async () =>
        {
            InputSystem.actions = new InputActionAsset();
            var host = new ManagerHost();
            Global.BindForTests(host);
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
