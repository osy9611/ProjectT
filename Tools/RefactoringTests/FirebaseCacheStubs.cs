using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

// Firebase SDK와 Addressables는 이 저장소에 소스가 없어 테스트가 URL 응답 시점을 정하는 대체물로 둔다.
internal static class FirebaseBoundary
{
    internal sealed class Fetch
    {
        internal string Url;
        internal TaskCompletionSource<Uri> Source = new TaskCompletionSource<Uri>();
    }

    internal sealed class Location : IResourceLocation
    {
        public string InternalId { get; set; }
        public string ProviderId { get; set; }
        public Type ResourceType { get; set; }
        public object Data { get; set; }
        public string PrimaryKey { get; set; }
        public IList<IResourceLocation> Dependencies { get; set; } = new List<IResourceLocation>();
    }

    // 프로바이더가 받은 요청 하나다. Complete 결과와 기본 프로바이더로 넘겼는지를 기록한다.
    internal sealed class Request
    {
        internal IResourceLocation Location;
        internal Type RequestedType;
        internal int CompleteCount;
        internal bool Status;
        internal object Result;
        internal Exception Exception;
        internal bool BaseProvided;
        internal string BaseUrl;
        internal string CacheKey;
    }

    internal sealed class Provision
    {
        internal IResourceLocation Location;
        internal Type Type;
        internal Action<object> Complete;
        internal Action<Exception> Fail;
        internal Func<bool> Released;
    }

    private sealed class Locator : IResourceLocator
    {
        public bool Locate(object key, Type type, out IList<IResourceLocation> locations)
        {
            return Keys.TryGetValue((string)key, out locations);
        }
    }

    private sealed class MainThreadContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object state)
        {
            FirebaseBoundary.Post(() => callback(state));
        }
    }

    internal static readonly Dictionary<string, IList<IResourceLocation>> Keys = new Dictionary<string, IList<IResourceLocation>>();
    internal static readonly List<IResourceLocator> Locators = new List<IResourceLocator> { new Locator() };
    internal static readonly List<Fetch> Fetches = new List<Fetch>();
    internal static readonly List<Provision> Provided = new List<Provision>();
    private static readonly Queue<Action> mainThread = new Queue<Action>();

    internal static void Reset()
    {
        if (mainThread.Count != 0)
            throw new InvalidOperationException("Previous scenario left a pending main-thread continuation.");

        Keys.Clear();
        Fetches.Clear();
        Provided.Clear();
        UnityEngine.Debug.Errors.Clear();
    }

    // 실제 앱처럼 FirebaseAddressablesManager.InitializeAsync로 프로바이더와 주소 변환을 등록한다.
    internal static void Initialize()
    {
        SynchronizationContext.SetSynchronizationContext(new MainThreadContext());
        var initialize = ProjectT.Addressable.FirebaseAddressablesManager.InitializeAsync(CancellationToken.None);
        RunMainThread();
        SynchronizationContext.SetSynchronizationContext(null);
        if (initialize.Status != UniTaskStatus.Succeeded)
            throw new InvalidOperationException("Firebase initialization did not complete: " + initialize.Status);
    }

    internal static void AddBundle(string key, params string[] dependencies)
    {
        var bundle = new Location { InternalId = key };
        foreach (var dependency in dependencies)
            bundle.Dependencies.Add(new Location { InternalId = dependency });
        Keys[key] = new List<IResourceLocation> { bundle };
    }

    internal static void Post(Action continuation)
    {
        mainThread.Enqueue(continuation);
    }

    internal static void ResolvePending()
    {
        foreach (var fetch in Fetches)
        {
            if (!fetch.Source.Task.IsCompleted)
                fetch.Source.SetResult(new Uri("https://storage.test/" + fetch.Url.Substring("gs://".Length)));
        }
        RunMainThread();
    }

    internal static void RunMainThread()
    {
        while (mainThread.Count != 0)
            mainThread.Dequeue()();
    }
}

namespace Firebase
{
    public enum DependencyStatus { Available, UnavailableOther }

    public sealed class FirebaseApp
    {
        public static Task<DependencyStatus> CheckAndFixDependenciesAsync() { return Task.FromResult(DependencyStatus.Available); }
    }
}

namespace Firebase.Extensions
{
    public static class TaskExtension
    {
        // 실제 SDK도 완료된 작업의 후속 처리를 다음 메인 스레드 갱신으로 넘긴다.
        public static Task ContinueWithOnMainThread<T>(this Task<T> task, Action<Task<T>> continuation)
        {
            return task.ContinueWith(completed => FirebaseBoundary.Post(() => continuation(completed)), TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}

namespace Firebase.Storage
{
    public sealed class FirebaseStorage
    {
        public static FirebaseStorage DefaultInstance { get; } = new FirebaseStorage();
        public StorageReference GetReferenceFromUrl(string url) { return new StorageReference(url); }
    }

    public sealed class StorageReference
    {
        private readonly string url;
        public StorageReference(string url) { this.url = url; }

        public Task<Uri> GetDownloadUrlAsync()
        {
            var fetch = new FirebaseBoundary.Fetch { Url = url };
            FirebaseBoundary.Fetches.Add(fetch);
            return fetch.Source.Task;
        }
    }
}

namespace Cysharp.Threading.Tasks
{
    public static class AddressablesAsyncExtensions
    {
        public static UniTask<T> ToUniTask<T>(this AsyncOperationHandle<T> handle, CancellationToken cancellationToken = default)
        {
            return UniTask.FromResult(handle.Result);
        }
    }
}

namespace UnityEngine.ResourceManagement.ResourceLocations
{
    public interface IResourceLocation
    {
        string InternalId { get; }
        string ProviderId { get; }
        Type ResourceType { get; }
        object Data { get; }
        string PrimaryKey { get; }
        IList<IResourceLocation> Dependencies { get; }
    }

    public class ResourceLocationBase : IResourceLocation
    {
        public ResourceLocationBase(string name, string id, string providerId, Type t, params IResourceLocation[] dependencies)
        {
            InternalId = id;
            ProviderId = providerId;
            ResourceType = t;
            Dependencies = new List<IResourceLocation>(dependencies);
        }

        public string InternalId { get; }
        public string ProviderId { get; }
        public Type ResourceType { get; }
        public object Data { get; set; }
        public string PrimaryKey { get; set; }
        public IList<IResourceLocation> Dependencies { get; }
    }
}

namespace UnityEngine.AddressableAssets.ResourceLocators
{
    public interface IResourceLocator
    {
        bool Locate(object key, Type type, out IList<IResourceLocation> locations);
    }

    public class ContentCatalogData
    {
    }
}

namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { None, Succeeded, Failed }

    public sealed class Operation<T>
    {
        internal bool IsDone;
        internal T Result;
        internal Exception Error;
        internal bool Released;
        internal event Action<AsyncOperationHandle<T>> Completed;

        internal void Complete(T result)
        {
            IsDone = true;
            Result = result;
            Completed?.Invoke(new AsyncOperationHandle<T>(this));
        }

        internal void Fail(Exception error)
        {
            IsDone = true;
            Error = error;
            Completed?.Invoke(new AsyncOperationHandle<T>(this));
        }
    }

    public struct AsyncOperationHandle<T>
    {
        private readonly Operation<T> operation;

        internal AsyncOperationHandle(Operation<T> operation) { this.operation = operation; }

        public bool IsDone => operation.IsDone;
        public T Result => operation.Result;
        public AsyncOperationStatus Status => !operation.IsDone ? AsyncOperationStatus.None : operation.Error == null ? AsyncOperationStatus.Succeeded : AsyncOperationStatus.Failed;
        public Exception OperationException => operation.Error;
        public bool IsValid() => operation != null;
        public void Release() { operation.Released = true; }

        public event Action<AsyncOperationHandle<T>> Completed
        {
            add
            {
                if (operation.IsDone)
                    throw new InvalidOperationException("Completed is subscribed after the operation finished in this fixture.");
                operation.Completed += value;
            }
            remove { operation.Completed -= value; }
        }
    }
}

namespace UnityEngine.ResourceManagement.ResourceProviders
{
    public interface IResourceProvider
    {
        string ProviderId { get; }
    }

    public interface IAssetBundleResource
    {
    }

    public struct ProvideHandle
    {
        internal readonly FirebaseBoundary.Request Request;

        internal ProvideHandle(FirebaseBoundary.Request request) { Request = request; }

        public IResourceLocation Location => Request.Location;
        public ResourceManager ResourceManager => AddressableAssets.Addressables.ResourceManager;

        // 실제 ProviderOperation.ProviderCompleted처럼 요청 타입으로 바꿀 수 없는 결과는 원래 예외 대신 변환 오류로 실패하고 throw한다.
        public void Complete<T>(T result, bool status, Exception exception)
        {
            Request.CompleteCount++;
            if (result != null && typeof(T) != Request.RequestedType && !Request.RequestedType.IsAssignableFrom(result.GetType()))
            {
                string message = "Provider has provided a result of type " + typeof(T) + " which cannot be converted to requested type " + Request.RequestedType + ".";
                Request.Status = false;
                Request.Exception = new Exception(message);
                throw new Exception(message);
            }

            Request.Status = status;
            Request.Result = result;
            Request.Exception = exception;
        }
    }

    public abstract class ResourceProviderBase : IResourceProvider
    {
        public virtual string ProviderId => GetType().FullName;

        public abstract void Provide(ProvideHandle provideHandle);

        public virtual void Release(IResourceLocation location, object asset)
        {
        }
    }

    public class AssetBundleProvider : ResourceProviderBase
    {
        // 실제 AssetBundleResource도 TransformInternalId 주소로 웹 요청을 만들고 보내기 전에 WebRequestOverride를 부른다.
        public override void Provide(ProvideHandle provideHandle)
        {
            var request = new Networking.UnityWebRequest(provideHandle.ResourceManager.TransformInternalId(provideHandle.Location));
            provideHandle.ResourceManager.WebRequestOverride?.Invoke(request);
            provideHandle.Request.BaseProvided = true;
            provideHandle.Request.BaseUrl = request.url;
        }
    }

    public class TextDataProvider : ResourceProviderBase
    {
        public override void Provide(ProvideHandle provideHandle)
        {
            provideHandle.Request.BaseProvided = true;
        }
    }

    public class JsonAssetProvider : TextDataProvider
    {
    }
}

namespace UnityEngine.ResourceManagement
{
    public sealed class ResourceManager
    {
        public readonly List<ResourceProviders.IResourceProvider> ResourceProviders = new List<ResourceProviders.IResourceProvider>();
        public Func<IResourceLocation, string> InternalIdTransformFunc;
        public Action<Networking.UnityWebRequest> WebRequestOverride { get; set; }

        public string TransformInternalId(IResourceLocation location)
        {
            return InternalIdTransformFunc == null ? location.InternalId : InternalIdTransformFunc(location);
        }

        public AsyncOperationHandle<T> ProvideResource<T>(IResourceLocation location)
        {
            var operation = new Operation<T>();
            FirebaseBoundary.Provided.Add(new FirebaseBoundary.Provision { Location = location, Type = typeof(T), Complete = result => operation.Complete((T)result), Fail = operation.Fail, Released = () => operation.Released });
            return new AsyncOperationHandle<T>(operation);
        }
    }
}

namespace UnityEngine.AddressableAssets
{
    public static class Addressables
    {
        private static readonly Operation<IResourceLocator> initialized = CreateInitialized();

        public static ResourceManagement.ResourceManager ResourceManager { get; } = new ResourceManagement.ResourceManager();
        public static IEnumerable<IResourceLocator> ResourceLocators => FirebaseBoundary.Locators;

        public static Func<IResourceLocation, string> InternalIdTransformFunc
        {
            get => ResourceManager.InternalIdTransformFunc;
            set => ResourceManager.InternalIdTransformFunc = value;
        }

        public static Action<Networking.UnityWebRequest> WebRequestOverride
        {
            get => ResourceManager.WebRequestOverride;
            set => ResourceManager.WebRequestOverride = value;
        }

        public static AsyncOperationHandle<IResourceLocator> InitializeAsync() => new AsyncOperationHandle<IResourceLocator>(initialized);

        private static Operation<IResourceLocator> CreateInitialized()
        {
            var operation = new Operation<IResourceLocator>();
            operation.Complete(FirebaseBoundary.Locators[0]);
            return operation;
        }
    }
}

namespace UnityEngine.Networking
{
    public class UnityWebRequest
    {
        public UnityWebRequest(string url) { this.url = url; }

        public string url { get; set; }
    }
}
