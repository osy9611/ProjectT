using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT.Addressable;

internal static partial class FirebaseCacheTests
{
    private static int failures;
    private static int scenarios;

    private static int Main()
    {
        FirebaseBoundary.Initialize();

        Run("단일 PreWarm은 모든 gs:// URL을 받은 뒤 완료된다", () =>
        {
            FirebaseBoundary.AddBundle("single", "gs://bucket/single/a.bundle", "gs://bucket/single/b.bundle", "local/c.bundle");
            var prewarm = FirebaseAddressablesCache.PreWarmDependenciesAsync(new List<string> { "single" }, CancellationToken.None);
            Check(FirebaseBoundary.Fetches.Count == 2, "gs:// 의존성마다 URL 요청을 시작한다");

            FirebaseBoundary.Fetches[0].Source.SetResult(new Uri("https://storage.test/single/a.bundle"));
            FirebaseBoundary.RunMainThread();
            Check(prewarm.Status == UniTaskStatus.Pending, "요청이 남아 있으면 대기한다");

            FirebaseBoundary.ResolvePending();
            Check(prewarm.Status == UniTaskStatus.Succeeded, "마지막 요청 뒤 완료된다");
            Check(Transform("gs://bucket/single/b.bundle") == "https://storage.test/bucket/single/b.bundle", "받은 URL로 주소를 바꾼다");
            return "status=" + prewarm.Status;
        });

        Run("gs:// 의존성이 없으면 즉시 완료된다", () =>
        {
            FirebaseBoundary.AddBundle("local", "local/a.bundle");
            var prewarm = FirebaseAddressablesCache.PreWarmDependenciesAsync(new List<string> { "local" }, CancellationToken.None);
            Check(FirebaseBoundary.Fetches.Count == 0, "URL 요청을 시작하지 않는다");
            Check(prewarm.Status == UniTaskStatus.Succeeded, "바로 완료된다");
            return "status=" + prewarm.Status;
        });

        Run("앞선 PreWarm이 진행 중일 때 들어온 PreWarm도 완료된다", () =>
        {
            FirebaseBoundary.AddBundle("overlap", "gs://bucket/overlap/a.bundle");
            var first = FirebaseAddressablesCache.PreWarmDependenciesAsync(new List<string> { "overlap" }, CancellationToken.None);
            using (var lifetime = new CancellationTokenSource())
            {
                var second = FirebaseAddressablesCache.PreWarmDependenciesAsync(new List<string> { "overlap" }, lifetime.Token);
                FirebaseBoundary.ResolvePending();
                var afterFetches = second.Status;
                lifetime.Cancel();
                FirebaseBoundary.RunMainThread();
                string trace = "first=" + first.Status + " second=" + afterFetches + " secondAfterCancel=" + second.Status + " fetches=" + FirebaseBoundary.Fetches.Count + " errors=" + string.Join("|", UnityEngine.Debug.Errors);

                Check(first.Status == UniTaskStatus.Succeeded, "앞선 PreWarm이 완료된다");
                Check(afterFetches == UniTaskStatus.Succeeded, "뒤의 PreWarm이 토큰 취소 없이 완료된다 (" + trace + ")");
                Check(UnityEngine.Debug.Errors.Count == 0, "오류 로그를 남기지 않는다 (" + trace + ")");
                return trace;
            }
        });

        Run("취소된 PreWarm의 요청이 끝나기 전에 다시 들어온 PreWarm도 완료된다", () =>
        {
            FirebaseBoundary.AddBundle("reenter", "gs://bucket/reenter/a.bundle");
            UniTask canceled;
            using (var scene = new CancellationTokenSource())
            {
                canceled = FirebaseAddressablesCache.PreWarmDependenciesAsync(new List<string> { "reenter" }, scene.Token);
                scene.Cancel();
            }
            var reentered = FirebaseAddressablesCache.PreWarmDependenciesAsync(new List<string> { "reenter" }, CancellationToken.None);
            FirebaseBoundary.ResolvePending();
            string trace = "canceled=" + canceled.Status + " reentered=" + reentered.Status + " fetches=" + FirebaseBoundary.Fetches.Count + " errors=" + string.Join("|", UnityEngine.Debug.Errors);

            Check(canceled.Status == UniTaskStatus.Canceled, "씬 토큰 취소로 앞선 PreWarm이 취소된다");
            Check(reentered.Status == UniTaskStatus.Succeeded, "다시 들어온 PreWarm이 완료된다 (" + trace + ")");
            return trace;
        });

        Run("URL 요청이 실패해도 오류 로그 없이 완료하고 주소를 gs://로 남긴다", () =>
        {
            FirebaseBoundary.AddBundle("failed", "gs://bucket/failed/a.bundle");
            var prewarm = FirebaseAddressablesCache.PreWarmDependenciesAsync(new List<string> { "failed" }, CancellationToken.None);
            FirebaseBoundary.Fetches[0].Source.SetException(new InvalidOperationException("url failed"));
            FirebaseBoundary.RunMainThread();
            string trace = "status=" + prewarm.Status + " errors=" + string.Join("|", UnityEngine.Debug.Errors);

            Check(prewarm.Status == UniTaskStatus.Succeeded, "실패한 요청 뒤에도 완료된다 (" + trace + ")");
            Check(UnityEngine.Debug.Errors.Count == 0, "번들 로드가 다시 해석하므로 오류 로그를 남기지 않는다 (" + trace + ")");
            Check(Transform("gs://bucket/failed/a.bundle") == "gs://bucket/failed/a.bundle", "실패한 주소는 바꾸지 않는다");
            return trace;
        });

        RunProviderScenarios();

        Console.WriteLine(failures == 0 ? "ALL PASSED: " + scenarios + " firebase scenarios" : "FAILED: " + failures + " of " + scenarios + " firebase scenarios");
        return failures == 0 ? 0 : 1;
    }

    private static string Transform(string internalId)
    {
        return FirebaseAddressablesCache.IdTransformFunc(new FirebaseBoundary.Location { InternalId = internalId });
    }

    private static void Run(string name, Func<string> scenario)
    {
        scenarios++;
        FirebaseBoundary.Reset();
        try
        {
            Console.WriteLine("TRACE " + scenario());
            Console.WriteLine("PASS " + name);
        }
        catch (InvalidOperationException error)
        {
            failures++;
            Console.WriteLine("FAIL " + name + ": " + error.Message);
        }
        finally
        {
            FirebaseBoundary.ResolvePending();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
