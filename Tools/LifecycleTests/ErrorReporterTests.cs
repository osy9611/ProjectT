using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Cysharp.Threading.Tasks;
using ProjectT;
using UnityEngine;

static partial class Program
{
    sealed class ListSink : IErrorSink
    {
        public readonly List<ErrorEvent> Events = new();
        public int Batches;

        public void Write(IReadOnlyList<ErrorEvent> errors)
        {
            Batches++;
            Events.AddRange(errors);
        }
    }

    sealed class ThrowingSink : IErrorSink
    {
        public int Calls;

        public void Write(IReadOnlyList<ErrorEvent> errors)
        {
            Calls++;
            throw new IOException("sink failed");
        }
    }

    sealed class LoggingSink : IErrorSink
    {
        public int Calls;

        public void Write(IReadOnlyList<ErrorEvent> errors)
        {
            Calls++;
            Debug.LogError("logged by sink");
        }
    }

    static void ResetReporter(params IErrorSink[] sinks)
    {
        ErrorReporter.ResetStatics();
        ErrorReporter.SetSinks(sinks);
        Debug.Errors.Clear();
    }

    static Exception Thrown(Exception error)
    {
        try { throw error; }
        catch (Exception caught) { return caught; }
    }

    static Exception ThrownAtSameSite(int index) => Thrown(new InvalidOperationException("repeat " + index));

    static string TempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ProjectT-ErrorReporter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static int CountOccurrences(string path, string marker)
    {
        if (!File.Exists(path))
            return 0;

        string text = File.ReadAllText(path);
        int count = 0;
        for (int index = text.IndexOf(marker, StringComparison.Ordinal); index >= 0; index = text.IndexOf(marker, index + marker.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    static string FingerprintOf(string condition, string stackTrace, LogType type = LogType.Error)
    {
        var sink = new ListSink();
        ErrorReporter.SetSinks(sink);
        Application.RaiseLog(condition, stackTrace, type);
        ErrorReporter.Flush();
        return sink.Events[0].Fingerprint;
    }

    static async UniTask ReporterRun(string name, Func<UniTask> test)
    {
        await Run(name, async () =>
        {
            try
            {
                await test();
            }
            finally
            {
                ResetReporter();
            }
        });
    }

    static async UniTask RunErrorReporterTests()
    {
        await ReporterRun("ErrorReporter: aggregate is split per inner with one event id and delivered once", () =>
        {
            var sink = new ListSink();
            ResetReporter(sink);
            var inner = new AggregateException(Thrown(new ArgumentException("b")), new OperationCanceledException());
            Global.LogException(new AggregateException(Thrown(new InvalidOperationException("a")), inner));
            ErrorReporter.Flush();
            ErrorReporter.Flush();
            Check(sink.Events.Count == 3 && sink.Batches == 1, "Inner events were not delivered exactly once");
            Check(sink.Events[0].Id == sink.Events[1].Id && sink.Events[1].Id == sink.Events[2].Id, "Inner events do not share the event id");
            Check(sink.Events[0].Exception is InvalidOperationException && sink.Events[1].Exception is ArgumentException &&
                sink.Events[2].Exception is OperationCanceledException, "Inner exceptions lost");
            Check(sink.Events[0].Fingerprint.StartsWith("System.InvalidOperationException|", StringComparison.Ordinal), "Fingerprint lacks exception type");
            Check(Debug.Errors.Count == 1 && Debug.Errors[0] is AggregateException, "Console did not receive the original exception once");
            return UniTask.CompletedTask;
        });

        // 정상 취소는 호출 지점이 거르므로 보고 경계까지 온 취소는 예상하지 못한 취소로 보고한다.
        await ReporterRun("ErrorReporter: cancellation that reaches the reporter is reported and null is ignored", () =>
        {
            var sink = new ListSink();
            ResetReporter(sink);
            Global.LogException(new OperationCanceledException());
            Global.LogException(new AggregateException(new System.Threading.Tasks.TaskCanceledException()));
            Global.LogException(null);
            ErrorReporter.Flush();
            Check(sink.Events.Count == 2 && sink.Events[0].Exception is OperationCanceledException &&
                sink.Events[1].Exception is System.Threading.Tasks.TaskCanceledException, "Cancellation reaching the reporter was not reported");
            Check(Debug.Errors.Count == 2, "Console did not receive each reported cancellation once");
            return UniTask.CompletedTask;
        });

        await ReporterRun("ErrorReporter: own console writes are not recaptured while external Unity logs are", () =>
        {
            var sink = new ListSink();
            ResetReporter(sink);
            ResetReporter(sink);
            Check(Application.LogSubscriberCount == 1, "Log hook subscribed twice");
            Global.LogException(Thrown(new InvalidOperationException("reported")));
            Global.LogError("handled");
            Debug.LogException(Thrown(new FormatException("external exception")));
            Application.RaiseLog("warning", "", LogType.Warning);
            Application.RaiseLog("assert", "", LogType.Assert);
            ErrorReporter.Flush();
            Check(sink.Events.Count == 4 && Debug.Errors.Count == 3, "Unexpected event count " + sink.Events.Count);
            Check(sink.Events[0].Exception is InvalidOperationException, "Reported exception lost");
            var handled = sink.Events[1];
            Check(handled.Kind == ErrorEvent.LogKind && handled.Exception == null && handled.Message.Contains("handled"), "Global.LogError not captured by the hook");
            Check(handled.Fingerprint.Contains("RunErrorReporterTests") && !handled.Fingerprint.Contains("UnityEngine.") && !handled.Fingerprint.Contains("ProjectT.Global:"),
                "LogError fingerprint does not point at the caller: " + handled.Fingerprint);
            Check(sink.Events[2].Kind == "FormatException" && sink.Events[3].Message == "assert", "External exception or assert not captured");
            return UniTask.CompletedTask;
        });

        await ReporterRun("ErrorReporter: unobserved UniTask exception is routed to the reporter", () =>
        {
            var sink = new ListSink();
            ResetReporter(sink);
            UniTaskScheduler.PublishUnobservedTaskException(new TimeoutException("unobserved"));
            ErrorReporter.Flush();
            Check(sink.Events.Count == 1 && sink.Events[0].Exception is TimeoutException && Debug.Errors.Count == 1, "Unobserved exception not reported once");
            return UniTask.CompletedTask;
        });

        await ReporterRun("ErrorReporter: hook fingerprints separate messages and source lines but not IL offsets", () =>
        {
            ResetReporter();
            const string frame = "Game.Shop:Buy (int)\n";
            const string engine = "UnityEngine.Debug:LogError (object)\nProjectT.Global:LogError (string)\n";
            Check(FingerprintOf("item 1 missing", engine + frame) == FingerprintOf("item 22 missing", engine + frame), "Message digits split the fingerprint");
            Check(FingerprintOf("item missing", engine + frame) != FingerprintOf("price missing", engine + frame), "Different messages from one frame merged");
            Check(FingerprintOf("same", "Game.Shop:Buy () (at Assets/Shop.cs:10)") != FingerprintOf("same", "Game.Shop:Buy () (at Assets/Shop.cs:20)"), "Different source lines merged");

            string onlyEngine = FingerprintOf("engine only", engine);
            Check(onlyEngine == "Log|engine only|", "Engine-only stack did not fall back to the message: " + onlyEngine);

            string first = FingerprintOf("InvalidOperationException: a", "at Game.Shop.Buy () [0x0001a] in <x>:0", LogType.Exception);
            string second = FingerprintOf("InvalidOperationException: b", "at Game.Shop.Buy () [0x0002b] in <x>:0", LogType.Exception);
            Check(first == second && first.StartsWith("InvalidOperationException|", StringComparison.Ordinal), "Exception kind or IL offset handling failed: " + first);
            return UniTask.CompletedTask;
        });

        await ReporterRun("ErrorReporter: repeated fingerprint is capped and the occurrence count stays visible", () =>
        {
            var sink = new ListSink();
            ResetReporter(sink);
            for (int i = 1; i <= 120; ++i)
            {
                Global.LogException(ThrownAtSameSite(i));
                if (i % 7 == 0)
                    ErrorReporter.Flush();
            }
            ErrorReporter.Flush();
            var occurrences = sink.Events.ConvertAll(error => error.Occurrence);
            Check(string.Join(",", occurrences) == "1,2,3,4,5,10,100", "Unexpected deliveries " + string.Join(",", occurrences));
            Check(sink.Events.TrueForAll(error => error.Fingerprint == sink.Events[0].Fingerprint), "Same site produced different fingerprints");
            return UniTask.CompletedTask;
        });

        await ReporterRun("ErrorReporter: queue cap drops overflow and reports the dropped count", () =>
        {
            var sink = new ListSink();
            ResetReporter(sink);
            for (int i = 0; i < 1005; ++i)
                Application.RaiseLog("flood", "Game.Loop:Tick ()", LogType.Error);
            ErrorReporter.Flush();
            Check(sink.Events.Count > 0 && sink.Events[0].Message == "5 error events dropped", "Dropped count missing");
            Check(sink.Events[sink.Events.Count - 1].Occurrence == 1000, "Queued events beyond the cap were kept");
            int delivered = sink.Events.Count;
            ErrorReporter.Flush();
            Check(sink.Events.Count == delivered && sink.Batches == 1, "Empty flush delivered");
            Application.RaiseLog("after", "Game.Loop:Tick ()", LogType.Error);
            ErrorReporter.Flush();
            Check(sink.Events.Count == delivered + 1, "Queue did not accept events after draining");
            return UniTask.CompletedTask;
        });

        await ReporterRun("ErrorReporter: distinct fingerprints beyond the cap share one bucket", () =>
        {
            var sink = new ListSink();
            ResetReporter(sink);
            for (int i = 0; i < 1010; ++i)
            {
                Application.RaiseLog("distinct", "Game.Generated:Method" + i + " ()", LogType.Error);
                if (i % 500 == 0)
                    ErrorReporter.Flush();
            }
            ErrorReporter.Flush();
            var counts = (Dictionary<string, int>)typeof(ErrorReporter).GetField("s_counts", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Check(counts.Count == 1001 && counts["Overflow"] == 10, "Fingerprint table grew past the cap: " + counts.Count);
            Check(sink.Events.Count == 1006, "Overflow bucket was not rate limited: " + sink.Events.Count);
            return UniTask.CompletedTask;
        });

        await ReporterRun("ErrorReporter: failing sink is removed without recursion and other sinks keep receiving", () =>
        {
            var throwing = new ThrowingSink();
            var logging = new LoggingSink();
            var sink = new ListSink();
            ResetReporter(throwing, logging, sink);
            Global.LogException(Thrown(new InvalidOperationException("first")));
            ErrorReporter.Flush();
            Check(throwing.Calls == 1 && sink.Events.Count == 1, "Remaining sinks skipped after a sink failure");
            Check(Debug.Errors.Exists(error => error is IOException), "Sink failure not written to console");

            ErrorReporter.Flush();
            Check(sink.Events.Count == 2 && sink.Events[1].Exception is IOException, "Sink failure not delivered to remaining sinks");
            for (int i = 0; i < 10; ++i)
                ErrorReporter.Flush();
            Check(throwing.Calls == 1 && sink.Events.Count == 2 && logging.Calls == 2, "Failure or sink log recirculated");

            Global.LogException(Thrown(new InvalidOperationException("second")));
            ErrorReporter.Flush();
            Check(throwing.Calls == 1 && sink.Events.Count == 3, "Removed sink was called again");
            return UniTask.CompletedTask;
        });

        await ReporterRun("ErrorReporter: Global.Update flushes even when a manager update throws", async () =>
        {
            var sink = new ListSink();
            ResetReporter(sink);
            Global.ResetStaticsForTests();
            var host = new ManagerHost();
            var global = Global.BindForTests(host);
            try
            {
                var probe = host.Register(new Probe<A>(new(), "throwing update"));
                probe.UpdateAction = () => throw new InvalidOperationException("update failed");
                await host.InitializeAsync();
                Global.LogException(new InvalidOperationException("before update"));
                await ThrowsSync<InvalidOperationException>(() => global.UpdateForTests());
                Check(sink.Events.Count == 1 && sink.Events[0].Exception.Message == "before update", "Update failure skipped the flush");
            }
            finally
            {
                global.Shutdown();
                UnityEngine.Object.Destroy(global.gameObject);
                Global.ResetStaticsForTests();
            }
        });

        await ReporterRun("ErrorReporter: host shutdown errors are reported once", async () =>
        {
            var sink = new ListSink();
            ResetReporter(sink);
            Global.ResetStaticsForTests();
            var globals = new List<Global>();
            try
            {
                var failedHost = new ManagerHost();
                var failedGlobal = Global.BindForTests(failedHost);
                globals.Add(failedGlobal);
                failedHost.Register(new Probe<A>(new(), "failed stop") { FailStop = true });
                failedHost.Register(new Probe<B>(new(), "failed start") { FailStart = true });
                failedGlobal.InitializeForTests().Forget(Global.LogException);
                failedGlobal.Shutdown();
                // Global.Shutdown releases its sinks after the final flush.
                ErrorReporter.SetSinks(sink);
                ErrorReporter.Flush();
                var failed = sink.Events.FindAll(error => error.Exception?.Message is "start:failed start" or "stop:failed stop");
                Check(failed.Count == 2 && failed[0].Exception.Message == "start:failed start" && failed[0].Id == failed[1].Id,
                    "Initialization failure and its cleanup were not reported once as one event");
                Check(Debug.Errors.Count == 1 && Debug.Errors[0] is AggregateException, "Initialization failure was not written to the console once");

                var gate = new UniTaskCompletionSource();
                var stoppedHost = new ManagerHost();
                var stoppedGlobal = Global.BindForTests(stoppedHost);
                globals.Add(stoppedGlobal);
                stoppedHost.Register(new Probe<A>(new(), "stopped stop") { FailStop = true, Gate = gate.Task });
                var pending = stoppedGlobal.InitializeForTests();
                stoppedGlobal.Shutdown();
                // Global.Shutdown releases its sinks after the final flush.
                ErrorReporter.SetSinks(sink);
                gate.TrySetResult();
                await pending;
                ErrorReporter.Flush();
                Check(sink.Events.FindAll(error => error.Exception?.Message == "stop:stopped stop").Count == 1, "Shutdown cleanup reported twice or missed");

                sink.Events.Clear();
                Debug.Errors.Clear();
                var normalHost = new ManagerHost();
                var normalGlobal = Global.BindForTests(normalHost);
                globals.Add(normalGlobal);
                normalHost.Register(new Probe<A>(new(), "normal first") { FailStop = true });
                normalHost.Register(new Probe<B>(new(), "normal second") { FailStop = true });
                await normalGlobal.InitializeForTests();
                normalGlobal.Shutdown();
                ErrorReporter.SetSinks(sink);
                ErrorReporter.Flush();
                Check(sink.Events.Count == 2 && sink.Events[0].Exception.Message == "stop:normal second" && sink.Events[0].Id == sink.Events[1].Id,
                    "Normal shutdown cleanup errors were not reported as one event in reverse order");
                Check(Debug.Errors.Count == 1 && Debug.Errors[0] is AggregateException, "Normal shutdown cleanup was not written to the console once");
            }
            finally
            {
                foreach (var global in globals)
                    UnityEngine.Object.Destroy(global.gameObject);
                Global.ResetStaticsForTests();
            }
        });

        await ReporterRun("ErrorReporter: initialization failure by cancellation exception reaches the caller", async () =>
        {
            ResetReporter(new ListSink());
            Global.ResetStaticsForTests();
            var host = new ManagerHost();
            var global = Global.BindForTests(host);
            try
            {
                host.Register(new Probe<A>(new(), "timeout") { StartAction = () => throw new OperationCanceledException("start timeout") });
                var failure = await Capture(() => global.InitializeForTests());
                Check(failure is AggregateException { InnerExceptions: [OperationCanceledException] }, "Cancellation failure did not reach the caller as a failure");
                Check(host.State == ManagerState.Failed, "Cancellation failure did not fail the host");
            }
            finally
            {
                global.Shutdown();
                UnityEngine.Object.Destroy(global.gameObject);
                Global.ResetStaticsForTests();
            }
        });

        await ReporterRun("ErrorReporter: initialization failure by cancellation exception keeps its message", async () =>
        {
            var sink = new ListSink();
            ResetReporter(sink);
            Global.ResetStaticsForTests();
            var gate = new UniTaskCompletionSource();
            var host = new ManagerHost();
            var global = Global.BindForTests(host);
            try
            {
                // UniTask는 await 전에 던진 취소 예외를 메서드 경계에서 새 취소로 바꾸므로 내부 타임아웃처럼 await 뒤에 던진다.
                async UniTask TimeoutAfterGate()
                {
                    await gate.Task;
                    throw new OperationCanceledException("start timeout");
                }
                host.Register(new Probe<A>(new(), "timeout") { Gate = TimeoutAfterGate() });
                global.InitializeForTests().Forget(Global.LogException);
                gate.TrySetResult();
                ErrorReporter.Flush();
                Check(sink.Events.Count == 1, "Cancellation initialization failure reported " + sink.Events.Count + " times");
                Check(sink.Events[0].Exception.Message == "start timeout", "Cancellation initialization failure lost its message");
            }
            finally
            {
                global.Shutdown();
                UnityEngine.Object.Destroy(global.gameObject);
                Global.ResetStaticsForTests();
            }
        });

        await ReporterRun("ErrorReporter: manager failing after shutdown during initialization is reported once", async () =>
        {
            var sink = new ListSink();
            ResetReporter(sink);
            Global.ResetStaticsForTests();
            var gate = new UniTaskCompletionSource();
            var host = new ManagerHost();
            var global = Global.BindForTests(host);
            try
            {
                host.Register(new Probe<A>(new(), "late") { FailStart = true, Gate = gate.Task });
                global.InitializeForTests().Forget(Global.LogException);
                global.Shutdown();
                // Global.Shutdown releases its sinks after the final flush.
                ErrorReporter.SetSinks(sink);
                gate.TrySetResult();
                ErrorReporter.Flush();
                var late = sink.Events.FindAll(error => error.Exception?.Message == "start:late");
                Check(late.Count == 1, "Late initialization failure reported " + late.Count + " times");
            }
            finally
            {
                UnityEngine.Object.Destroy(global.gameObject);
                Global.ResetStaticsForTests();
            }
        });

        await ReporterRun("ErrorReporter: recreated or duplicate Global does not duplicate sinks", () =>
        {
            string directory = TempDirectory();
            string log = Path.Combine(directory, "ErrorReports", "errors.log");
            Application.persistentDataPath = directory;
            ResetReporter();
            Global.ResetStaticsForTests();
            var globals = new List<Global>();
            try
            {
                var first = new GameObject("Global").AddComponent<Global>();
                globals.Add(first);
                first.InitForTests();
                first.Shutdown();

                var second = new GameObject("Global").AddComponent<Global>();
                globals.Add(second);
                second.InitForTests();
                var duplicate = new GameObject("Global").AddComponent<Global>();
                globals.Add(duplicate);
                duplicate.InitForTests();
                Check(ReferenceEquals(Global.Instance, second), "Duplicate Global replaced the active instance");

                Global.LogException(new InvalidOperationException("marker-once"));
                duplicate.UpdateForTests();
                Check(CountOccurrences(log, "Exception: marker-once") == 0, "Inactive Global flushed");
                second.UpdateForTests();
                Check(CountOccurrences(log, "Exception: marker-once") == 1, "Event written " + CountOccurrences(log, "Exception: marker-once") + " times");

                Global.LogException(new InvalidOperationException("marker-shutdown"));
                second.Shutdown();
                Check(CountOccurrences(log, "Exception: marker-shutdown") == 1, "Shutdown did not flush once");

                Global.LogException(new InvalidOperationException("marker-after"));
                ErrorReporter.Flush();
                Check(CountOccurrences(log, "Exception: marker-after") == 0, "Sink outlived its Global");
            }
            finally
            {
                Global.Instance?.Shutdown();
                foreach (var global in globals)
                    UnityEngine.Object.Destroy(global.gameObject);
                Global.ResetStaticsForTests();
                Application.persistentDataPath = "";
                Directory.Delete(directory, true);
            }
            return UniTask.CompletedTask;
        });

        await ReporterRun("FileErrorSink: rotates once at the size cap", () =>
        {
            string directory = TempDirectory();
            try
            {
                var sink = new FileErrorSink(directory, 600);
                string log = Path.Combine(directory, "errors.log");
                string rotated = Path.Combine(directory, "errors.1.log");
                for (int i = 0; i < 20; ++i)
                {
                    var error = new ErrorEvent(Guid.NewGuid(), ErrorEvent.LogKind, "batch " + i + " " + new string('x', 80), null, null);
                    sink.Write(new[] { error.WithFingerprint(error.MakeFingerprint(), 1) });
                    Check(new FileInfo(log).Length <= 600, "Active log exceeded the cap");
                }

                Check(File.Exists(rotated) && new FileInfo(rotated).Length <= 600, "Log was not rotated");
                Check(Directory.GetFiles(directory).Length == 2, "Rotation kept more than one old file");
                Check(CountOccurrences(log, "batch 19 ") == 1 && CountOccurrences(log, "batch 0 ") == 0, "Newest batch missing or oldest kept");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
            return UniTask.CompletedTask;
        });

        await ReporterRun("FileErrorSink: keeps working after a transient IO failure", () =>
        {
            string directory = TempDirectory();
            try
            {
                var sink = new FileErrorSink(directory, 1024 * 1024);
                string log = Path.Combine(directory, "errors.log");
                var error = new ErrorEvent(Guid.NewGuid(), ErrorEvent.LogKind, "first", null, null);
                sink.Write(new[] { error.WithFingerprint(error.MakeFingerprint(), 1) });
                using (new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    error = new ErrorEvent(Guid.NewGuid(), ErrorEvent.LogKind, "locked", null, null);
                    sink.Write(new[] { error.WithFingerprint(error.MakeFingerprint(), 1) });
                }

                error = new ErrorEvent(Guid.NewGuid(), ErrorEvent.LogKind, "after", null, null);
                sink.Write(new[] { error.WithFingerprint(error.MakeFingerprint(), 1) });
                Check(CountOccurrences(log, "1 error batches dropped") == 1, "Dropped batch was not noted");
                Check(CountOccurrences(log, "\nafter\n") == 1 && CountOccurrences(log, "\nlocked\n") == 0, "Sink did not recover after the lock");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
            return UniTask.CompletedTask;
        });
    }
}
