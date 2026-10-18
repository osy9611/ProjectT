using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Scene;
using ProjectT.Addressable;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

static partial class Program
{
    sealed class FlowScene<T> : SceneBase
    {
        public static FlowScene<T> Last;
        public static bool FailReady, FailInitialize;
        public readonly List<string> Trace = new();
        public ActorProbe Actor;
        public int Notifications;
        public int Updates;

        protected override void OnInitialize()
        {
            Last = this;
            Trace.Add("initialize");
            Global.Notify.Subscribe((NotificationId)1, OnNotification, LifetimeToken);
            Actor = BorrowSceneActor(this);
            if (FailInitialize) throw new InvalidOperationException("Initialize");
        }

        private void OnNotification(object[] args)
        {
            Notifications++;
        }

        protected override UniTask OnEnter(CancellationToken token, params object[] data)
        {
            Check(Trace.Count == 1 && !Dispatches(Actor), "Initialize order/input invalid");
            Trace.Add("enter");
            return UniTask.CompletedTask;
        }

        protected override void OnActivate()
        {
            Check(!Global.Scene.IsInputAllowed && !Dispatches(Actor), "Input resumed before ready hook");
            Trace.Add("ready");
            if (FailReady) throw new InvalidOperationException("Ready");
        }

        protected override void OnFinalize() => Trace.Add("finalize");

        protected internal override void OnUpdate(float dt) => Updates++;

        public static void Reset()
        {
            Last = null;
            FailReady = FailInitialize = false;
        }
    }

    static void ResetFlowScenes()
    {
        FlowScene<A>.Reset();
        FlowScene<B>.Reset();
    }

    public sealed class CustomResource : Resource<UnityEngine.Object>
    {
        public static int Created, Released;
        protected override void OnInitialize() => Created++;
        protected override void OnRelease() => Released++;
    }

    public sealed class ThrowingResource : Resource<UnityEngine.Object>
    {
        protected override void OnRelease() => throw new InvalidOperationException("Release");
    }

    public sealed class FailingResource : Resource<UnityEngine.Object>
    {
        public static int Released;

        protected override void OnInitialize()
        {
            throw new InvalidOperationException("Initialize");
        }

        protected override void OnRelease()
        {
            Released++;
        }
    }

    static async UniTask RunSceneContractTests()
    {
        await Run("resources: derived resource is shared by raw and typed loads until last scope", () =>
        {
            var resource = StartResources(out var host);
            CustomResource.Created = CustomResource.Released = 0;
            var scope = resource.CreateScope();
            resource.LoadResource<CustomResource>("asset", scope: scope);
            Check(CustomResource.Created == 1, "Custom resource was not initialized by ordinary load");
            var extension = resource.LoadResource<CustomResource>("asset", scope: scope);
            Check(ReferenceEquals(extension, resource.LoadResource<CustomResource>("asset", scope: resource.AppScope)) && CustomResource.Created == 1, "Extension not shared");
            scope.Dispose();
            Check(CustomResource.Released == 0, "Shared extension released early");
            host.Shutdown();
            Check(CustomResource.Released == 1, "Extension release missing");
            return UniTask.CompletedTask;
        });

        await Run("resources: release exception does not skip other resources or native handle", async () =>
        {
            var resource = StartResources(out var host);
            CustomResource.Released = 0;
            var scope = resource.CreateScope();
            resource.LoadResource<ThrowingResource>("asset", scope: scope);
            Addressables.Factory = () => new UnityEngine.ResourceManagement.AsyncOperations.FakeOperation { IsDone = true, Result = new UnityEngine.GameObject("Other") };
            resource.LoadAndGet<UnityEngine.GameObject>("other", scope: scope);
            await ThrowsSync<AggregateException>(() => scope.Dispose());
            Check(Addressables.Operations[1].Releases == 1 && Addressables.Operations[0].Releases == 1, "Extension exception skipped cleanup");
            host.Shutdown();
        });

        await Run("resources: derived initialization failure releases and permits replacement", async () =>
        {
            var resource = StartResources(out var host);
            FailingResource.Released = 0;
            await ThrowsSync<InvalidOperationException>(() => resource.LoadResource<FailingResource>("asset"));
            Check(FailingResource.Released == 1 && Addressables.Operations[0].Releases == 1, "Failed initialization leaked");
            Addressables.Factory = () => new UnityEngine.ResourceManagement.AsyncOperations.FakeOperation { IsDone = true, Result = new UnityEngine.Object() };
            var loaded = await resource.LoadResourceAsync<CustomResource>("asset");
            Check(ReferenceEquals(loaded.Asset, resource.LoadAndGet<UnityEngine.Object>("asset")), "Async resource differs from raw asset");
            await ThrowsSync<InvalidOperationException>(() => resource.LoadResource<FailingResource>("asset"));
            host.Shutdown();
        });

        await Run("resources: generic first load rejects specialized request without extra ownership", async () =>
        {
            var resource = StartResources(out var host);
            var scope = resource.CreateScope();
            resource.LoadAndGet<UnityEngine.Object>("asset", scope: scope);
            await ThrowsSync<InvalidOperationException>(() => resource.LoadResource<CustomResource>("asset", scope: resource.AppScope));
            Check(Addressables.Operations.Count == 1, "Invalid resource request loaded asset");
            scope.Dispose();
            Check(Addressables.Operations[0].Releases == 1, "Rejected request retained ownership");
            host.Shutdown();
        });

        await Run("resources: specialized pending load shares raw waiter and survives individual cancellation", async () =>
        {
            var resource = StartResources(out var host);
            CustomResource.Created = CustomResource.Released = 0;
            using var cancel = new System.Threading.CancellationTokenSource();
            var typed = resource.LoadResourceAsync<CustomResource>("asset", cancelToken: cancel.Token);
            var raw = resource.LoadAndGetAsync<UnityEngine.Object>("asset", scope: resource.AppScope);
            cancel.Cancel();
            await Throws<OperationCanceledException>(async () => { await typed; });
            Check(Addressables.Operations.Count == 1, "Duplicate pending handle");
            Addressables.Operations[0].Complete();
            var asset = await raw;
            Check(ReferenceEquals(asset, resource.LoadResource<CustomResource>("asset").Asset) && CustomResource.Created == 1, "Pending initialization not shared");
            host.Shutdown();
            Check(CustomResource.Released == 1, "Derived resource not released");
        });

        await Run("resources: specialized atlas preload shares sprite and raw access", async () =>
        {
            var resource = StartResources(out var host);
            var atlas = new UnityEngine.U2D.SpriteAtlas();
            Addressables.Factory = () => new UnityEngine.ResourceManagement.AsyncOperations.FakeOperation { Result = atlas };
            var preload = resource.PreloadResourceAsync<SpriteAtlasResource>("atlas");
            var loading = resource.GetSpriteAsync("atlas", "icon");
            Check(Addressables.Operations.Count == 1, "Atlas preload duplicated handle");
            Addressables.Operations[0].Complete();
            await preload;
            Check(await loading != null && ReferenceEquals(atlas, resource.LoadAndGet<UnityEngine.U2D.SpriteAtlas>("atlas")), "Atlas preload not reused");
            host.Shutdown();
        });

        await Run("resources: same asset type supports separate resource classes on different paths", () =>
        {
            var resource = StartResources(out var host);
            var first = resource.LoadResource<CustomResource>("first");
            var second = resource.LoadResource<Resource<UnityEngine.Object>>("second");
            Check(Addressables.Operations.Count == 2 && ReferenceEquals(Addressables.Operations[0].Result, first.Asset) && ReferenceEquals(Addressables.Operations[1].Result, second.Asset), "Resource classes over one asset type did not load separately");
            host.Shutdown();
            return UniTask.CompletedTask;
        });

        await Run("contract: initialize failure skips enter and cleans partially created content", async () =>
        {
            var manager = StartScenes(out var host, out _);
            ResetFlowScenes();
            FlowScene<A>.FailInitialize = true;
            await Throws<InvalidOperationException>(() => Finish(manager.TransitionAsync<FlowScene<A>>("A", LoadSceneMode.Single)));
            var scene = FlowScene<A>.Last;
            Global.Notify.Publish((NotificationId)1);
            Check(!scene.Trace.Contains("enter") && scene.Notifications == 0 && !Dispatches(scene.Actor), "Partial initialization leaked");
            Check(!manager.IsTransitioning && Global.UI.InputAllowed, "Initialization failure retained input lock");
            host.Shutdown();
        });

        await Run("contract: failed presentation does not stop primary and releases input lock", async () =>
        {
            var manager = StartScenes(out var host, out _);
            ResetFlowScenes();
            await Finish(manager.TransitionAsync<FlowScene<A>>("A", LoadSceneMode.Single));
            SceneTransitionView.ShowAction = () => throw new InvalidOperationException("Show");
            await Throws<InvalidOperationException>(() => Finish(manager.TransitionAsync<FlowScene<B>>("B", LoadSceneMode.Single)));
            Check(manager.CurrentScene == FlowScene<A>.Last && Dispatches(FlowScene<A>.Last.Actor) && Global.UI.InputAllowed, "Show failure stopped primary or locked input");
            SceneTransitionView.ShowAction = null;
            SceneTransitionView.HideAction = () => throw new InvalidOperationException("Hide");
            await Throws<InvalidOperationException>(() => Finish(manager.TransitionAsync<FlowScene<B>>("B", LoadSceneMode.Additive)));
            Global.Notify.Publish((NotificationId)1);
            Check(Dispatches(FlowScene<A>.Last.Actor) && FlowScene<A>.Last.Notifications == 1 && FlowScene<B>.Last.Notifications == 0 && !manager.IsTransitioning, "Hide failure leaked additive or locked primary");
            host.Shutdown();
        });

        await Run("contract: show, initialize, prepare, hide, ready, input and completion are ordered", async () =>
        {
            var manager = StartScenes(out var host, out _);
            ResetFlowScenes();
            SceneTransitionView.ShowGate = new UniTaskCompletionSource();
            SceneTransitionView.HideGate = new UniTaskCompletionSource();
            int calls = 0;
            manager.Transition<FlowScene<A>>("A", LoadSceneMode.Single, _ => calls++);
            Check(!Global.UI.InputAllowed && Addressables.SceneOperations.Count == 0, "Show not awaited");
            SceneTransitionView.ShowGate.TrySetResult();
            FakeFrames.Pump();
            var scene = FlowScene<A>.Last;
            Check(scene.State == SceneState.Prepared && scene.Trace.Count == 2 && calls == 0 && !Dispatches(scene.Actor), "Hide not awaited");
            host.Update(1f);
            Check(scene.Updates == 0, "Update during presentation");
            SceneTransitionView.HideGate.TrySetResult();
            Check(scene.State == SceneState.Active && Dispatches(scene.Actor) && Global.UI.InputAllowed && calls == 1, "Activation did not finish");
            Check(string.Join(",", scene.Trace) == "initialize,enter,ready", "Order changed");
            host.Shutdown();
            await UniTask.CompletedTask;
        });

        await Run("contract: additive cleanup retains primary handlers and input", async () =>
        {
            var manager = StartScenes(out var host, out _);
            ResetFlowScenes();
            await Finish(manager.TransitionAsync<FlowScene<A>>("A", LoadSceneMode.Single));
            await Finish(manager.TransitionAsync<FlowScene<B>>("B", LoadSceneMode.Additive));
            var main = FlowScene<A>.Last;
            var child = FlowScene<B>.Last;
            await Finish(manager.UnloadAdditiveAsync<FlowScene<B>>());
            Global.Notify.Publish((NotificationId)1);
            Check(Dispatches(main.Actor) && main.Notifications == 1 && manager.CurrentScene == main, "Primary content stopped");
            Check(!Dispatches(child.Actor) && child.Notifications == 0 && child.State == SceneState.Stopped, "Additive content survived");
            host.Shutdown();
        });

        await Run("contract: ready failure cleans additive and restores input for primary", async () =>
        {
            var manager = StartScenes(out var host, out _);
            ResetFlowScenes();
            await Finish(manager.TransitionAsync<FlowScene<A>>("A", LoadSceneMode.Single));
            FlowScene<B>.FailReady = true;
            await Throws<InvalidOperationException>(() => Finish(manager.TransitionAsync<FlowScene<B>>("B", LoadSceneMode.Additive)));
            Check(!manager.IsTransitioning && Dispatches(FlowScene<A>.Last.Actor) && Global.UI.InputAllowed, "Failure retained input lock");
            Global.Notify.Publish((NotificationId)1);
            Check(FlowScene<A>.Last.Notifications == 1 && FlowScene<B>.Last.Notifications == 0 && manager.GetScene<FlowScene<B>>() == null, "Failed content leaked");
            host.Shutdown();
        });

        await Run("contract: shutdown during presentation cannot activate after late hide", async () =>
        {
            var manager = StartScenes(out var host, out _);
            ResetFlowScenes();
            SceneTransitionView.HideGate = new UniTaskCompletionSource();
            var pending = manager.TransitionAsync<FlowScene<A>>("A", LoadSceneMode.Single);
            FakeFrames.Pump();
            var scene = FlowScene<A>.Last;
            host.Shutdown();
            await Throws<OperationCanceledException>(() => Finish(pending));
            SceneTransitionView.HideGate.TrySetResult();
            Check(scene.State == SceneState.Stopped && !scene.Trace.Contains("ready") && !Dispatches(scene.Actor), "Late presentation revived scene");
        });
    }
}
