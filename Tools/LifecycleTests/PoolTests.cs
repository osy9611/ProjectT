using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Pool;
using ProjectT.Addressable;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

static partial class Program
{
    sealed class Lease : IPoolable
    {
        public int Gets, Returns;
        public bool FailGet, FailReturn;
        public Action Getting;

        public void OnGet()
        {
            Gets++;
            Getting?.Invoke();
            if (FailGet) throw new InvalidOperationException("Get");
        }

        public void OnReturn()
        {
            Returns++;
            if (FailReturn) throw new InvalidOperationException("Return");
        }
    }

    public sealed class PoolComponent : MonoBehaviour, IPoolable
    {
        public static Action Getting;
        public int Gets, Returns;
        public Transform ParentAtGet;

        public void OnGet()
        {
            Gets++;
            ParentAtGet = transform.parent;
            Getting?.Invoke();
        }

        public Action ReturnAction;

        public void OnReturn()
        {
            Returns++;
            ReturnAction?.Invoke();
        }
    }

    // 풀·Scope 누수는 공개 API로 관찰할 수 없어 PoolManager의 Scope 상태를 직접 센다.
    static (int Pools, int OpenScopes, int ClosedScopes) ScopeState(PoolManager manager)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var scopes = (System.Collections.IDictionary)typeof(PoolManager).GetField("scopes", flags).GetValue(manager);
        int pools = 0, open = 0, closed = 0;
        foreach (var entry in scopes.Values)
        {
            pools += ((System.Collections.ICollection)entry.GetType().GetField("Pools", flags).GetValue(entry)).Count;
            if ((bool)entry.GetType().GetField("IsClosed", flags).GetValue(entry))
                closed++;
            else
                open++;
        }
        return (pools, open, closed);
    }

    static PoolManager StartPools(out ManagerHost host, out ResourceManager resource)
    {
        Addressables.Reset();
        host = new ManagerHost();
        Global.BindForTests(host);
        resource = host.Register(new ResourceManager());
        var pool = host.Register(new PoolManager());
        host.InitializeAsync().GetAwaiter().GetResult();
        return pool;
    }

    public sealed class PooledScene<T> : ProjectT.Scene.SceneBase
    {
        public static GameObject Instance;

        protected override void OnInitialize()
        {
            Instance = Global.Pool.Get("pooled", null, ResourceScope);
        }

        protected override void OnFinalize()
        {
        }

    }

    static async UniTask RunPoolTests()
    {
        await Run("pool: unchecked duplicate and foreign return cannot corrupt counts", () =>
        {
            using var pool = new Pool<Lease>(() => new Lease(), false, 0, 1);
            var item = pool.Get();
            pool.Return(item);
            pool.Return(item);
            pool.Return(new Lease());
            Check(pool.ActiveCount == 0 && pool.InactiveCount == 1 && item.Returns == 1, "Invalid return corrupted pool");
            return UniTask.CompletedTask;
        });

        await Run("pool: get callback failure propagates and clear reclaims the item", async () =>
        {
            int destroyed = 0;
            bool fail = true;
            using var pool = new Pool<Lease>(() => new Lease { FailGet = fail }, true, 0, 1, 1, _ => destroyed++);
            await ThrowsSync<InvalidOperationException>(() => pool.Get());
            Check(pool.ActiveCount == 1 && destroyed == 0, "Get failure changed pool state");
            pool.Clear();
            Check(pool.Count == 0 && destroyed == 1, "Clear did not reclaim the failed item");
            fail = false;
            Check(pool.Get() != null, "Capacity not restored");
        });

        await Run("pool: invalid generated item is created and destroyed once", async () =>
        {
            int generated = 0;
            int destroyed = 0;
            using var pool = new Pool<Lease>(
                () =>
                {
                    generated++;
                    return new Lease { FailGet = true };
                },
                true,
                0,
                1,
                destroy: _ => destroyed++,
                isValid: item => !item.FailGet);

            await ThrowsSync<InvalidOperationException>(() => pool.Get());
            Check(generated == 1 && destroyed == 1 && pool.Count == 0, "Invalid generation retried or leaked");
        });

        await Run("pool: return failure keeps item tracked while return all continues", async () =>
        {
            int destroyed = 0;
            using var pool = new Pool<Lease>(() => new Lease(), true, 0, 2, destroy: _ => destroyed++);
            var failed = pool.Get();
            failed.FailReturn = true;
            var other = pool.Get();
            await ThrowsSync<AggregateException>(() => pool.ReturnAll());
            Check(pool.ActiveCount == 1 && pool.InactiveCount == 1 && other.Returns == 1 && destroyed == 0, "Return failure stopped return all or dropped the item");
            failed.FailReturn = false;
        });

        await Run("pool: single return failure propagates unchanged and is never handed out", async () =>
        {
            using var pool = new Pool<Lease>(() => new Lease(), true, 0, 2);
            var failed = pool.Get();
            failed.FailReturn = true;
            await ThrowsSync<InvalidOperationException>(() => pool.Return(failed));
            Check(pool.ActiveCount == 1 && pool.InactiveCount == 0 && pool.IsActive(failed), "Failed return changed tracking");
            Check(!ReferenceEquals(failed, pool.Get()), "Failed item was handed out");
            failed.FailReturn = false;
        });

        await Run("pool: explicit return after a failure makes the item reusable", async () =>
        {
            using var pool = new Pool<Lease>(() => new Lease(), true, 0, 1);
            var failed = pool.Get();
            failed.FailReturn = true;
            await ThrowsSync<InvalidOperationException>(() => pool.Return(failed));
            failed.FailReturn = false;
            pool.Return(failed);
            Check(pool.ActiveCount == 0 && pool.InactiveCount == 1, "Retry did not move the item to inactive");
            Check(ReferenceEquals(failed, pool.Get()), "Recovered item was not reused");
        });

        await Run("pool: clear and dispose still destroy items whose return failed", async () =>
        {
            int destroyed = 0;
            using var cleared = new Pool<Lease>(() => new Lease(), true, 0, 2, destroy: _ => destroyed++);
            var failedA = cleared.Get();
            var otherA = cleared.Get();
            failedA.FailReturn = true;
            await ThrowsSync<InvalidOperationException>(() => cleared.Return(failedA));
            await ThrowsSync<AggregateException>(() => cleared.Clear());
            Check(cleared.Count == 0 && failedA.Returns == 2 && otherA.Returns == 1 && destroyed == 2, "Clear skipped the failed item");

            destroyed = 0;
            var disposed = new Pool<Lease>(() => new Lease(), true, 0, 2, destroy: _ => destroyed++);
            var failedB = disposed.Get();
            var otherB = disposed.Get();
            failedB.FailReturn = true;
            await ThrowsSync<InvalidOperationException>(() => disposed.Return(failedB));
            await ThrowsSync<AggregateException>(() => disposed.Dispose());
            Check(disposed.Count == 0 && failedB.Returns == 2 && otherB.Returns == 1 && destroyed == 2, "Dispose skipped the failed item");
        });

        await Run("pool: capacity still counts an item whose return failed", async () =>
        {
            using var pool = new Pool<Lease>(() => new Lease(), true, 0, 1, 1);
            var failed = pool.Get();
            failed.FailReturn = true;
            await ThrowsSync<InvalidOperationException>(() => pool.Return(failed));
            await ThrowsSync<InvalidOperationException>(() => pool.Get());
            Check(pool.ActiveCount == 1 && pool.InactiveCount == 0, "Capacity ignored the failed item");
            failed.FailReturn = false;
        });

        await Run("pool: dispose clears all once and rejects reuse", async () =>
        {
            int destroyed = 0;
            var pool = new Pool<Lease>(() => new Lease(), true, 2, 2, destroy: _ => destroyed++);
            var item = pool.Get();
            pool.Dispose();
            pool.Dispose();
            Check(pool.Count == 0 && item.Returns == 1 && destroyed == 2, "Dispose contract failed");
            await ThrowsSync<ObjectDisposedException>(() => pool.Get());
        });

        await Run("pool: clear discards cached and borrowed items but remains reusable", () =>
        {
            int destroyed = 0;
            using var pool = new Pool<Lease>(() => new Lease(), true, 2, 2, destroy: _ => destroyed++);
            var item = pool.Get();
            pool.Clear();
            Check(pool.Count == 0 && destroyed == 2 && !ReferenceEquals(item, pool.Get()), "Clear retained items");
            return UniTask.CompletedTask;
        });

        await Run("pool: callback reentry is rejected and the pool stays usable", async () =>
        {
            int destroyed = 0;
            bool reenter = true;
            Pool<Lease> pool = null;
            pool = new Pool<Lease>(() => new Lease { Getting = () => { if (reenter) pool.Get(); } }, true, 0, 1, destroy: _ => destroyed++);
            await ThrowsSync<InvalidOperationException>(() => pool.Get());
            reenter = false;
            Check(pool.Get() != null && pool.ActiveCount == 2, "Reentry left the pool locked");
            pool.Dispose();
            Check(pool.Count == 0 && destroyed == 2, "Dispose missed the failed item");
        });

        await Run("pool: remove destroys only selected item and reclaims capacity", () =>
        {
            int destroyed = 0;
            using var pool = new Pool<Lease>(() => new Lease(), true, 2, 2, 2, _ => destroyed++);
            var a = pool.Get();
            var b = pool.Get();
            pool.Return(b);
            Check(pool.Remove(b) && pool.Remove(a) && pool.Count == 0 && destroyed == 2, "Remove missed active or inactive");
            Check(pool.Get() != null, "Removed capacity unavailable");
            return UniTask.CompletedTask;
        });

        await Run("pool: renamed instances and same-name prefabs return to original owner", () =>
        {
            var manager = StartPools(out var host, out _);
            var first = new GameObject("Prefab");
            var second = new GameObject("Prefab");
            var a = manager.Get(first, null, Global.Resource.AppScope);
            var b = manager.Get(second, null, Global.Resource.AppScope);
            a.name = "Renamed";
            manager.Return(a);
            manager.Return(b);
            Check(ReferenceEquals(a, manager.Get(first, null, Global.Resource.AppScope)) && ReferenceEquals(b, manager.Get(second, null, Global.Resource.AppScope)), "Name-based ownership remains");
            host.Shutdown();
            return UniTask.CompletedTask;
        });

        await Run("pool: parent and component hooks are prepared before use", () =>
        {
            var manager = StartPools(out var host, out _);
            var prefab = new GameObject("Prefab");
            prefab.AddComponent<PoolComponent>();
            var parent = new GameObject("Parent").transform;
            var obj = manager.Get(prefab, parent, Global.Resource.AppScope);
            var component = obj.GetComponent<PoolComponent>();
            Check(component.Gets == 1 && component.ParentAtGet == parent, "Get hook missing or parent late");
            manager.Return(obj);
            Check(component.Returns == 1 && !obj.activeSelf, "Return hook or deactivation missing");
            manager.Get(prefab, null, Global.Resource.AppScope);
            Check(component.Gets == 2, "Component not reused");
            host.Shutdown();
            return UniTask.CompletedTask;
        });

        await Run("pool: spawn parent is released after get", () =>
        {
            var parent = new GameObject("Parent").transform;
            using var pool = new GameObjectPool(new GameObject("Prefab"), 0);
            pool.Get(parent);

            var retainedParent = typeof(GameObjectPool)
                .GetField("spawnParent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(pool);

            Check(retainedParent == null, "Pool retained the spawn parent");
            return UniTask.CompletedTask;
        });

        await Run("pool: rejected dispose keeps game object pool usable", async () =>
        {
            var prefab = new GameObject("Prefab");
            prefab.AddComponent<PoolComponent>();
            using var pool = new GameObjectPool(prefab, 0);
            PoolComponent.Getting = pool.Dispose;

            try
            {
                await ThrowsSync<InvalidOperationException>(() => pool.Get(null));
                Check(pool.Root != null, "Rejected dispose removed the pool root");
            }
            finally
            {
                PoolComponent.Getting = null;
            }

            Check(pool.Get(null) != null, "Rejected dispose left the pool unusable");
        });

        await Run("pool: failed dispose still destroys the game object pool root", async () =>
        {
            var prefab = new GameObject("Prefab");
            prefab.AddComponent<PoolComponent>();
            var pool = new GameObjectPool(prefab, 0);
            var root = pool.Root.gameObject;
            pool.Get(null).GetComponent<PoolComponent>().ReturnAction = () => throw new InvalidOperationException("Return failed");
            await ThrowsSync<AggregateException>(() => pool.Dispose());
            Check(pool.Root == null && UnityEngine.Object.Destroyed.Contains(root), "Failed dispose leaked the pool root");
            await ThrowsSync<ObjectDisposedException>(() => pool.Get(null));
        });

        await Run("pool: unknown object return has no destructive side effect", () =>
        {
            var manager = StartPools(out var host, out _);
            var obj = new GameObject("Unknown");
            Check(!manager.Return(obj) && !UnityEngine.Object.Destroyed.Contains(obj), "Foreign object destroyed");
            host.Shutdown();
            return UniTask.CompletedTask;
        });

        await Run("pool: explicit destroy removes active ownership", () =>
        {
            var manager = StartPools(out var host, out _);
            var prefab = new GameObject("Prefab");
            var obj = manager.Get(prefab, null, Global.Resource.AppScope);
            manager.Destroy(obj);
            Check(UnityEngine.Object.Destroyed.Contains(obj) && !ReferenceEquals(obj, manager.Get(prefab, null, Global.Resource.AppScope)), "Destroy did not remove lease");
            host.Shutdown();
            return UniTask.CompletedTask;
        });

        await Run("pool: scopes isolate instances while sharing original resource", () =>
        {
            var manager = StartPools(out var host, out var resources);
            Addressables.Factory = () => new FakeOperation { IsDone = true, Result = new GameObject("Prefab") };
            var scopeA = resources.CreateScope();
            var scopeB = resources.CreateScope();
            var a = manager.Get("asset", null, scopeA);
            var b = manager.Get("asset", null, scopeB);
            scopeB.Dispose();
            Check(UnityEngine.Object.Destroyed.Contains(b) && !UnityEngine.Object.Destroyed.Contains(a) && Addressables.Operations[0].Releases == 0, "Scope cleanup crossed ownership");
            scopeA.Dispose();
            Check(UnityEngine.Object.Destroyed.Contains(a) && Addressables.Operations[0].Releases == 1, "Last scope did not clean up");
            host.Shutdown();
            return UniTask.CompletedTask;
        });

        await Run("pool: scope cleanup requests destruction before resource release", () =>
        {
            var manager = StartPools(out var host, out var resources);
            Addressables.Factory = () => new FakeOperation { IsDone = true, Result = new GameObject("Prefab") };
            GameObject instance = null;
            bool cleaned = false;
            var scope = resources.CreateScope();
            scope.Token.Register(() =>
            {
                Check(UnityEngine.Object.Destroyed.Contains(instance), "Pool destruction request missing");
                Check(Addressables.Operations[0].Releases == 0, "Resource released before pool cleanup");
                cleaned = true;
            });
            instance = manager.Get("asset", null, scope);
            scope.Dispose();
            scope.Dispose();
            Check(cleaned && Addressables.Operations[0].Releases == 1 && manager.GetOriginal("Prefab") == null, "Scope retained ownership");
            host.Shutdown();
            Check(host.ShutdownErrors.Count == 0, "Shutdown repeated disposed scope cleanup");
            return UniTask.CompletedTask;
        });

        await Run("pool: cancel during prefab loading creates no late instance", async () =>
        {
            var manager = StartPools(out var host, out var resources);
            Addressables.Factory = () => new FakeOperation { Result = new GameObject("Prefab") };
            var scope = resources.CreateScope();
            var loading = manager.GetAsync("asset", null, scope);
            scope.Dispose();
            await Throws<OperationCanceledException>(async () => { await loading; });
            Addressables.Operations[0].Complete();
            Check(manager.GetOriginal("Prefab") == null, "Late load created pool");
            host.Shutdown();
        });

        await Run("pool: shutdown permits returns but rejects new leases", () =>
        {
            var manager = StartPools(out var host, out _);
            var prefab = new GameObject("Prefab");
            var instance = manager.Get(prefab, null, Global.Resource.AppScope);
            var token = (CancellationToken)typeof(ManagerBase)
                .GetProperty("LifetimeToken", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(manager);
            bool returned = false;
            token.Register(() =>
            {
                Check(manager.Return(instance), "Object return was rejected during shutdown");
                ThrowsSync<OperationCanceledException>(() => manager.Get(prefab, null, Global.Resource.AppScope)).GetAwaiter().GetResult();
                returned = true;
            });
            host.Shutdown();
            Check(returned && host.ShutdownErrors.Count == 0, "Shutdown return failed or repeated");
            return UniTask.CompletedTask;
        });

        await Run("pool: shutdown disposes cached objects", () =>
        {
            var manager = StartPools(out var host, out _);
            var obj = manager.Get(new GameObject("Prefab"), null, Global.Resource.AppScope);
            host.Shutdown();
            Check(UnityEngine.Object.Destroyed.Contains(obj), "Shutdown skipped object pool");
            return UniTask.CompletedTask;
        });

        await Run("pool: additive scope disposal destroys only its scene pool", async () =>
        {
            var scenes = StartScenes(out var host, out _);
            Addressables.Factory = () => new FakeOperation { Result = new GameObject("Prefab"), IsDone = true };
            await Finish(scenes.TransitionAsync<PooledScene<A>>("A", UnityEngine.SceneManagement.LoadSceneMode.Single));
            await Finish(scenes.TransitionAsync<PooledScene<B>>("B", UnityEngine.SceneManagement.LoadSceneMode.Additive));
            await Finish(scenes.UnloadAdditiveAsync<PooledScene<B>>());
            Check(UnityEngine.Object.Destroyed.Contains(PooledScene<B>.Instance) && !UnityEngine.Object.Destroyed.Contains(PooledScene<A>.Instance), "Scene pool ownership crossed");
            host.Shutdown();
        });

        await Run("pool: scope clear returns borrowed objects once and later scope disposal stays consistent", () =>
        {
            var manager = StartPools(out var host, out var resources);
            var prefab = new GameObject("Prefab");
            prefab.AddComponent<PoolComponent>();
            var scope = resources.CreateScope();
            var other = resources.CreateScope();
            var item = manager.Get(prefab, null, scope).GetComponent<PoolComponent>();
            var kept = manager.Get(prefab, null, other).GetComponent<PoolComponent>();
            manager.ClearScope(scope);
            Check(item.Returns == 1 && !item.gameObject.activeSelf && UnityEngine.Object.Destroyed.Contains(item.gameObject), "Scope clear left the borrowed object running");
            Check(kept.Returns == 0 && kept.gameObject.activeSelf, "Scope clear crossed ownership");
            scope.Dispose();
            manager.ClearScope(scope);
            Check(item.Returns == 1, "Scope disposal repeated cleanup");
            host.Shutdown();
            Check(host.ShutdownErrors.Count == 0 && kept.Returns == 1, "Shutdown repeated cleared scope or skipped another scope");
            return UniTask.CompletedTask;
        });

        await Run("pool: cleared scope rejects new leases without creating a pool until disposal removes the mark", async () =>
        {
            var manager = StartPools(out var host, out var resources);
            Addressables.Factory = () => new FakeOperation { IsDone = true, Result = new GameObject("Loaded") };
            var prefab = new GameObject("Prefab");
            var scope = resources.CreateScope();
            manager.Get(prefab, null, scope);
            manager.ClearScope(scope);
            int pools = ScopeState(manager).Pools;
            await ThrowsSync<InvalidOperationException>(() => manager.Get(prefab, null, scope));
            await ThrowsSync<InvalidOperationException>(() => manager.CreatePool(prefab, 1, scope));
            await ThrowsSync<InvalidOperationException>(() => manager.Get("asset", null, scope));
            await Throws<InvalidOperationException>(async () => { await manager.GetAsync("asset", null, scope); });
            Check(ScopeState(manager).Pools == pools && ScopeState(manager).OpenScopes == 0, "Rejected lease created a pool or scope registration");
            Check(Addressables.Operations.Count == 0, "Rejected lease started a resource load");
            Check(manager.Get(prefab, null, Global.Resource.AppScope) != null && manager.Get(prefab, null, resources.CreateScope()) != null, "Closing one scope blocked other scopes");
            Check(ScopeState(manager).ClosedScopes == 1, "Clear did not close the scope");
            scope.Dispose();
            Check(ScopeState(manager).ClosedScopes == 0, "Scope disposal kept the closed mark");
            var other = resources.CreateScope();
            manager.ClearScope(other);
            host.Shutdown();
            Check(ScopeState(manager).ClosedScopes == 0 && host.ShutdownErrors.Count == 0, "Pool shutdown kept a closed mark");
        });

        await Run("pool: lease from a scope during its own clear is rejected and leaves nothing", async () =>
        {
            var manager = StartPools(out var host, out var resources);
            var prefab = new GameObject("Prefab");
            prefab.AddComponent<PoolComponent>();
            var other = new GameObject("Other");
            var scope = resources.CreateScope();
            var item = manager.Get(prefab, null, scope).GetComponent<PoolComponent>();
            manager.Get(other, null, scope);
            Exception nested = null;
            item.ReturnAction = () => nested = CaptureSync(() => manager.Get(other, null, scope));
            manager.ClearScope(scope);
            Check(nested is InvalidOperationException && item.Returns == 1, "Lease during clear was not rejected");
            Check(ScopeState(manager).Pools == 0 && ScopeState(manager).OpenScopes == 0, "Lease during clear left a pool or registration");
            scope.Dispose();
            host.Shutdown();
            Check(host.ShutdownErrors.Count == 0, "Shutdown failed after a rejected lease");
        });

        await Run("pool: invalid cached item is discarded instead of reused", () =>
        {
            int destroyed = 0;
            using var pool = new Pool<Lease>(() => new Lease(), true, 0, 1, destroy: _ => destroyed++, isValid: x => !x.FailGet);
            var item = pool.Get();
            pool.Return(item);
            item.FailGet = true;
            Check(!ReferenceEquals(item, pool.Get()) && destroyed == 1, "Invalid item reused");
            return UniTask.CompletedTask;
        });

        await Run("pool: game object ReturnAll deactivates and reparents every lease", () =>
        {
            var prefab = new GameObject("Prefab");
            prefab.AddComponent<PoolComponent>();
            using var pool = new GameObjectPool(prefab, 0);
            var a = pool.Get(null);
            var b = pool.Get(null);
            pool.ReturnAll();
            Check(pool.ActiveCount == 0 && pool.InactiveCount == 2 && !a.activeSelf && !b.activeSelf, "ReturnAll left active objects");
            Check(a.transform.parent == pool.Root && a.GetComponent<PoolComponent>().Returns == 1, "ReturnAll skipped cleanup");
            return UniTask.CompletedTask;
        });

        await Run("pool: external destroy notification removes borrowed tracking", () =>
        {
            var prefab = new GameObject("Prefab");
            using var pool = new GameObjectPool(prefab, 0);
            var obj = pool.Get(null);
            var item = obj.GetComponent<PooledObject>();
            typeof(PooledObject).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(item, null);
            Check(pool.ActiveCount == 0 && pool.InactiveCount == 0, "Destroyed object still borrowed");
            return UniTask.CompletedTask;
        });
    }
}
