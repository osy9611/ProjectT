using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        public IList<IResourceLocation> Dependencies { get; set; } = new List<IResourceLocation>();
    }

    private sealed class Locator : IResourceLocator
    {
        public bool Locate(object key, Type type, out IList<IResourceLocation> locations)
        {
            return Keys.TryGetValue((string)key, out locations);
        }
    }

    internal static readonly Dictionary<string, IList<IResourceLocation>> Keys = new Dictionary<string, IList<IResourceLocation>>();
    internal static readonly List<IResourceLocator> Locators = new List<IResourceLocator> { new Locator() };
    internal static readonly List<Fetch> Fetches = new List<Fetch>();
    private static readonly Queue<Action> mainThread = new Queue<Action>();

    internal static void Reset()
    {
        if (mainThread.Count != 0)
            throw new InvalidOperationException("Previous scenario left a pending main-thread continuation.");

        Keys.Clear();
        Fetches.Clear();
        UnityEngine.Debug.Errors.Clear();
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

namespace ProjectT.Addressable
{
    // 실제 클래스는 FirebaseApp과 프로바이더 등록에 의존한다. PreWarm이 읽는 준비 상태와 gs:// 판별만 남긴다.
    public static class FirebaseAddressablesManager
    {
        public static bool IsFirebaseSetupFinished => true;
        public static event Action FirebaseSetupFinished { add { throw new InvalidOperationException("Setup is finished in this fixture."); } remove { } }
        public static bool IsFirebaseStorageLocation(string internalId) => internalId.StartsWith("gs://");
    }
}

namespace UnityEngine.ResourceManagement.ResourceLocations
{
    public interface IResourceLocation
    {
        string InternalId { get; }
        IList<IResourceLocation> Dependencies { get; }
    }
}

namespace UnityEngine.AddressableAssets.ResourceLocators
{
    public interface IResourceLocator
    {
        bool Locate(object key, Type type, out IList<IResourceLocation> locations);
    }
}

namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public struct AsyncOperationHandle<T>
    {
        public bool IsDone => true;
        public event Action<AsyncOperationHandle<T>> Completed { add { throw new InvalidOperationException("Addressables is initialized in this fixture."); } remove { } }
    }
}

namespace UnityEngine.ResourceManagement
{
    public sealed class ResourceManager
    {
        public string TransformInternalId(IResourceLocation location) => location.InternalId;
    }
}

namespace UnityEngine.AddressableAssets
{
    public static class Addressables
    {
        public static ResourceManagement.ResourceManager ResourceManager { get; } = new ResourceManagement.ResourceManager();
        public static IEnumerable<IResourceLocator> ResourceLocators => FirebaseBoundary.Locators;
        public static AsyncOperationHandle<IResourceLocator> InitializeAsync() => new AsyncOperationHandle<IResourceLocator>();
    }
}
