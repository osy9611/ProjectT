using ProjectT;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;

static partial class Program
{
    sealed class A { }
    sealed class B { }
    sealed class C { }
    sealed class Probe<T> : ManagerBase
    {
        public readonly List<string> Trace;
        public readonly string Id;
        public UniTask Gate = UniTask.CompletedTask;
        public Action StartAction;
        public Action StopAction;
        public Action UpdateAction;
        public bool FailStart, FailStop;
        public int Starts, Stops, Updates, Fixed, Late, Focuses, Pauses;
        public GameObject Owned;
        public CancellationToken Token => LifetimeToken;
        public Probe(List<string> trace, string id) { Trace = trace; Id = id; }
        public U Dependency<U>() where U : ManagerBase => Global.GetManager<U>();
        protected override async UniTask OnInitializeAsync(CancellationToken token)
        {
            Starts++;
            Trace.Add("start:" + Id);
            CreateRootObject(Id);
            Owned = RootObject.gameObject;
            StartAction?.Invoke();
            await Gate;
            if (FailStart) throw new InvalidOperationException("start:" + Id);
        }
        protected override void OnShutdown(ShutdownReason reason)
        {
            Stops++;
            Trace.Add("stop:" + Id + ":" + reason);
            StopAction?.Invoke();
            if (FailStop) throw new InvalidOperationException("stop:" + Id);
        }
        public override void OnUpdate(float dt) { Updates++; UpdateAction?.Invoke(); }
        public override void OnFixedUpdate(float dt) { Fixed++; }
        public override void OnLateUpdate() { Late++; }
        public override void OnAppFocus(bool focused) { Focuses++; }
        public override void OnAppPause(bool paused) { Pauses++; }
    }
    static Global BindGlobal(ManagerHost host)
    {
        var global = new GameObject("Global").AddComponent<Global>();
        typeof(Global).GetField("host", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(global, host);
        typeof(Global).GetField("s_instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).SetValue(null, global);
        return global;
    }

    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    static async UniTask Throws<T>(Func<UniTask> work) where T : Exception
    {
        try { await work(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    static async UniTask<Exception> Capture(Func<UniTask> work)
    {
        try
        {
            await work();
        }
        catch (Exception error)
        {
            return error;
        }

        throw new Exception("Expected failure");
    }
    static UniTask ThrowsSync<T>(Action work) where T : Exception => Throws<T>(() => { work(); return UniTask.CompletedTask; });
    static readonly List<string> failures = new();
    static async UniTask Run(string name, Func<UniTask> test)
    {
        UnityEngine.Object.Destroyed.Clear();
        try
        {
            await test();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            failures.Add(name);
            Console.WriteLine("FAIL " + name + " :: " + error.GetType().Name + ": " + error.Message);
        }
    }
    static int Main()
    {
        RunAll().GetAwaiter().GetResult();
        Console.WriteLine(failures.Count == 0 ? "ALL PASSED" : failures.Count + " FAILED");
        return failures.Count == 0 ? 0 : 1;
    }

    static async UniTask RunAll()
    {
        await RunControllerTests();
        await RunActorTests();

        await Run("Global: initialized dependency is accessible while whole host remains pending", async () =>
        {
            var host = new ManagerHost();
            var global = BindGlobal(host);
            var resource = host.Register(new ResourceManager());
            var gate = new UniTaskCompletionSource();
            var pending = host.Register(new Probe<A>(new(), "pending") { Gate = gate.Task });
            var initialization = host.InitializeAsync();
            var ready = Global.WhenReadyAsync();
            Check(ReferenceEquals(Global.Resource, resource) && ReferenceEquals(Global.GetManager<ResourceManager>(), resource), "Typed and generic lookup differ");
            Check(!Global.IsReady && !Global.TryGetReady<ResourceManager>(out _) && !ready.Status.IsCompleted(), "Whole host readiness published early");
            await ThrowsSync<InvalidOperationException>(() => Global.GetManager<Probe<A>>());
            await ThrowsSync<InvalidOperationException>(() => Global.GetManager<Probe<B>>());
            host.Update(1f);
            Check(pending.Updates == 0, "Update before all managers initialized");
            gate.TrySetResult();
            await initialization;
            await ready;
            Check(Global.TryGetReady<ResourceManager>(out var found) && ReferenceEquals(found, resource), "Ready lookup failed");
            global.Shutdown();
        });

        await Run("Global: readiness waiter started before Global awakens resolves after it becomes ready", async () =>
        {
            ResetGlobal();
            FakeFrames.Clear();
            var ready = Global.WhenReadyAsync();
            Check(Global.State == ManagerState.Created && !ready.Status.IsCompleted(), "Waiter resolved without Global");
            var host = new ManagerHost();
            var global = BindGlobal(host);
            host.Register(new Probe<A>(new(), "A"));
            await host.InitializeAsync();
            FakeFrames.Pump();
            await ready;
            Check(Global.IsReady, "Waiter resumed before readiness");
            global.Shutdown();
        });

        await Run("Global: shutdown resolves ready dependencies and rejects new work", async () =>
        {
            var host = new ManagerHost();
            var global = BindGlobal(host);
            var resource = host.Register(new ResourceManager());
            host.Register(new PoolManager());
            var probe = host.Register(new Probe<A>(new(), "consumer"));
            host.Register(new Probe<B>(new(), "stopped first"));
            bool checkedShutdown = false;
            probe.StopAction = () =>
            {
                Check(ReferenceEquals(Global.Resource, resource), "Ready dependency inaccessible");
                Check(!Global.TryGetReady<ResourceManager>(out _), "Whole host readiness exposed during shutdown");
                ThrowsSync<InvalidOperationException>(() => Global.GetManager<Probe<A>>()).GetAwaiter().GetResult();
                ThrowsSync<InvalidOperationException>(() => Global.GetManager<Probe<B>>()).GetAwaiter().GetResult();
                ThrowsSync<InvalidOperationException>(() => Global.GetManager<Probe<C>>()).GetAwaiter().GetResult();
                ThrowsSync<OperationCanceledException>(() => Global.Resource.LoadAndGet<GameObject>("shutdown")).GetAwaiter().GetResult();
                checkedShutdown = true;
            };
            await host.InitializeAsync();
            global.Shutdown();
            Check(checkedShutdown && host.ShutdownErrors.Count == 0, "Shutdown access contract failed");
            await ThrowsSync<InvalidOperationException>(() => host.GetReadyManager<ResourceManager>());
            await ThrowsSync<InvalidOperationException>(() => Global.GetManager<ResourceManager>());
        });

        await Run("Global: absent and unstarted host cannot expose registered managers", async () =>
        {
            var global = BindGlobal(null);
            await ThrowsSync<InvalidOperationException>(() => Global.GetManager<ResourceManager>());
            var host = new ManagerHost();
            BindGlobal(host);
            host.Register(new ResourceManager());
            await ThrowsSync<InvalidOperationException>(() => Global.GetManager<ResourceManager>());
            Check(!Global.TryGetReady<ResourceManager>(out _), "Created manager exposed");
            host.Shutdown();
        });

        await Run("root: manager uses Global parent and shutdown preserves Global", async () =>
        {
            var host = new ManagerHost();
            var global = BindGlobal(host);
            var probe = host.Register(new Probe<A>(new(), "child"));
            await host.InitializeAsync();
            var managerRoot = probe.RootObject.gameObject;
            Check(ReferenceEquals(probe.RootObject.parent, global.transform), "Manager root is not parented to Global");
            host.Shutdown();
            Check(UnityEngine.Object.Destroyed.Contains(managerRoot), "Manager root was not destroyed on shutdown");
            Check(!UnityEngine.Object.Destroyed.Contains(global.gameObject), "Global parent destroyed by manager");
        });

        await RunResourceTests();
        await RunSceneTests();
        await RunSceneContractTests();
        await RunPoolTests();
        await Run("ready waits for preparation; concurrent waiters; gated updates", async () =>
        {
            var gate = new UniTaskCompletionSource<bool>();
            var host = new ManagerHost(); BindGlobal(host);
            var probe = new Probe<A>(new(), "A") { Gate = gate.Task };
            host.Register(probe);
            var w1 = host.WhenReady;
            var w2 = host.WhenReady;
            var init = host.InitializeAsync();
            var repeated = host.InitializeAsync();
            host.Update(1); host.FixedUpdate(1); host.LateUpdate(); host.Focus(true); host.Pause(false);
            Check(!w1.Status.IsCompleted() && !host.TryGetReady<Probe<A>>(out _), "Ready published early.");
            Check(probe.Updates + probe.Fixed + probe.Late + probe.Focuses + probe.Pauses == 0, "Early callback.");
            var allWaiters = UniTask.WhenAll(init, repeated, w1, w2);
            Check(!allWaiters.Status.IsCompleted(), "Waiters completed before preparation.");
            gate.TrySetResult(true);
            await allWaiters;
            Check(probe.Starts == 1, "Initialization/configuration mismatch.");
            host.Update(1); host.FixedUpdate(1); host.LateUpdate(); host.Focus(true); host.Pause(false);
            Check(probe.Updates == 1 && probe.Fixed == 1 && probe.Late == 1 && probe.Focuses == 1 && probe.Pauses == 1, "Callback missing.");
            host.Shutdown();
            host.Update(1); host.FixedUpdate(1); host.LateUpdate();
            Check(probe.Updates == 1 && probe.Fixed == 1 && probe.Late == 1, "Callback after shutdown.");
        });
        await Run("reverse shutdown preserves dependencies; same display names use exact types", async () =>
        {
            var trace = new List<string>();
            var host = new ManagerHost(); BindGlobal(host);
            var a = new Probe<A>(trace, "A"); var b = new Probe<B>(trace, "B");
            Probe<A> dependency = null;
            b.StartAction = () =>
            {
                dependency = b.Dependency<Probe<A>>();
                Check(ReferenceEquals(dependency, a), "Startup dependency unavailable.");
            };
            b.StopAction = () => Check(dependency.State == ManagerState.Ready, "Dependency stopped early.");
            host.Register(a); host.Register(b);
            await host.InitializeAsync();
            Check(a.Name == b.Name && Global.GetManager<Probe<B>>() == b, "Type registry mismatch.");
            host.Shutdown(); host.Shutdown();
            Check(string.Join(",", trace) == "start:A,start:B,stop:B:Normal,stop:A:Normal", "Incorrect lifecycle order.");
            Check(a.Stops == 1 && b.Stops == 1 && host.ShutdownErrors.Count == 0, "Repeated/failed cleanup.");
            Check(UnityEngine.Object.Destroyed.Count == 2 && UnityEngine.Object.Destroyed.All(o => o is GameObject), "Wrong destruction target.");
            Check(!a.Owned.Active && a.RootObject == null, "Root remains usable.");
        });
        await Run("partial startup failure rolls back failing manager and preserves error", async () =>
        {
            var trace = new List<string>(); var host = new ManagerHost(); BindGlobal(host);
            var a = new Probe<A>(trace, "A"); var b = new Probe<B>(trace, "B") { FailStart = true }; var c = new Probe<C>(trace, "C");
            host.Register(a); host.Register(b); host.Register(c);
            await Throws<InvalidOperationException>(() => host.InitializeAsync());
            await Throws<InvalidOperationException>(() => host.WhenReady);
            Check(host.State == ManagerState.Failed && host.InitializationError.Message == "start:B", "Failure lost.");
            Check(a.Stops == 1 && b.Stops == 1 && c.Starts == 0 && c.Stops == 0, "Rollback scope incorrect.");
            Check(b.RootObject == null && UnityEngine.Object.Destroyed.Contains(b.Owned), "Partial root leaked.");
        });
        await Run("cleanup failure does not skip other managers or root cleanup", async () =>
        {
            var host = new ManagerHost(); BindGlobal(host);
            var a = new Probe<A>(new(), "A"); var b = new Probe<B>(new(), "B") { FailStop = true };
            host.Register(a); host.Register(b); await host.InitializeAsync(); host.Shutdown();
            Check(a.Stops == 1 && b.Stops == 1 && host.ShutdownErrors.Count == 1, "Cleanup exception not isolated.");
            Check(b.RootObject == null && host.State == ManagerState.Stopped, "Finally cleanup failed.");
        });
        await Run("shutdown during await cancels readiness and prevents late Ready", async () =>
        {
            var gate = new UniTaskCompletionSource<bool>(); var host = new ManagerHost(); BindGlobal(host);
            var a = new Probe<A>(new(), "A") { Gate = gate.Task };
            host.Register(a); var init = host.InitializeAsync(); host.Shutdown();
            await Throws<OperationCanceledException>(() => host.WhenReady);
            gate.TrySetResult(true);
            await Throws<OperationCanceledException>(() => init);
            Check(host.State == ManagerState.Stopped && a.State == ManagerState.Stopped && a.Stops == 1, "Late initialization resurrected manager.");
        });
        await Run("synchronous cancellation callback reentrancy keeps normal shutdown", async () =>
        {
            var gate = new UniTaskCompletionSource<bool>(); var host = new ManagerHost(); BindGlobal(host);
            var a = new Probe<A>(new(), "A") { Gate = gate.Task };
            a.StartAction = () => a.Token.Register(() => gate.TrySetCanceled());
            host.Register(a); var init = host.InitializeAsync(); host.Shutdown();
            await Throws<OperationCanceledException>(() => init);
            await Throws<OperationCanceledException>(() => host.WhenReady);
            Check(host.State == ManagerState.Stopped && host.InitializationError == null && a.Stops == 1, "Cancellation treated as startup failure.");
        });
        await Run("throwing cancellation subscriber does not stop cleanup", async () =>
        {
            var host = new ManagerHost(); BindGlobal(host); var a = new Probe<A>(new(), "A");
            a.StartAction = () => a.Token.Register(() => throw new Exception("cancel"));
            host.Register(a); await host.InitializeAsync(); host.Shutdown();
            Check(a.Stops == 1 && host.ShutdownErrors.Count == 1, "Cancellation exception broke shutdown.");
        });
        await Run("duplicate registration and mutation after startup rejected", async () =>
        {
            var host = new ManagerHost(); BindGlobal(host); var a = new Probe<A>(new(), "A"); host.Register(a);
            await ThrowsSync<ArgumentException>(() => host.Register(new Probe<A>(new(), "duplicate")));
            await ThrowsSync<InvalidOperationException>(() => Global.GetManager<Probe<A>>());
            await host.InitializeAsync();
            await ThrowsSync<InvalidOperationException>(() => host.Register(new Probe<B>(new(), "B")));
            host.Shutdown();
        });
        await Run("out-of-order dependency fails and rolls back", async () =>
        {
            var host = new ManagerHost(); BindGlobal(host); var a = new Probe<A>(new(), "A"); var b = new Probe<B>(new(), "B");
            a.StartAction = () => a.Dependency<Probe<B>>();
            host.Register(a); host.Register(b);
            await Throws<InvalidOperationException>(() => host.InitializeAsync());
            Check(a.Stops == 1 && b.Starts == 0, "Invalid dependency order accepted.");
        });
        await Run("shutdown before initialization settles waiters without initializing", async () =>
        {
            var host = new ManagerHost(); BindGlobal(host); var a = new Probe<A>(new(), "A"); host.Register(a); host.Shutdown();
            await Throws<OperationCanceledException>(() => host.WhenReady);
            await ThrowsSync<InvalidOperationException>(() => host.InitializeAsync());
            Check(a.Starts == 0 && a.Stops == 0, "Uninitialized manager executed.");
        });
        await Run("external shutdown cannot claim initialization failure", async () =>
        {
            var host = new ManagerHost(); BindGlobal(host); var a = new Probe<A>(new(), "A"); host.Register(a);
            await host.InitializeAsync();
            await ThrowsSync<ArgumentException>(() => host.Shutdown(ShutdownReason.InitializationFailure));
            Check(host.State == ManagerState.Ready && a.Stops == 0, "Rejected shutdown changed the host.");
            host.Shutdown();
            Check(host.State == ManagerState.Stopped && a.Stops == 1, "Normal shutdown after rejection failed.");
        });
        await Run("canceling one readiness waiter leaves other waiters alive", async () =>
        {
            var gate = new UniTaskCompletionSource<bool>();
            var host = new ManagerHost(); BindGlobal(host);
            var probe = new Probe<A>(new(), "A") { Gate = gate.Task };
            host.Register(probe);
            var initialization = host.InitializeAsync();
            using var cancellation = new CancellationTokenSource();
            var canceledWaiter = host.WhenReady.AttachExternalCancellation(cancellation.Token);
            var remainingWaiters = UniTask.WhenAll(initialization, host.WhenReady);
            cancellation.Cancel();
            await Throws<OperationCanceledException>(() => canceledWaiter);
            Check(!remainingWaiters.Status.IsCompleted(), "Caller cancellation canceled shared readiness.");
            gate.TrySetResult(true);
            await remainingWaiters;
            Check(host.State == ManagerState.Ready, "Caller cancellation stopped the host.");
            host.Shutdown();
        });
        await Run("registration returns the same reference and missing types stay absent", async () =>
        {
            var host = new ManagerHost(); BindGlobal(host);
            var a = new Probe<A>(new(), "A");
            Check(ReferenceEquals(host.Register(a), a), "Registration replaced the manager.");
            await host.InitializeAsync();
            Check(ReferenceEquals(Global.GetManager<Probe<A>>(), a), "Lookup returned a different manager.");
            Check(!host.TryGetReady<Probe<B>>(out var missing) && missing == null, "Missing type was resolved.");
            await ThrowsSync<InvalidOperationException>(() => Global.GetManager<Probe<B>>());
            host.Shutdown();
        });
        await Run("shutdown inside update prevents remaining manager callbacks", async () =>
        {
            var host = new ManagerHost(); BindGlobal(host);
            var a = new Probe<A>(new(), "A");
            var b = new Probe<B>(new(), "B");
            a.UpdateAction = () => host.Shutdown();
            host.Register(a);
            host.Register(b);
            await host.InitializeAsync();
            host.Update(1f);
            Check(a.Updates == 1 && b.Updates == 0, "Updates continued after shutdown.");
            Check(a.Stops == 1 && b.Stops == 1, "Shutdown missed a started manager.");
        });
        await Run("Pool ReturnAll returns every item exactly once and supports reuse", () =>
        {
            foreach (int count in new[] { 0, 1, 4, 127 })
            {
                using var pool = new ProjectT.Pool.Pool<PoolItem>(() => new PoolItem(), true, count, count);
                var borrowed = new HashSet<PoolItem>();
                for (int i = 0; i < count; ++i)
                    borrowed.Add(pool.Get());

                pool.ReturnAll();
                Check(pool.ActiveCount == 0 && pool.InactiveCount == count, "ReturnAll skipped an item.");
                Check(borrowed.All(item => item.ReturnCount == 1), "Return callback was skipped or duplicated.");
                pool.ReturnAll();
                Check(borrowed.All(item => item.ReturnCount == 1), "Empty ReturnAll repeated callbacks.");

                var reused = new HashSet<PoolItem>();
                for (int i = 0; i < count; ++i)
                    reused.Add(pool.Get());

                Check(reused.SetEquals(borrowed), "Returned items were not reused correctly.");
                pool.ReturnAll();
                Check(borrowed.All(item => item.ReturnCount == 2), "Reuse changed return accounting.");
            }

            return UniTask.CompletedTask;
        });
        await RunErrorReporterTests();
        Console.WriteLine("Unity engine behavior requires Play Mode verification.");
    }

    sealed class PoolItem : ProjectT.Pool.IPoolable
    {
        public int ReturnCount;

        public void OnGet()
        {
        }

        public void OnReturn()
        {
            ReturnCount++;
        }
    }
}
