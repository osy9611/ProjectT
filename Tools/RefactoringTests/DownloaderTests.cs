using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT.Addressable;

internal static class DownloaderTests
{
    private static readonly List<string> Labels = new List<string> { "fixture" };
    private static readonly List<DownloadInfo> Infos = new List<DownloadInfo>();
    private static readonly List<float> Progress = new List<float>();
    private static int passed;

    private static int Main()
    {
        try
        {
            Run("progress and completion reuse the same mutable info", ProgressAndCompletion);
            Run("immediate completion reports once without yielding", ImmediateCompletion);
            Run("null callback skips status reads while pending", () => NullCallback(2));
            Run("null callback skips status reads on immediate completion", () => NullCallback(0));
            Run("pending callback failure retries and preserves final error", () => CallbackFailure(2));
            Run("completion callback failure retries and preserves final error", () => CallbackFailure(0));
            Run("download failure releases before retry and then succeeds", RetryThenSuccess);
            Run("three download failures preserve the final error", RetryExhausted);
            Run("failed handle without an exception uses the existing fallback", MissingFailureException);
            Run("cancellation while yielding releases without another callback", CancellationDuringYield);
            Run("callback cancellation and error suppress retries", CancellationInsideCallback);
            Run("cancellation during retry delay prevents another attempt", CancellationDuringRetry);
            Console.WriteLine("ALL PASSED: " + passed + " downloader scenarios");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Run(string name, Action test)
    {
        DownloadBoundary.Reset();
        Infos.Clear();
        Progress.Clear();
        test();
        Check(DownloadBoundary.PendingCount == 0, "No pending waits may remain.");
        Check(DownloadBoundary.Locations.Releases == 1, "Locations must be released once.");
        int locationRelease = DownloadBoundary.Trace.IndexOf("release:locations");
        foreach (var operation in DownloadBoundary.Started)
        {
            Check(operation.Releases == 1, "Every started download must be released once.");
            Check(DownloadBoundary.Trace.IndexOf("release:" + operation.Name) < locationRelease, "Download handles must be released before locations.");
        }

        Check(DownloadBoundary.Trace.FindLastIndex(entry => entry.StartsWith("callback:")) < locationRelease, "Progress may not be reported after locations are released.");
        Console.WriteLine("PASS " + name);
        Console.WriteLine("TRACE " + string.Join(" > ", DownloadBoundary.Trace));
        passed++;
    }

    private static void Record(DownloadInfo info)
    {
        Check(info.size == 100 && (info.downloadedByte == 25 || info.downloadedByte == 75), "Progress byte fields must be refreshed.");
        int identity = Infos.FindIndex(existing => ReferenceEquals(existing, info));
        if (identity < 0)
            identity = Infos.Count;

        DownloadBoundary.Trace.Add("callback:" + identity + ":" + info.size + ":" + info.downloadedByte + ":" + info.progress.ToString(CultureInfo.InvariantCulture));
        Infos.Add(info);
        Progress.Add(info.progress);
    }

    private static Exception Finish(UniTask task)
    {
        for (int i = 0; task.Status == UniTaskStatus.Pending && i < 16; ++i)
            DownloadBoundary.Step();

        Check(task.Status != UniTaskStatus.Pending, "Download did not finish within the fixture's step limit.");
        try
        {
            task.GetAwaiter().GetResult();
            return null;
        }
        catch (Exception error)
        {
            DownloadBoundary.Trace.Add("error:" + error.GetType().Name + ":" + error.Message);
            return error;
        }
    }

    private static void ProgressAndCompletion()
    {
        var operation = DownloadBoundary.Enqueue(2);
        Action<DownloadInfo> callback = info =>
        {
            Record(info);
            info.size = -1;
            info.downloadedByte = -1;
            info.progress = -1f;
        };
        var task = Downloader.DownloadAsync(Labels, callback, default);
        Check(Infos.Count == 1 && task.Status == UniTaskStatus.Pending, "First progress callback must precede the first yield.");
        Check(Finish(task) == null, "Expected success.");
        Check(Progress.Count == 3 && Progress[0] == 0.25f && Progress[1] == 0.75f && Progress[2] == 1f, "Progress values or callback count changed.");
        Check(ReferenceEquals(Infos[0], Infos[1]) && ReferenceEquals(Infos[1], Infos[2]), "One info must be reused per attempt.");
        Check(operation.Reads == 3 && DownloadBoundary.Yields == 2, "Read/yield counts changed.");
    }

    private static void ImmediateCompletion()
    {
        var operation = DownloadBoundary.Enqueue(0);
        Check(Finish(Downloader.DownloadAsync(Labels, Record, default)) == null, "Expected success.");
        Check(Progress.Count == 1 && Progress[0] == 1f && operation.Reads == 1 && DownloadBoundary.Yields == 0, "Immediate completion changed.");
    }

    private static void NullCallback(int frames)
    {
        var operation = DownloadBoundary.Enqueue(frames);
        Check(Finish(Downloader.DownloadAsync(Labels, null, default)) == null, "Expected success.");
        Check(operation.Reads == 0 && DownloadBoundary.Yields == frames, "Null callback must skip all status reads without changing yields.");
    }

    private static void CallbackFailure(int frames)
    {
        for (int i = 0; i < 3; ++i)
            DownloadBoundary.Enqueue(frames);

        var failure = new InvalidOperationException("callback failure");
        var error = Finish(Downloader.DownloadAsync(Labels, info => { Record(info); throw failure; }, default));
        Check(ReferenceEquals(error, failure), "Original callback exception must reach the caller.");
        Check(Infos.Count == 3 && DownloadBoundary.Started.Count == 3 && DownloadBoundary.Delays == 2, "Callback failures must preserve existing retries.");
        Check(!ReferenceEquals(Infos[0], Infos[1]) && !ReferenceEquals(Infos[1], Infos[2]), "Retries need separate info instances.");
        Check(DownloadBoundary.Yields == 0, "No yield may occur after a throwing callback.");
    }

    private static void RetryThenSuccess()
    {
        var failed = DownloadBoundary.Enqueue(1, new InvalidOperationException("download failed"));
        DownloadBoundary.Enqueue(0);
        Check(Finish(Downloader.DownloadAsync(Labels, Record, default)) == null, "Retry should succeed.");
        Check(Progress.Count == 2 && Progress[0] == 0.25f && Progress[1] == 1f, "Failed attempt must not publish completion.");
        Check(failed.Reads == 1 && DownloadBoundary.Delays == 1, "Failure reads or retry count changed.");
        Check(DownloadBoundary.Trace.IndexOf("release:download1") < DownloadBoundary.Trace.IndexOf("delay:1s"), "Failed handle must be released before delay.");
        Check(!ReferenceEquals(Infos[0], Infos[1]), "Retry must allocate its own info.");
    }

    private static void RetryExhausted()
    {
        DownloadBoundary.Enqueue(0, new InvalidOperationException("failure 1"));
        DownloadBoundary.Enqueue(0, new InvalidOperationException("failure 2"));
        var final = new InvalidOperationException("failure 3");
        DownloadBoundary.Enqueue(0, final);
        Check(ReferenceEquals(Finish(Downloader.DownloadAsync(Labels, Record, default)), final), "Final download error must be preserved.");
        Check(Infos.Count == 0 && DownloadBoundary.Delays == 2 && DownloadBoundary.Started.Count == 3, "Retry exhaustion changed.");
    }

    private static void MissingFailureException()
    {
        for (int i = 0; i < 3; ++i)
            DownloadBoundary.Enqueue(0, failed: true);

        var error = Finish(Downloader.DownloadAsync(Labels, Record, default));
        Check(error is InvalidOperationException && error.Message == "Bundle download failed.", "Fallback exception changed.");
        Check(Infos.Count == 0 && DownloadBoundary.Delays == 2, "Failed handle must not publish completion.");
    }

    private static void CancellationDuringYield()
    {
        using var source = new CancellationTokenSource();
        var operation = DownloadBoundary.Enqueue(2);
        var task = Downloader.DownloadAsync(Labels, Record, source.Token);
        source.Cancel();
        var error = Finish(task);
        Check(error is OperationCanceledException canceled && canceled.CancellationToken == source.Token, "Cancellation token must reach the caller.");
        Check(Infos.Count == 1 && operation.Reads == 1 && DownloadBoundary.Delays == 0, "Cancellation must stop progress and retries.");
    }

    private static void CancellationInsideCallback()
    {
        using var source = new CancellationTokenSource();
        DownloadBoundary.Enqueue(2);
        var failure = new InvalidOperationException("callback canceled and failed");
        var task = Downloader.DownloadAsync(Labels, info => { Record(info); source.Cancel(); throw failure; }, source.Token);
        Check(ReferenceEquals(Finish(task), failure), "Callback failure must not be replaced by cancellation.");
        Check(Infos.Count == 1 && DownloadBoundary.Delays == 0 && DownloadBoundary.Started.Count == 1, "Canceled callback must suppress retries.");
    }

    private static void CancellationDuringRetry()
    {
        using var source = new CancellationTokenSource();
        var failed = DownloadBoundary.Enqueue(0, new InvalidOperationException("download failed"));
        DownloadBoundary.Enqueue(0);
        var task = Downloader.DownloadAsync(Labels, Record, source.Token);
        Check(failed.Releases == 1 && DownloadBoundary.Delays == 1, "Must enter retry delay after releasing the failed handle.");
        source.Cancel();
        Check(Finish(task) is OperationCanceledException, "Expected retry delay cancellation.");
        Check(DownloadBoundary.Started.Count == 1 && Infos.Count == 0, "Canceled retry must not start a second download.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
