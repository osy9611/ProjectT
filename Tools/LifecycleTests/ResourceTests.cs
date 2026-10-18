using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Asset = UnityEngine.Object;

static partial class Program
{
    static ResourceManager StartResources(out ManagerHost host)
    {
        Addressables.Reset();
        host = new ManagerHost(); Global.BindForTests(host);
        var resource = host.Register(new ResourceManager());
        host.InitializeAsync().GetAwaiter().GetResult();
        return resource;
    }

    static async UniTask RunResourceTests()
    {
        await Run("scenes: application exit skips unloading loaded scenes while normal shutdown unloads them", async () =>
        {
            var resource = StartResources(out var host);
            await resource.LoadSceneAsync("Exit", UnityEngine.SceneManagement.LoadSceneMode.Single);
            host.Shutdown(ShutdownReason.ApplicationExit);
            Check(Addressables.UnloadOperations.Count == 0 && host.ShutdownErrors.Count == 0, "Application exit unloaded the last scene");

            resource = StartResources(out host);
            await resource.LoadSceneAsync("Normal", UnityEngine.SceneManagement.LoadSceneMode.Single);
            host.Shutdown();
            Check(Addressables.UnloadOperations.Count == 1 && host.ShutdownErrors.Count == 0, "Normal shutdown skipped scene unload");
        });
        await Run("sprites: reuse per atlas and destroy after last owner before atlas release", () =>
        {
            var resource = StartResources(out var host);
            var atlas = new UnityEngine.U2D.SpriteAtlas();
            Addressables.Factory = () => new FakeOperation { Result = atlas, IsDone = true };
            var firstScope = resource.CreateScope();
            var secondScope = resource.CreateScope();
            var sprite = resource.GetSprite("atlas", "icon", scope: firstScope);
            Check(ReferenceEquals(sprite, resource.GetSprite("atlas", "icon", scope: secondScope)) && atlas.Calls == 1, "Sprite was cloned again");
            firstScope.Dispose();
            Check(!UnityEngine.Object.Destroyed.Contains(sprite) && Addressables.Operations[0].Releases == 0, "Shared sprite destroyed");
            Addressables.Released = _ => Check(UnityEngine.Object.Destroyed.Contains(sprite), "Atlas released before sprite cleanup");
            secondScope.Dispose();
            secondScope.Dispose();
            Check(UnityEngine.Object.Destroyed.FindAll(x => ReferenceEquals(x, sprite)).Count == 1 && Addressables.Operations[0].Releases == 1, "Duplicate/missing release");
            host.Shutdown();
            return UniTask.CompletedTask;
        });

        await Run("sprites: app ownership survives scene reset while scene sprite is destroyed", () =>
        {
            var resource = StartResources(out var host);
            Addressables.Factory = () => new FakeOperation { Result = new UnityEngine.U2D.SpriteAtlas(), IsDone = true };
            var remote = resource.GetSprite("atlas", "icon", scope: resource.AppScope);
            var scene = resource.GetSprite("scene-atlas", "icon");
            resource.ReleaseAll();
            Check(UnityEngine.Object.Destroyed.Contains(scene) && !UnityEngine.Object.Destroyed.Contains(remote), "Lifetime isolation failed");
            Check(ReferenceEquals(remote, resource.GetSprite("atlas", "icon", scope: resource.AppScope)), "App cache lost");
            host.Shutdown();
            Check(UnityEngine.Object.Destroyed.Contains(remote), "App sprite leaked");
            return UniTask.CompletedTask;
        });

        await Run("sprites: missing name is not cached and different names are separate", () =>
        {
            var resource = StartResources(out var host);
            var atlas = new UnityEngine.U2D.SpriteAtlas { Missing = true };
            Addressables.Factory = () => new FakeOperation { Result = atlas, IsDone = true };
            Check(resource.GetSprite("atlas", "icon") == null, "Missing sprite succeeded");
            atlas.Missing = false;
            var icon = resource.GetSprite("atlas", "icon");
            Check(icon != null && !ReferenceEquals(icon, resource.GetSprite("atlas", "other")), "Missing result cached or name collision");
            host.Shutdown();
            return UniTask.CompletedTask;
        });

        await Run("sprites: canceled scene waiter cannot create sprite while app waiter survives", async () =>
        {
            var resource = StartResources(out var host);
            var atlas = new UnityEngine.U2D.SpriteAtlas();
            Addressables.Factory = () => new FakeOperation { Result = atlas };
            var scene = resource.GetSpriteAsync("atlas", "scene");
            var app = resource.GetSpriteAsync("atlas", "app", scope: resource.AppScope);
            resource.ReleaseAll();
            await Throws<OperationCanceledException>(async () => { await scene; });
            Addressables.Operations[0].Complete();
            var sprite = await app;
            Check(sprite != null && atlas.Calls == 1 && Addressables.Operations.Count == 1, "Canceled request cloned or load duplicated");
            host.Shutdown();
        });

        await Run("preload: normal request shares handle and scope release owns cleanup", async () =>
        {
            var resource = StartResources(out var host);
            var scope = resource.CreateScope();
            var preload = resource.PreloadAsync<Asset>("asset", scope: scope);
            var loading = resource.LoadAndGetAsync<Asset>("asset", scope: scope);
            Check(Addressables.Operations.Count == 1, "Preload created separate handle");
            Addressables.Operations[0].Complete();
            await preload;
            Check(ReferenceEquals(await loading, resource.LoadAndGet<Asset>("asset", scope: scope)), "Preload cache not reused");
            scope.Dispose();
            Check(Addressables.Operations[0].Releases == 1, "Preload leaked handle");
            host.Shutdown();
        });

        await Run("preload: failure releases handle and allows retry", async () =>
        {
            var resource = StartResources(out var host);
            Addressables.Factory = () => new FakeOperation { IsDone = true, Error = new InvalidOperationException("Load") };
            await Throws<InvalidOperationException>(() => resource.PreloadAsync<Asset>("asset"));
            Check(Addressables.Operations[0].Releases == 1, "Failed preload leaked");
            Addressables.Factory = () => new FakeOperation { IsDone = true, Result = new Asset() };
            await resource.PreloadAsync<Asset>("asset");
            host.Shutdown();
        });

        await Run("resources: throwing load does not leave pending cache entry", () =>
        {
            var resource = StartResources(out var host);
            Addressables.Factory = () => throw new InvalidOperationException("Load");
            try { resource.LoadAndGet<Asset>("asset"); throw new Exception("Load should fail"); }
            catch (InvalidOperationException) { }
            Addressables.Factory = () => new FakeOperation { IsDone = true, Result = new Asset() };
            Check(resource.LoadAndGet<Asset>("asset") != null, "Retry failed");
            host.Shutdown();
            return UniTask.CompletedTask;
        });

        await Run("resources: concurrent requests share one pending handle", async () =>
        {
            var resource = StartResources(out var host);
            var first = resource.LoadAndGetAsync<Asset>("shared");
            var second = resource.LoadAndGetAsync<Asset>("shared");
            Check(Addressables.Operations.Count == 1, "Duplicate load");
            Addressables.Operations[0].Complete();
            Check(ReferenceEquals(await first, await second), "Different result");
            host.Shutdown();
            Check(Addressables.Operations[0].Releases == 1, "Handle not released exactly once");
        });

        await Run("resources: individual cancellation only cancels the waiter", async () =>
        {
            var resource = StartResources(out var host);
            using var cancel = new CancellationTokenSource();
            var first = resource.LoadAndGetAsync<Asset>("shared", cancelToken: cancel.Token);
            var second = resource.LoadAndGetAsync<Asset>("shared");
            cancel.Cancel();
            await Throws<OperationCanceledException>(async () => { await first; });
            Check(Addressables.Operations[0].Releases == 0, "Canceled waiter released shared handle");
            Addressables.Operations[0].Complete();
            await second;
            host.Shutdown();
        });

        await Run("resources: scene reset cancels its waiter while app request survives", async () =>
        {
            var resource = StartResources(out var host);
            var scene = resource.LoadAndGetAsync<Asset>("shared");
            var app = resource.LoadAndGetAsync<Asset>("shared", scope: resource.AppScope);
            resource.ReleaseAll();
            await Throws<OperationCanceledException>(async () => { await scene; });
            Check(Addressables.Operations[0].Releases == 0, "App ownership lost");
            Addressables.Operations[0].Complete();
            await app;
            host.Shutdown();
            Check(Addressables.Operations[0].Releases == 1, "App handle leaked");
        });

        await Run("resources: released pending operation cannot replace new scene cache", async () =>
        {
            var resource = StartResources(out var host);
            var previous = resource.LoadAndGetAsync<Asset>("scene");
            var oldOperation = Addressables.Operations[0];
            resource.ReleaseAll();
            await Throws<OperationCanceledException>(async () => { await previous; });
            var next = resource.LoadAndGetAsync<Asset>("scene");
            oldOperation.Complete();
            Addressables.Operations[1].Complete();
            var nextAsset = await next;
            Check(ReferenceEquals(nextAsset, resource.LoadAndGet<Asset>("scene")), "Stale cache");
            Check(Addressables.Operations.Count == 2 && oldOperation.Releases == 1, "Invalid release/reload");
            host.Shutdown();
        });

        await Run("resources: explicit scopes retain independently and dispose idempotently", () =>
        {
            var resource = StartResources(out var host);
            var first = resource.CreateScope();
            var second = resource.CreateScope();
            resource.LoadAndGet<Asset>("shared", scope: first);
            resource.LoadAndGet<Asset>("shared", scope: second);
            first.Dispose();
            first.Dispose();
            resource.ReleaseAll();
            Check(Addressables.Operations[0].Releases == 0, "Explicit scope released by scene reset");
            second.Dispose();
            Check(Addressables.Operations[0].Releases == 1, "Last scope did not release");
            host.Shutdown();
            return UniTask.CompletedTask;
        });

        await Run("resources: a resource released with its last scope stays done and rejects reads as disposed", async () =>
        {
            var resource = StartResources(out var host);
            var scope = resource.CreateScope();
            var loaded = resource.LoadResource<ProjectT.Addressable.Resource<Asset>>("held", scope);
            loaded.GetResult<Asset>();
            scope.Dispose();
            await ThrowsSync<ObjectDisposedException>(() => loaded.GetResult<Asset>());
            Check(loaded.IsDone, "Released resource stopped reporting completion");
            host.Shutdown();
        });

        await Run("resources: cache hit can add app ownership", () =>
        {
            var resource = StartResources(out var host);
            var asset = resource.LoadAndGet<Asset>("shared");
            resource.LoadAndGet<Asset>("shared", scope: resource.AppScope);
            resource.ReleaseAll();
            Check(Addressables.Operations[0].Releases == 0, "Cache hit ignored app scope");
            Check(ReferenceEquals(asset, resource.LoadAndGet<Asset>("shared", scope: resource.AppScope)), "Cache changed");
            host.Shutdown();
            return UniTask.CompletedTask;
        });

        await Run("resources: failed shared load releases once and permits retry", async () =>
        {
            var resource = StartResources(out var host);
            var first = resource.LoadAndGetAsync<Asset>("failed");
            var second = resource.LoadAndGetAsync<Asset>("failed");
            Addressables.Operations[0].Complete(error: new InvalidOperationException("Load failed"));
            await Throws<InvalidOperationException>(async () => { await first; });
            await Throws<InvalidOperationException>(async () => { await second; });
            Check(Addressables.Operations[0].Releases == 1, "Failed handle leaked");
            resource.LoadAndGet<Asset>("failed");
            Check(Addressables.Operations.Count == 2, "Failure cached");
            host.Shutdown();
        });

        await Run("resources: synchronous request joins pending async load", async () =>
        {
            var resource = StartResources(out var host);
            var pending = resource.LoadAndGetAsync<Asset>("shared");
            var result = resource.LoadAndGet<Asset>("shared");
            Check(ReferenceEquals(result, await pending) && Addressables.Operations.Count == 1, "Sync request duplicated pending load");
            host.Shutdown();
        });

        await Run("resources: exception during synchronous wait also settles existing async waiters", async () =>
        {
            var resource = StartResources(out var host);
            var pending = resource.LoadAndGetAsync<Asset>("asset");
            Addressables.Operations[0].ThrowOnWait = true;
            await ThrowsSync<InvalidOperationException>(() => resource.LoadAndGet<Asset>("asset"));
            await Throws<InvalidOperationException>(async () => { await pending; });
            Check(Addressables.Operations[0].Releases == 1, "Wait failure leaked handle");
            resource.LoadAndGet<Asset>("asset");
            Check(Addressables.Operations.Count == 2, "Wait failure cached");
            host.Shutdown();
        });

        await Run("resources: completed failures throw consistently in sync and async APIs", async () =>
        {
            var resource = StartResources(out var host);
            Addressables.Factory = () => new FakeOperation { IsDone = true, Error = new InvalidOperationException("Failed") };
            await ThrowsSync<InvalidOperationException>(() => resource.LoadAndGet<Asset>("failed"));
            await Throws<InvalidOperationException>(async () => { await resource.LoadAndGetAsync<Asset>("failed"); });
            Check(Addressables.Operations.Count == 2 && Addressables.Operations.TrueForAll(x => x.Releases == 1), "Immediate failure handles leaked");
            host.Shutdown();
        });

        await Run("resources: foreign/disposed scopes, empty keys and incompatible types are rejected", async () =>
        {
            var resource = StartResources(out var host);
            var otherHost = new ManagerHost();
            var other = otherHost.Register(new ResourceManager());
            await otherHost.InitializeAsync();
            var scope = resource.CreateScope();
            await ThrowsSync<InvalidOperationException>(() => other.LoadAndGet<Asset>("asset", scope: scope));
            scope.Dispose();
            await ThrowsSync<InvalidOperationException>(() => resource.LoadAndGet<Asset>("asset", scope: scope));
            await ThrowsSync<ArgumentException>(() => resource.LoadAndGet<Asset>(" "));
            resource.LoadAndGet<Asset>("asset");
            await ThrowsSync<InvalidOperationException>(() => resource.LoadAndGet<string>("asset"));
            Check(Addressables.Operations.Count == 1, "Rejected call loaded a handle");
            host.Shutdown();
            otherHost.Shutdown();
        });

        await Run("resources: shutdown cancels pending scopes and forbids subsequent loads", async () =>
        {
            var resource = StartResources(out var host);
            var scope = resource.CreateScope();
            var pending = resource.LoadAndGetAsync<Asset>("asset", scope: scope);
            host.Shutdown();
            await Throws<OperationCanceledException>(async () => { await pending; });
            Addressables.Operations[0].Complete();
            Check(Addressables.Operations[0].Releases == 1, "Shutdown leaked/doubled pending handle");
            await ThrowsSync<OperationCanceledException>(() => resource.LoadAndGet<Asset>("asset"));
            scope.Dispose();
        });

        await Run("resources: already canceled request does not start or retain a load", async () =>
        {
            var resource = StartResources(out var host);
            await Throws<OperationCanceledException>(async () => { await resource.LoadAndGetAsync<Asset>("asset", cancelToken: new CancellationToken(true)); });
            Check(Addressables.Operations.Count == 0, "Canceled request started loading");
            host.Shutdown();
        });
    }
}
