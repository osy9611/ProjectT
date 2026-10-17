using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

internal static class DownloadBoundary
{
    internal sealed class Operation
    {
        internal string Name;
        internal int Frames;
        internal int Frame;
        internal int Reads;
        internal int Releases;
        internal bool Failed;
        internal Exception Error;
        internal object Result;
        internal bool IsDone => Frame >= Frames;
    }

    private sealed class Wait
    {
        internal UniTaskCompletionSource Source = new UniTaskCompletionSource();
        internal CancellationToken Token;
        internal bool IsYield;
    }

    internal static readonly List<string> Trace = new List<string>();
    internal static readonly Queue<Operation> Downloads = new Queue<Operation>();
    internal static readonly List<Operation> Started = new List<Operation>();
    private static readonly List<Wait> waits = new List<Wait>();
    internal static Operation Locations;
    internal static int Delays;
    internal static int Yields;
    internal static int PendingCount => waits.Count;

    internal static void Reset()
    {
        if (waits.Count != 0)
            throw new InvalidOperationException("Previous scenario left a pending wait.");

        Trace.Clear();
        Downloads.Clear();
        Started.Clear();
        Delays = 0;
        Yields = 0;
        Locations = new Operation { Name = "locations", Result = new List<IResourceLocation>() };
    }

    internal static Operation Enqueue(int frames, Exception error = null, bool failed = false)
    {
        var operation = new Operation { Name = "download" + (Downloads.Count + 1), Frames = frames, Error = error, Failed = failed || error != null };
        Downloads.Enqueue(operation);
        return operation;
    }

    internal static UniTask Schedule(bool isYield, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return UniTask.FromCanceled(token);

        var wait = new Wait { Token = token, IsYield = isYield };
        waits.Add(wait);
        return wait.Source.Task;
    }

    internal static void Step()
    {
        if (waits.Count == 0)
            throw new InvalidOperationException("Pending download has no scheduled wait.");

        var snapshot = waits.ToArray();
        waits.Clear();
        foreach (var wait in snapshot)
        {
            if (wait.Token.IsCancellationRequested)
            {
                wait.Source.TrySetCanceled(wait.Token);
                continue;
            }

            if (wait.IsYield)
                Started[Started.Count - 1].Frame++;

            wait.Source.TrySetResult();
        }
    }

    internal static void Release(Operation operation)
    {
        operation.Releases++;
        if (operation.Releases != 1)
            throw new InvalidOperationException("Handle released more than once: " + operation.Name);

        Trace.Add("release:" + operation.Name);
    }
}

namespace Cysharp.Threading.Tasks
{
    public enum PlayerLoopTiming { Update, LastPostLateUpdate }

    public readonly partial struct UniTask
    {
        public static UniTask Yield(PlayerLoopTiming timing, CancellationToken cancellationToken)
        {
            if (timing != PlayerLoopTiming.LastPostLateUpdate)
                throw new InvalidOperationException("Unexpected download loop timing.");

            DownloadBoundary.Yields++;
            DownloadBoundary.Trace.Add("yield:" + timing);
            return DownloadBoundary.Schedule(true, cancellationToken);
        }

        public static UniTask Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (delay != TimeSpan.FromSeconds(1))
                throw new InvalidOperationException("Unexpected retry delay.");

            DownloadBoundary.Delays++;
            DownloadBoundary.Trace.Add("delay:1s");
            return DownloadBoundary.Schedule(false, cancellationToken);
        }
    }

    public static class DownloadHandleExtensions
    {
        public static UniTask<T> ToUniTask<T>(this AsyncOperationHandle<T> handle, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return UniTask.FromCanceled<T>(cancellationToken);

            return UniTask.FromResult(handle.Result);
        }
    }
}

namespace UnityEngine
{
    public static class JsonUtility
    {
        public static string ToJson(object value) => throw new NotSupportedException("Serialization is outside this fixture.");
    }
}

namespace UnityEngine.ResourceManagement.ResourceLocations
{
    public interface IResourceLocation { }
}

namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { None, Succeeded, Failed }

    public struct DownloadStatus
    {
        public long TotalBytes;
        public long DownloadedBytes;
        public float Percent => TotalBytes == 0 ? 1f : (float)DownloadedBytes / TotalBytes;
    }

    public struct AsyncOperationHandle
    {
        internal DownloadBoundary.Operation Operation;
        public bool IsDone => Operation.IsDone;
        public bool IsValid() => Operation != null && Operation.Releases == 0;
        public Exception OperationException => Operation.Error;
        public AsyncOperationStatus Status => !IsDone ? AsyncOperationStatus.None : Operation.Failed ? AsyncOperationStatus.Failed : AsyncOperationStatus.Succeeded;

        public DownloadStatus GetDownloadStatus()
        {
            if (!IsValid())
                throw new InvalidOperationException("Reading a released handle.");

            Operation.Reads++;
            DownloadBoundary.Trace.Add("status:" + Operation.Name + ":" + Operation.Frame);
            // A terminal raw percentage below 1 detects loss of the explicit completion override.
            return new DownloadStatus { TotalBytes = 100, DownloadedBytes = Operation.Frame == 0 && !IsDone ? 25 : 75 };
        }
    }

    public struct AsyncOperationHandle<T>
    {
        internal DownloadBoundary.Operation Operation;
        public T Result => (T)Operation.Result;
        public bool IsValid() => Operation != null && Operation.Releases == 0;
    }
}

namespace UnityEngine.AddressableAssets
{
    public static class Addressables
    {
        public enum MergeMode { Union }

        public static AsyncOperationHandle<IList<IResourceLocation>> LoadResourceLocationsAsync(IList<object> keys, MergeMode mode, Type type)
        {
            DownloadBoundary.Trace.Add("locations");
            return new AsyncOperationHandle<IList<IResourceLocation>> { Operation = DownloadBoundary.Locations };
        }

        public static AsyncOperationHandle<long> GetDownloadSizeAsync(IList<IResourceLocation> locations)
        {
            throw new NotSupportedException("Size lookup is outside this fixture.");
        }

        public static AsyncOperationHandle DownloadDependenciesAsync(IList<IResourceLocation> locations, bool autoReleaseHandle)
        {
            if (autoReleaseHandle)
                throw new InvalidOperationException("Downloader must own its download handle.");

            var operation = DownloadBoundary.Downloads.Dequeue();
            DownloadBoundary.Started.Add(operation);
            DownloadBoundary.Trace.Add("start:" + operation.Name);
            return new AsyncOperationHandle { Operation = operation };
        }

        public static void Release(AsyncOperationHandle handle)
        {
            DownloadBoundary.Release(handle.Operation);
        }

        public static void Release<T>(AsyncOperationHandle<T> handle)
        {
            DownloadBoundary.Release(handle.Operation);
        }
    }
}
