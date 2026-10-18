using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Scene;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using Scenes = ProjectT.SceneManager;

static partial class Program
{
    sealed class SceneProbe<T> : SceneBase
    {
        public static UniTaskCompletionSource Gate;
        public static bool FailEnter, FailStop;
        public static SceneProbe<T> Last;
        public static Action UpdateAction;
        public int Initialized, Finalized, Updated;

        protected override async UniTask OnEnter(CancellationToken token, params object[] data)
        {
            Last = this;
            Global.Resource.LoadAndGet<UnityEngine.Object>(typeof(T).Name, scope: ResourceScope);
            if (Gate != null)
                await Gate.Task;
            if (FailEnter)
                throw new InvalidOperationException("Enter failed");
        }

        protected override void OnInitialize() => Initialized++;

        protected override void OnFinalize()
        {
            Finalized++;
            if (FailStop)
                throw new InvalidOperationException("Stop failed");
        }

        protected internal override void OnUpdate(float dt)
        {
            Updated++;
            UpdateAction?.Invoke();
        }

        public static void Reset()
        {
            Gate = null;
            FailEnter = FailStop = false;
            Last = null;
            UpdateAction = null;
        }
    }

    public sealed class ScenePoolItem : MonoBehaviour, ProjectT.Pool.IPoolable
    {
        public static bool FailReturn;
        public static Action<ScenePoolItem> Returning;
        public int Returns;

        public void OnGet()
        {
        }

        public void OnReturn()
        {
            Returns++;
            Returning?.Invoke(this);
            if (FailReturn)
                throw new InvalidOperationException("Return failed");
        }
    }

    sealed class PoolScene<T> : SceneBase
    {
        public static GameObject Prefab;
        public static bool FailEnter, FailStop;
        public static PoolScene<T> Last;
        public ScenePoolItem Item;

        protected override UniTask OnEnter(CancellationToken token, params object[] data)
        {
            if (FailEnter)
                throw new InvalidOperationException("Enter failed");
            return UniTask.CompletedTask;
        }

        protected override void OnInitialize()
        {
            Last = this;
            Item = Global.Pool.Get(Prefab, null, ResourceScope).GetComponent<ScenePoolItem>();
        }

        protected override void OnFinalize()
        {
            if (FailStop)
                throw new InvalidOperationException("Stop failed");
        }

        public static void Reset()
        {
            Prefab = new GameObject("ScenePoolItem");
            Prefab.AddComponent<ScenePoolItem>();
            FailEnter = FailStop = false;
            Last = null;
        }
    }

    static Scenes StartPoolScenes(out ManagerHost host)
    {
        var manager = StartScenes(out host, out _);
        PoolScene<A>.Reset();
        PoolScene<B>.Reset();
        ScenePoolItem.FailReturn = false;
        ScenePoolItem.Returning = null;
        return manager;
    }

    static bool IsReturnedAtStop<T>(PoolScene<T> scene, int returns = 1)
    {
        var item = scene.Item;
        return scene.State == SceneState.Stopped && item.Returns == returns && !item.gameObject.activeSelf && UnityEngine.Object.Destroyed.Contains(item.gameObject);
    }

    static bool HasStopAndReturnErrors(Exception error)
    {
        var errors = error is AggregateException aggregate ? new List<Exception>(aggregate.Flatten().InnerExceptions) : new List<Exception> { error };
        return errors.Exists(e => e.Message == "Stop failed") && errors.Exists(e => e.Message == "Return failed");
    }

    static Scenes StartScenes(out ManagerHost host, out ResourceManager resource)
    {
        Addressables.Reset();
        SceneTransitionView.ShowGate = SceneTransitionView.HideGate = null;
        SceneTransitionView.ShowAction = SceneTransitionView.HideAction = null;
        SceneProbe<A>.Reset();
        SceneProbe<B>.Reset();
        SceneProbe<C>.Reset();
        UnityEngine.InputSystem.InputSystem.actions = new UnityEngine.InputSystem.InputActionAsset();
        host = new ManagerHost(); Global.BindForTests(host);
        resource = host.Register(new ResourceManager());
        host.Register(new InputManager());
        host.Register(new PoolManager());
        host.Register(new DataManager());
        host.Register(new NotificationManager());
        var manager = host.Register(new Scenes());
        host.Register(new UIManager());
        host.Register(new SoundManager());
        host.InitializeAsync().GetAwaiter().GetResult();
        return manager;
    }

    static async UniTask Finish(UniTask task)
    {
        for (int i = 0; i < 20 && !task.Status.IsCompleted(); ++i)
            FakeFrames.Pump();
        Check(task.Status.IsCompleted(), "Scene task did not settle");
        await task;
    }

    static async UniTask RunSceneTests()
    {
        await Run("scenes: bootstrap binding retains local assets until first replacement", async () =>
        {
            var manager = StartScenes(out var host, out var resource);
            var local = resource.LoadAndGet<UnityEngine.Object>("title");
            await Finish(manager.TransitionAsync<SceneProbe<A>>("", LoadSceneMode.Single));
            Check(Addressables.SceneOperations.Count == 0 && UnityEngine.Resources.Sweeps == 0, "Bootstrap loaded remote scene or swept assets");
            Check(ReferenceEquals(local, resource.LoadAndGet<UnityEngine.Object>("title")), "Bootstrap ownership lost");
            await Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Single));
            Check(UnityEngine.Resources.Sweeps == 1, "Replacement did not sweep");
            host.Shutdown();
        });

        await Run("scenes: cleanup failure blocks next load and can retry", async () =>
        {
            var manager = StartScenes(out var host, out _);
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            UnityEngine.Resources.Sweep = () => throw new InvalidOperationException("Sweep failed");
            await Throws<InvalidOperationException>(() => Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Single)));
            Check(Addressables.SceneOperations.Count == 1 && manager.CurrentScene == null && !manager.IsTransitioning, "Cleanup failure loaded next scene");
            UnityEngine.Resources.Sweep = () => new UnityEngine.AsyncOperation { isDone = true };
            await Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Single));
            Check(manager.CurrentScene is SceneProbe<B>, "Cleanup retry failed");
            host.Shutdown();
        });

        await Run("scenes: single unload failure preserves ownership and retries once", async () =>
        {
            var manager = StartScenes(out var host, out _);
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            int sweeps = UnityEngine.Resources.Sweeps;
            Addressables.UnloadFactory = () => new FakeOperation { IsDone = true, Error = new InvalidOperationException("Unload failed") };
            await Throws<InvalidOperationException>(() => Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Single)));
            Check(Addressables.Operations[0].Releases == 0 && UnityEngine.Resources.Sweeps == sweeps, "Failed unload returned live ownership");
            Addressables.UnloadFactory = null;
            await Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Single));
            Check(Addressables.Operations[0].Releases == 1 && SceneProbe<A>.Last.Finalized == 1, "Unload retry duplicated cleanup");
            host.Shutdown();
        });

        await ReporterRun("scenes: unload failure during shutdown is reported once and releases its handle", async () =>
        {
            var sink = new ListSink();
            ResetReporter(sink);
            var manager = StartScenes(out var host, out _);
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            Addressables.UnloadFactory = () => new FakeOperation { IsDone = true, Error = new InvalidOperationException("Shutdown unload failed") };
            host.Shutdown();
            Addressables.UnloadFactory = null;
            ErrorReporter.Flush();
            Check(sink.Events.Count == 1 && sink.Events[0].Exception?.Message == "Shutdown unload failed", "Shutdown unload failure reported " + sink.Events.Count + " times");
            Check(Addressables.UnloadOperations.Count == 1 && Addressables.UnloadOperations[0].Releases == 1, "Shutdown unload handle leaked or doubled");
        });

        await Run("scenes: scene resources released and sweep completed before next load", async () =>
        {
            var manager = StartScenes(out var host, out var resource);
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            var old = resource.LoadAndGet<UnityEngine.Object>("local");
            var common = resource.LoadAndGet<UnityEngine.Object>("common", scope: resource.AppScope);
            var sweep = new UnityEngine.AsyncOperation();
            UnityEngine.Resources.Sweep = () =>
            {
                Check(Addressables.Operations[0].Releases == 1 && Addressables.Operations[1].Releases == 1, "Old asset retained at sweep");
                Check(!((UnityEngine.ResourceManagement.ResourceProviders.SceneInstance)Addressables.SceneOperations[0].Result).Scene.isLoaded, "Old scene retained at sweep");
                return sweep;
            };
            var transition = manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Single);
            FakeFrames.Pump();
            Check(Addressables.SceneOperations.Count == 1 && manager.IsTransitioning, "Next scene loaded before sweep completed");
            Check(ReferenceEquals(common, resource.LoadAndGet<UnityEngine.Object>("common", scope: resource.AppScope)), "Common asset lost");
            Check(!ReferenceEquals(old, resource.LoadAndGet<UnityEngine.Object>("local")), "Old scene cache retained");
            sweep.Complete();
            await Finish(transition);
            host.Shutdown();
        });

        await Run("scenes: completion is owned by manager and waits for initialization", () =>
        {
            var manager = StartScenes(out var host, out _);
            SceneProbe<A>.Gate = new UniTaskCompletionSource();
            int calls = 0;
            manager.Transition<SceneProbe<A>>("A", LoadSceneMode.Single, result =>
            {
                Check(result == eSceneTransitionResult.Success && manager.CurrentScene != null, "Premature completion");
                calls++;
            });
            FakeFrames.Pump();
            Check(calls == 0 && manager.CurrentScene == null, "Initialization was not awaited");
            SceneProbe<A>.Gate.TrySetResult();
            Check(calls == 1 && !manager.IsTransitioning, "Completion missing or gate stuck");
            host.Shutdown();
            return UniTask.CompletedTask;
        });

        await Run("scenes: completion reports shutdown cancellation as Canceled", () =>
        {
            var manager = StartScenes(out var host, out _);
            SceneProbe<A>.Gate = new UniTaskCompletionSource();
            var results = new List<eSceneTransitionResult>();
            manager.Transition<SceneProbe<A>>("A", LoadSceneMode.Single, results.Add);
            FakeFrames.Pump();
            host.Shutdown();
            for (int i = 0; i < 20 && results.Count == 0; ++i)
                FakeFrames.Pump();
            Check(results.Count == 1 && results[0] == eSceneTransitionResult.Canceled, "Cancellation not reported as Canceled");
            SceneProbe<A>.Gate.TrySetResult();
            return UniTask.CompletedTask;
        });

        await Run("scenes: overlapping requests rejected; original transition remains intact", async () =>
        {
            var manager = StartScenes(out var host, out _);
            var first = manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single);
            await Throws<InvalidOperationException>(() => manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Single));
            Check(manager.IsTransitioning, "Rejected request cleared active gate");
            await Finish(first);
            Check(manager.CurrentScene is SceneProbe<A>, "Wrong scene became active");
            host.Shutdown();
        });

        await Run("scenes: bad target does not stop active scene", async () =>
        {
            var manager = StartScenes(out var host, out _);
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            var previous = manager.CurrentScene;
            await Throws<ArgumentException>(() => manager.TransitionAsync<SceneProbe<B>>("missing", LoadSceneMode.Single));
            Check(manager.CurrentScene == previous && SceneProbe<A>.Last.Finalized == 0 && Addressables.Operations[0].Releases == 0, "Validation changed current scene");
            host.Shutdown();
        });

        await Run("scenes: single transition destroys controller and releases previous assets after native replacement", async () =>
        {
            var manager = StartScenes(out var host, out _);
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            var previous = SceneProbe<A>.Last;
            var native = ((UnityEngine.ResourceManagement.ResourceProviders.SceneInstance)Addressables.SceneOperations[0].Result).Scene;
            var asset = Addressables.Operations[0];
            Addressables.Released = operation =>
            {
                if (ReferenceEquals(operation, asset))
                    Check(!native.isLoaded && UnityEngine.Object.Destroyed.Contains(previous.gameObject), "Asset released before native unload and controller destruction");
            };
            await Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Single));
            Check(previous.Finalized == 1, "Duplicate/missing stop");
            Check(UnityEngine.Object.Destroyed.Contains(previous.gameObject) && !native.isLoaded && Addressables.Operations[0].Releases == 1, "Old scene ownership retained");
            Check(manager.CurrentScene is SceneProbe<B>, "Next scene not active");
            host.Shutdown();
        });

        await Run("scenes: additive scope unloads independently and type can be loaded again", async () =>
        {
            var manager = StartScenes(out var host, out _);
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            await Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Additive));
            var first = manager.GetScene<SceneProbe<B>>();
            await Finish(manager.UnloadAdditiveAsync<SceneProbe<B>>());
            Check(manager.CurrentScene is SceneProbe<A> && manager.GetScene<SceneProbe<B>>() == null, "Primary affected");
            Check(Addressables.Operations[0].Releases == 0 && Addressables.Operations[1].Releases == 1, "Scope isolation failed");
            await Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Additive));
            Check(!ReferenceEquals(first, manager.GetScene<SceneProbe<B>>()), "Stopped component reused");
            host.Shutdown();
        });

        await Run("scenes: additive requires primary and rejects duplicate type", async () =>
        {
            var manager = StartScenes(out var host, out _);
            await Throws<InvalidOperationException>(() => manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Additive));
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            await Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Additive));
            await Throws<InvalidOperationException>(() => manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Additive));
            Check(Addressables.SceneOperations.Count == 2, "Invalid request loaded native scene");
            host.Shutdown();
        });

        await Run("scenes: failed primary initialization stays tracked and next single recovers", async () =>
        {
            var manager = StartScenes(out var host, out _);
            SceneProbe<A>.FailEnter = true;
            await Throws<InvalidOperationException>(() => Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single)));
            Check(manager.CurrentScene == null && !manager.IsTransitioning && SceneProbe<A>.Last.State == SceneState.Stopped, "Failure state invalid");
            Check(Addressables.Operations[0].Releases == 0, "Only loaded scene released prematurely");
            await Throws<InvalidOperationException>(() => manager.UnloadAdditiveAsync<SceneProbe<A>>());
            SceneProbe<A>.FailEnter = false;
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            Check(manager.CurrentScene != null && Addressables.Operations[0].Releases == 1, "Retry did not clean previous scene");
            host.Shutdown();
        });

        await Run("scenes: native load failure leaves holding scene after old ownership is released", async () =>
        {
            var manager = StartScenes(out var host, out _);
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            Addressables.SceneFactory = () => new FakeOperation { IsDone = true, Error = new InvalidOperationException("Load failed") };
            await Throws<InvalidOperationException>(() => Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Single)));
            Check(Addressables.Operations[0].Releases == 1 && manager.CurrentScene == null && !manager.IsTransitioning, "Load failure lost ownership or gate");
            Addressables.SceneFactory = null;
            await Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Single));
            Check(Addressables.Operations[0].Releases == 1 && manager.CurrentScene is SceneProbe<B>, "Retry failed");
            host.Shutdown();
        });

        await Run("scenes: native load finishing after shutdown is unloaded without scene initialization", async () =>
        {
            var manager = StartScenes(out var host, out _);
            Addressables.SceneFactory = () => new FakeOperation();
            var pending = manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single);
            FakeFrames.Pump();
            host.Shutdown();
            await Throws<OperationCanceledException>(() => Finish(pending));
            Addressables.SceneOperations[0].Complete();
            Check(SceneProbe<A>.Last == null && Addressables.UnloadOperations.Count == 1 && Addressables.SceneOperations[0].Releases == 1, "Late load leaked or initialized");
        });

        await Run("scenes: failed additive initialization unloads native scene and returns scope", async () =>
        {
            var manager = StartScenes(out var host, out _);
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            SceneProbe<B>.FailEnter = true;
            await Throws<InvalidOperationException>(() => Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Additive)));
            Check(manager.CurrentScene is SceneProbe<A> && Addressables.Operations[1].Releases == 1 && Addressables.UnloadOperations.Count == 1, "Partial scene leaked");
            SceneProbe<B>.FailEnter = false;
            await Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Additive));
            host.Shutdown();
        });

        await Run("scenes: finalization failure still destroys scene once and permits retry", async () =>
        {
            var manager = StartScenes(out var host, out _);
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            SceneProbe<A>.FailStop = true;
            await Throws<AggregateException>(() => Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Single)));
            Check(SceneProbe<A>.Last.Finalized == 1 && SceneProbe<A>.Last.State == SceneState.Stopped && !manager.IsTransitioning, "Stop aborted or gate stuck");
            await Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Single));
            Check(SceneProbe<A>.Last.Finalized == 1, "Finalized twice");
            host.Shutdown();
        });

        await Run("scenes: late OnEnter completion after shutdown cannot initialize or become active", async () =>
        {
            var manager = StartScenes(out var host, out _);
            SceneProbe<A>.Gate = new UniTaskCompletionSource();
            var pending = manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single);
            FakeFrames.Pump();
            var scene = SceneProbe<A>.Last;
            host.Shutdown();
            await Throws<OperationCanceledException>(() => Finish(pending));
            SceneProbe<A>.Gate.TrySetResult();
            Check(scene.Initialized == 1 && scene.Finalized == 1 && scene.State == SceneState.Stopped && manager.CurrentScene == null, "Scene revived after shutdown");
        });

        await Run("scenes: update reaches primary and additive only while active", async () =>
        {
            var manager = StartScenes(out var host, out _);
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            await Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Additive));
            host.Update(1f);
            Check(SceneProbe<A>.Last.Updated == 1 && SceneProbe<B>.Last.Updated == 1, "Scene updates not dispatched");
            var closing = manager.UnloadAdditiveAsync<SceneProbe<B>>();
            host.Update(1f);
            Check(SceneProbe<B>.Last.Updated == 1, "Stopped scene updated");
            await Finish(closing);
            host.Shutdown();
        });

        await Run("scenes: throwing completion callback is not called twice", () =>
        {
            var manager = StartScenes(out var host, out _);
            int count = 0;
            manager.Transition<SceneProbe<A>>("A", LoadSceneMode.Single, _ => { count++; throw new Exception("Callback"); });
            FakeFrames.Pump();
            Check(count == 1 && manager.CurrentScene != null && !manager.IsTransitioning, "Callback changed transition outcome");
            host.Shutdown();
            return UniTask.CompletedTask;
        });

        await Run("scenes: immediate additive transition inside update does not invalidate iteration", async () =>
        {
            var manager = StartScenes(out var host, out _);
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            SceneProbe<A>.UpdateAction = () => manager.Transition<SceneProbe<B>>("B", LoadSceneMode.Additive, _ => { });
            host.Update(1f);
            Check(manager.GetScene<SceneProbe<B>>() != null && SceneProbe<B>.Last.Updated == 0, "Reentrant update did not stop iteration");
            SceneProbe<A>.UpdateAction = null;
            host.Update(1f);
            Check(SceneProbe<B>.Last.Updated == 1, "New scene not updated next tick");
            host.Shutdown();
        });

        await Run("scenes: failed additive unload keeps ownership until retry succeeds", async () =>
        {
            var manager = StartScenes(out var host, out _);
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            await Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Additive));
            Addressables.UnloadFactory = () => new FakeOperation { IsDone = true, Error = new InvalidOperationException("Unload failed") };
            await Throws<InvalidOperationException>(() => Finish(manager.UnloadAdditiveAsync<SceneProbe<B>>()));
            Check(Addressables.Operations[1].Releases == 0 && Addressables.SceneOperations[1].Releases == 0, "Failure dropped scene ownership");
            Addressables.UnloadFactory = null;
            await Finish(manager.UnloadAdditiveAsync<SceneProbe<B>>());
            Check(Addressables.Operations[1].Releases == 1 && SceneProbe<B>.Last.Finalized == 1, "Retry leaked or finalized twice");
            host.Shutdown();
        });

        await Run("scenes: pending native unload is shared and not restarted during shutdown", async () =>
        {
            var manager = StartScenes(out var host, out var resource);
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            await Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Additive));
            Addressables.UnloadFactory = () => new FakeOperation();
            var closing = manager.UnloadAdditiveAsync<SceneProbe<B>>();
            FakeFrames.Pump();
            var instance = (UnityEngine.ResourceManagement.ResourceProviders.SceneInstance)Addressables.SceneOperations[1].Result;
            var second = resource.UnloadAddressableSceneAsync(instance);
            Check(Addressables.UnloadOperations.Count == 1, "Duplicate unload");
            Addressables.UnloadFactory = null;
            host.Shutdown();
            Check(Addressables.UnloadOperations.Count == 2, "Shutdown restarted additive unload");
            Addressables.UnloadOperations[0].Complete();
            await Finish(closing);
            await second;
            Check(Addressables.UnloadOperations[0].Releases == 1, "Unload handle leaked");
        });

        await Run("scenes: only single transitions clear transient widgets and spatial sounds", async () =>
        {
            var manager = StartScenes(out var host, out _);
            Check(Global.UI.TransientClears == 0 && Global.Sound.SpatialStops == 0, "Widgets or spatial sounds cleared before any transition");
            await Finish(manager.TransitionAsync<SceneProbe<A>>("A", LoadSceneMode.Single));
            Check(Global.UI.TransientClears == 1, "Single transition did not clear transient widgets");
            Check(Global.Sound.SpatialStops == 1, "Single transition did not stop spatial sounds");
            await Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Additive));
            await Finish(manager.UnloadAdditiveAsync<SceneProbe<B>>());
            Check(Global.UI.TransientClears == 1 && Global.Sound.SpatialStops == 1, "Additive transition cleared widgets or spatial sounds");
            await Finish(manager.TransitionAsync<SceneProbe<B>>("B", LoadSceneMode.Single));
            Check(Global.UI.TransientClears == 2 && Global.Sound.SpatialStops == 2, "Next single transition did not clear widgets or spatial sounds");
            host.Shutdown();
        });

        await Run("scenes: single transition returns scene pool objects at stop before unload and scope disposal", async () =>
        {
            var manager = StartPoolScenes(out var host);
            await Finish(manager.TransitionAsync<PoolScene<A>>("A", LoadSceneMode.Single));
            var previous = PoolScene<A>.Last;
            var scope = previous.ResourceScope;
            var native = ((UnityEngine.ResourceManagement.ResourceProviders.SceneInstance)Addressables.SceneOperations[0].Result).Scene;
            var transition = manager.TransitionAsync<PoolScene<B>>("B", LoadSceneMode.Single);
            Check(IsReturnedAtStop(previous), "Scene pool object kept running after stop");
            Check(native.isLoaded && !scope.Token.IsCancellationRequested, "Scene pool cleanup waited for unload or scope disposal");
            await Finish(transition);
            Check(scope.Token.IsCancellationRequested && previous.Item.Returns == 1, "Scope disposal returned the cleaned pool again");
            Check(PoolScene<B>.Last.Item.Returns == 0 && PoolScene<B>.Last.Item.gameObject.activeSelf, "Next scene pool affected");
            host.Shutdown();
        });

        await Run("scenes: additive unload returns only its scene pool objects at stop before scope disposal", async () =>
        {
            var manager = StartPoolScenes(out var host);
            await Finish(manager.TransitionAsync<PoolScene<A>>("A", LoadSceneMode.Single));
            await Finish(manager.TransitionAsync<PoolScene<B>>("B", LoadSceneMode.Additive));
            var additive = PoolScene<B>.Last;
            var scope = additive.ResourceScope;
            var native = ((UnityEngine.ResourceManagement.ResourceProviders.SceneInstance)Addressables.SceneOperations[1].Result).Scene;
            var closing = manager.UnloadAdditiveAsync<PoolScene<B>>();
            Check(IsReturnedAtStop(additive), "Additive pool object kept running after stop");
            Check(native.isLoaded && !scope.Token.IsCancellationRequested, "Additive pool cleanup waited for unload or scope disposal");
            Check(PoolScene<A>.Last.Item.Returns == 0 && PoolScene<A>.Last.Item.gameObject.activeSelf, "Primary scene pool affected");
            await Finish(closing);
            Check(scope.Token.IsCancellationRequested && additive.Item.Returns == 1, "Scope disposal returned the cleaned pool again");
            host.Shutdown();
        });

        await Run("scenes: failed single and additive entry return scene pool objects once", async () =>
        {
            var manager = StartPoolScenes(out var host);
            PoolScene<A>.FailEnter = true;
            await Throws<InvalidOperationException>(() => Finish(manager.TransitionAsync<PoolScene<A>>("A", LoadSceneMode.Single)));
            var failed = PoolScene<A>.Last;
            Check(IsReturnedAtStop(failed) && !failed.ResourceScope.Token.IsCancellationRequested, "Failed single entry kept its pool object");
            PoolScene<A>.FailEnter = false;
            await Finish(manager.TransitionAsync<PoolScene<A>>("A", LoadSceneMode.Single));
            Check(failed.Item.Returns == 1 && failed.ResourceScope.Token.IsCancellationRequested, "Retry returned the failed scene pool again");
            PoolScene<B>.FailEnter = true;
            await Throws<InvalidOperationException>(() => Finish(manager.TransitionAsync<PoolScene<B>>("B", LoadSceneMode.Additive)));
            Check(IsReturnedAtStop(PoolScene<B>.Last) && PoolScene<B>.Last.ResourceScope.Token.IsCancellationRequested, "Failed additive entry kept or repeated its pool cleanup");
            Check(PoolScene<A>.Last.Item.Returns == 0, "Primary scene pool affected");
            host.Shutdown();
        });

        await Run("scenes: manager shutdown returns scene pool objects while the pool manager is ready", async () =>
        {
            var manager = StartPoolScenes(out var host);
            await Finish(manager.TransitionAsync<PoolScene<A>>("A", LoadSceneMode.Single));
            await Finish(manager.TransitionAsync<PoolScene<B>>("B", LoadSceneMode.Additive));
            var pool = Global.Pool;
            var states = new List<ManagerState>();
            ScenePoolItem.Returning = _ => states.Add(pool.State);
            host.Shutdown();
            Check(IsReturnedAtStop(PoolScene<A>.Last) && IsReturnedAtStop(PoolScene<B>.Last), "Shutdown skipped or repeated scene pool cleanup");
            Check(states.Count == 2 && states.TrueForAll(state => state == ManagerState.Ready), "Scene pools were left to pool manager shutdown");
        });

        await Run("scenes: stop failure and scene pool cleanup failure both reach the close boundary", async () =>
        {
            var manager = StartPoolScenes(out var host);
            await Finish(manager.TransitionAsync<PoolScene<A>>("A", LoadSceneMode.Single));
            await Finish(manager.TransitionAsync<PoolScene<B>>("B", LoadSceneMode.Additive));
            var additive = PoolScene<B>.Last;
            PoolScene<B>.FailStop = true;
            ScenePoolItem.FailReturn = true;
            var error = await Capture(() => Finish(manager.UnloadAdditiveAsync<PoolScene<B>>()));
            Check(HasStopAndReturnErrors(error), "Additive close lost a failure");
            Check(IsReturnedAtStop(additive) && additive.ResourceScope.Token.IsCancellationRequested && manager.GetScene<PoolScene<B>>() == null, "Additive close stopped after failures");
            PoolScene<B>.FailStop = false;
            var primary = PoolScene<A>.Last;
            PoolScene<A>.FailStop = true;
            error = await Capture(() => Finish(manager.TransitionAsync<PoolScene<B>>("B", LoadSceneMode.Single)));
            Check(HasStopAndReturnErrors(error), "Single transition lost a failure");
            Check(IsReturnedAtStop(primary) && !manager.IsTransitioning, "Single stop failure skipped scene pool cleanup");
            ScenePoolItem.FailReturn = false;
            await Finish(manager.TransitionAsync<PoolScene<B>>("B", LoadSceneMode.Single));
            Check(primary.Item.Returns == 1 && primary.ResourceScope.Token.IsCancellationRequested, "Retry returned the failed scene pool again");
            host.Shutdown();
        });
    }
}
