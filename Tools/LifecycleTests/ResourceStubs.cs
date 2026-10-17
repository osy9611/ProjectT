using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace Cysharp.Threading.Tasks
{
    public partial struct UniTask
    {
        public static UniTask Yield(CancellationToken cancellationToken = default) => FakeFrames.Wait(cancellationToken);

        public static UniTask NextFrame(CancellationToken cancellationToken = default) => FakeFrames.Wait(cancellationToken);

        public static async UniTask WaitUntil(Func<bool> predicate, CancellationToken cancellationToken = default)
        {
            while (!predicate())
                await FakeFrames.Wait(cancellationToken);
        }

    }
}

namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { None, Succeeded, Failed }

    public sealed class FakeOperation
    {
        public object Result;
        public Exception Error;
        public bool IsDone;
        public int Releases;
        public bool ThrowOnWait;
        public Action<AsyncOperationHandle> Completed;

        public void Complete(object result = null, Exception error = null)
        {
            Result = result ?? Result;
            Error = error ?? Error;
            IsDone = true;
            Completed?.Invoke(new AsyncOperationHandle(this));
        }
    }

    public struct AsyncOperationHandle
    {
        internal FakeOperation Operation;
        public AsyncOperationHandle(FakeOperation operation) { Operation = operation; }
        public bool IsValid() => Operation != null && Operation.Releases == 0;
        public bool IsDone => Operation.IsDone;
        public object Result => Operation.Result;
        public Exception OperationException => Operation.Error;
        public AsyncOperationStatus Status => !IsDone ? AsyncOperationStatus.None : Operation.Error == null ? AsyncOperationStatus.Succeeded : AsyncOperationStatus.Failed;
        public event Action<AsyncOperationHandle> Completed
        {
            add { Operation.Completed += value; }
            remove { Operation.Completed -= value; }
        }

        public object WaitForCompletion()
        {
            if (Operation.ThrowOnWait)
                throw new InvalidOperationException("Wait failed");
            Operation.Complete();
            return Result;
        }
    }

    public struct AsyncOperationHandle<T>
    {
        internal FakeOperation Operation;
        public AsyncOperationHandle(FakeOperation operation) { Operation = operation; }
        public bool IsValid() => Operation != null && Operation.Releases == 0;
        public bool IsDone => Operation.IsDone;
        public T Result => (T)Operation.Result;
        public float PercentComplete => IsDone ? 1f : 0f;
        public Exception OperationException => Operation.Error;
        public AsyncOperationStatus Status => ((AsyncOperationHandle)this).Status;
        public event Action<AsyncOperationHandle<T>> Completed
        {
            add { var operation = Operation; Operation.Completed += _ => value(new AsyncOperationHandle<T>(operation)); }
            remove { throw new NotSupportedException(); }
        }

        public static implicit operator AsyncOperationHandle(AsyncOperationHandle<T> handle) => new AsyncOperationHandle(handle.Operation);
    }
}

namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode { Single, Additive }
    public struct Scene
    {
        internal SceneData Data;
        public bool IsValid() => Data != null;
        public string name => Data?.Name;
        public bool isLoaded { get => Data != null && Data.Loaded; set => Data.Loaded = value; }
        public int buildIndex { get => Data.Id; set => Data.Id = value; }
        public static bool operator ==(Scene a, Scene b) => ReferenceEquals(a.Data, b.Data);
        public static bool operator !=(Scene a, Scene b) => !(a == b);
        public override bool Equals(object value) => value is Scene scene && this == scene;
        public override int GetHashCode() => Data?.GetHashCode() ?? 0;
    }
    public sealed class SceneData
    {
        public string Name;
        public bool Loaded;
        public int Id;
    }
    public static class SceneManager
    {
        public static Scene Active;
        public static readonly List<Scene> All = new();
        public static int sceneCount => All.FindAll(s => s.isLoaded).Count;
        public static Scene GetSceneAt(int i) => All.FindAll(s => s.isLoaded)[i];
        public static Scene GetActiveScene() => Active;
        public static bool SetActiveScene(Scene scene) { Active = scene; return true; }
        public static Scene CreateScene(string name)
        {
            var scene = new Scene { Data = new SceneData { Name = name, Loaded = true } };
            All.Add(scene);
            return scene;
        }

        public static UnityEngine.AsyncOperation UnloadSceneAsync(Scene scene)
        {
            if (sceneCount <= 1) throw new Exception("Cannot unload last scene");
            scene.isLoaded = false;
            return new UnityEngine.AsyncOperation { isDone = true };
        }
    }
}

namespace UnityEngine.ResourceManagement.ResourceProviders
{
    public struct SceneInstance
    {
        public UnityEngine.SceneManagement.Scene Scene;
    }
}

namespace UnityEngine.AddressableAssets
{
    // Only Addressables/Unity are replaced; ResourceManager, scope, entry and UniTask are production code.
    public static class Addressables
    {
        public static readonly List<FakeOperation> Operations = new List<FakeOperation>();
        public static Func<FakeOperation> Factory;
        public static Action<FakeOperation> Released;

        public static void Reset()
        {
            Operations.Clear();
            Released = null;
            SceneOperations.Clear();
            UnloadOperations.Clear();
            SceneFactory = null;
            UnloadFactory = null;
            FakeFrames.Clear();
            UnityEngine.SceneManagement.SceneManager.Active = new UnityEngine.SceneManagement.Scene { Data = new UnityEngine.SceneManagement.SceneData { Name = "Bootstrap", Loaded = true } };
            UnityEngine.SceneManagement.SceneManager.All.Clear();
            UnityEngine.SceneManagement.SceneManager.All.Add(UnityEngine.SceneManagement.SceneManager.Active);
            UnityEngine.Resources.Reset();
            Factory = () => new FakeOperation { Result = new UnityEngine.Object() };
        }

        public static AsyncOperationHandle<T> LoadAssetAsync<T>(string path)
        {
            var operation = Factory();
            Operations.Add(operation);
            return new AsyncOperationHandle<T>(operation);
        }

        public static void Release(AsyncOperationHandle handle)
        {
            if (++handle.Operation.Releases != 1)
                throw new Exception("Double release");
            Released?.Invoke(handle.Operation);
        }

        public static readonly List<FakeOperation> SceneOperations = new();
        public static readonly List<FakeOperation> UnloadOperations = new();
        public static Func<FakeOperation> SceneFactory;
        public static Func<FakeOperation> UnloadFactory;

        public static AsyncOperationHandle<SceneInstance> LoadSceneAsync(string name, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            var scene = new UnityEngine.SceneManagement.Scene { Data = new UnityEngine.SceneManagement.SceneData { Name = name, Id = SceneOperations.Count + 1 } };
            var operation = SceneFactory?.Invoke() ?? new FakeOperation { IsDone = true };
            operation.Result = new SceneInstance { Scene = scene };
            void Loaded(AsyncOperationHandle _)
            {
                if (operation.Error != null) return;
                if (mode == UnityEngine.SceneManagement.LoadSceneMode.Single)
                {
                    foreach (var previous in SceneOperations)
                    {
                        var previousScene = ((SceneInstance)previous.Result).Scene;
                        if (previousScene.isLoaded)
                        {
                            previousScene.isLoaded = false;
                            if (previous.Releases == 0) Release(new AsyncOperationHandle(previous));
                        }
                    }
                    var active = UnityEngine.SceneManagement.SceneManager.Active;
                    if (active.IsValid()) active.isLoaded = false;
                    UnityEngine.SceneManagement.SceneManager.Active = scene;
                }
                scene.isLoaded = true;
                UnityEngine.SceneManagement.SceneManager.All.Add(scene);
            }
            if (operation.IsDone) Loaded(default);
            else operation.Completed += Loaded;
            SceneOperations.Add(operation);
            return new AsyncOperationHandle<SceneInstance>(operation);
        }

        public static AsyncOperationHandle<SceneInstance> UnloadSceneAsync(AsyncOperationHandle<SceneInstance> handle) => UnloadSceneAsync(handle.Result, true);

        public static AsyncOperationHandle<SceneInstance> UnloadSceneAsync(SceneInstance scene, bool autoReleaseHandle)
        {
            var operation = UnloadFactory?.Invoke() ?? new FakeOperation { IsDone = true };
            operation.Result = scene;
            void Unloaded(AsyncOperationHandle _)
            {
                if (operation.Error == null)
                {
                    scene.Scene.isLoaded = false;
                    var load = SceneOperations.Find(x => ((SceneInstance)x.Result).Scene == scene.Scene);
                    if (load != null && load.Releases == 0) Release(new AsyncOperationHandle(load));
                }
                if (autoReleaseHandle) Release(new AsyncOperationHandle(operation));
            }
            UnloadOperations.Add(operation);
            if (operation.IsDone) Unloaded(default);
            else operation.Completed += Unloaded;
            return new AsyncOperationHandle<SceneInstance>(operation);
        }
    }
}

namespace UnityEngine
{
    public class AsyncOperation
    {
        public bool isDone;
        public event Action<AsyncOperation> completed;

        public void Complete()
        {
            isDone = true;
            completed?.Invoke(this);
        }
    }

    public class ResourceRequest : AsyncOperation
    {
        public Object asset;
    }

    public static class Resources
    {
        public static readonly List<ResourceRequest> Requests = new();
        public static Func<string, Type, Object> Factory;
        public static Func<AsyncOperation> Sweep;
        public static int Loads, Sweeps;

        public static void Reset()
        {
            Requests.Clear();
            Factory = (_, _) => new Object();
            Sweep = () => new AsyncOperation { isDone = true };
            Loads = Sweeps = 0;
        }

        public static Object Load(string path, Type type)
        {
            Loads++;
            return Factory(path, type);
        }

        public static ResourceRequest LoadAsync(string path, Type type)
        {
            var request = new ResourceRequest { asset = Factory(path, type) };
            Requests.Add(request);
            return request;
        }

        public static AsyncOperation UnloadUnusedAssets()
        {
            Sweeps++;
            return Sweep();
        }
    }
}

namespace UnityEngine
{
    public sealed class Sprite : Object { }
}

namespace UnityEngine.U2D
{
    public sealed class SpriteAtlas : UnityEngine.Object
    {
        public int Calls;
        public bool Missing;

        public UnityEngine.Sprite GetSprite(string name)
        {
            Calls++;
            return Missing ? null : new UnityEngine.Sprite();
        }
    }
}
