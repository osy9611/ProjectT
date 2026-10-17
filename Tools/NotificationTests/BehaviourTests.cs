using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT;

internal static class BehaviourTests
{
    private static readonly NotificationId First = (NotificationId)1;

    private sealed class ListSink : IErrorSink
    {
        public readonly List<Exception> Exceptions = new List<Exception>();

        public void Write(IReadOnlyList<ErrorEvent> errors)
        {
            foreach (var error in errors)
                Exceptions.Add(error.Exception);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly ManagerHost Host = new ManagerHost();
        public readonly NotificationManager Manager;
        private readonly ListSink sink = new ListSink();

        public Fixture(bool hasGlobal = true, bool isReady = true)
        {
            ErrorReporter.ResetStatics();
            ErrorReporter.SetSinks(sink);
            UnityEngine.Debug.Errors.Clear();
            Manager = Host.Register(new NotificationManager());
            if (!hasGlobal)
                return;

            BindGlobal();
            if (isReady)
                Ready();
        }

        public void BindGlobal() => Global.BindForTests(Host);

        public void Ready() => Host.InitializeAsync().GetAwaiter().GetResult();

        // Global.Shutdown은 마지막 Flush 뒤 sink를 해제하므로 종료 뒤에 보고된 예외도 받도록 다시 지정한다.
        public IReadOnlyList<Exception> Reported()
        {
            ErrorReporter.SetSinks(sink);
            ErrorReporter.Flush();
            return sink.Exceptions;
        }

        public void Dispose()
        {
            if (Global.Instance == null)
                Host.Shutdown();
            else
                Global.Instance.Shutdown();
        }
    }

    private class TestHandler : NotifyHandlerBehaviour
    {
        private readonly Action<object[]> listener;

        public int Calls { get; private set; }
        public List<CancellationToken> SubscriptionTokens { get; } = new List<CancellationToken>();

        public TestHandler()
        {
            listener = _ => Calls++;
        }

        protected override void OnSubscribe(CancellationToken token)
        {
            SubscriptionTokens.Add(token);
            Global.Notify.Subscribe(First, listener, token);
        }

        public void Enable()
        {
            enabled = true;
            OnEnable();
        }

        public void Disable()
        {
            enabled = false;
            OnDisable();
        }

        public void Destroy()
        {
            OnDestroy();
        }
    }

    private sealed class FailingHandler : NotifyHandlerBehaviour
    {
        private readonly Action<object[]> listener;
        private readonly Exception failure;
        private readonly Exception cleanupFailure;

        public int Calls { get; private set; }

        public FailingHandler(Exception failure, Exception cleanupFailure = null)
        {
            this.failure = failure;
            this.cleanupFailure = cleanupFailure;
            listener = _ => Calls++;
        }

        protected override void OnSubscribe(CancellationToken token)
        {
            Global.Notify.Subscribe(First, listener, token);
            if (cleanupFailure != null)
                token.Register(() => throw cleanupFailure);

            throw failure;
        }

        public void Enable()
        {
            enabled = true;
            OnEnable();
        }
    }

    private sealed class ReentrantHandler : NotifyHandlerBehaviour
    {
        private readonly Action<object[]> listener;
        private readonly Exception failure;
        private int subscriptions;

        public int Calls { get; private set; }

        public ReentrantHandler(Exception failure)
        {
            this.failure = failure;
            listener = _ => Calls++;
        }

        protected override void OnSubscribe(CancellationToken token)
        {
            subscriptions++;
            Global.Notify.Subscribe(First, listener, token);
            if (subscriptions != 1)
                return;

            enabled = false;
            OnDisable();
            enabled = true;
            OnEnable();
            throw failure;
        }

        public void Enable()
        {
            enabled = true;
            OnEnable();
        }

        public void Disable()
        {
            enabled = false;
            OnDisable();
        }
    }

    private sealed class ExpectedCancellationHandler : NotifyHandlerBehaviour
    {
        private readonly Exception cleanupFailure;

        public ExpectedCancellationHandler(Exception cleanupFailure)
        {
            this.cleanupFailure = cleanupFailure;
        }

        protected override void OnSubscribe(CancellationToken token)
        {
            token.Register(() => throw cleanupFailure);
            Global.Instance.Shutdown();
            throw new OperationCanceledException("expected shutdown cancellation", token);
        }

        public void Enable()
        {
            enabled = true;
            OnEnable();
        }
    }

    public static void RunAll(Action<string, Action> run)
    {
        run("enabled ready behaviour subscribes immediately", ReadyEnableSubscribes);
        run("behaviour waits for Global and readiness", DeferredGlobalReadiness);
        run("disabling while waiting prevents a late subscription", DisableWhileWaiting);
        run("re-enabling while readiness is pending keeps only the latest generation", ReenableWhileReadinessPending);
        run("disabling removes owned subscriptions", DisableRemovesSubscriptions);
        run("re-enabling uses a fresh token without duplicates", ReenableUsesFreshLifetime);
        run("destroying removes owned subscriptions", DestroyRemovesSubscriptions);
        run("Global shutdown while waiting is expected cancellation", GlobalShutdownWhileWaiting);
        run("expected shutdown cancellation reports cleanup failure once", ExpectedCancellationReportsCleanupFailure);
        run("partial subscription failure rolls back and reports once", PartialFailureRollsBack);
        run("throwing rollback still removes subscriptions and reports once", ThrowingRollbackStillRemovesSubscriptions);
        run("unexpected hook cancellation rolls back and reports once", UnexpectedHookCancellationReports);
        run("an old failed generation cannot cancel a re-enabled generation", OldFailureCannotCancelNewGeneration);
    }

    private static void ReadyEnableSubscribes()
    {
        using var fixture = new Fixture();
        var handler = new UnityEngine.GameObject().Attach(new TestHandler());

        handler.Enable();
        fixture.Manager.Publish(First);

        Check(handler.Calls == 1, "A ready enabled behaviour did not subscribe.");
        Check(handler.SubscriptionTokens.Count == 1, "The subscription hook ran more than once.");
    }

    private static void DeferredGlobalReadiness()
    {
        using var fixture = new Fixture(false);
        var handler = new UnityEngine.GameObject().Attach(new TestHandler());

        handler.Enable();
        Check(handler.SubscriptionTokens.Count == 0, "The hook ran before Global existed.");

        fixture.BindGlobal();
        ManualNotificationScheduler.StepFrame();
        Check(handler.SubscriptionTokens.Count == 0, "The hook ran before Global was ready.");

        fixture.Ready();
        fixture.Manager.Publish(First);
        Check(handler.Calls == 1, "The behaviour did not subscribe after Global became ready.");
    }

    private static void DisableWhileWaiting()
    {
        using var fixture = new Fixture(false);
        var handler = new UnityEngine.GameObject().Attach(new TestHandler());

        handler.Enable();
        handler.Disable();
        fixture.BindGlobal();
        ManualNotificationScheduler.StepFrame();
        fixture.Ready();
        fixture.Manager.Publish(First);

        Check(handler.Calls == 0 && handler.SubscriptionTokens.Count == 0, "A disabled waiting behaviour subscribed late.");
        Check(fixture.Reported().Count == 0, "Normal owner cancellation was reported as an error.");
    }

    private static void DisableRemovesSubscriptions()
    {
        using var fixture = new Fixture();
        var handler = new UnityEngine.GameObject().Attach(new TestHandler());

        handler.Enable();
        handler.Disable();
        fixture.Manager.Publish(First);

        Check(handler.Calls == 0, "Disable left an owned subscription registered.");
    }

    private static void ReenableWhileReadinessPending()
    {
        using var fixture = new Fixture(true, false);
        var handler = new UnityEngine.GameObject().Attach(new TestHandler());

        handler.Enable();
        handler.Disable();
        handler.Enable();
        fixture.Ready();
        fixture.Manager.Publish(First);

        Check(handler.SubscriptionTokens.Count == 1, "A stale readiness continuation invoked the subscription hook.");
        Check(handler.Calls == 1, "Pending re-enable produced a missing or duplicate subscription.");
        Check(!handler.SubscriptionTokens[0].IsCancellationRequested, "The stale generation canceled the latest owner token.");
    }

    private static void ReenableUsesFreshLifetime()
    {
        using var fixture = new Fixture();
        var handler = new UnityEngine.GameObject().Attach(new TestHandler());

        handler.Enable();
        var firstToken = handler.SubscriptionTokens[0];
        handler.Disable();
        handler.Enable();
        var secondToken = handler.SubscriptionTokens[1];
        fixture.Manager.Publish(First);

        Check(firstToken.IsCancellationRequested, "Disable did not cancel the first owner token.");
        Check(!secondToken.IsCancellationRequested, "Re-enable reused a canceled owner token.");
        Check(handler.Calls == 1, "Re-enable produced a missing or duplicate subscription.");
    }

    private static void DestroyRemovesSubscriptions()
    {
        using var fixture = new Fixture();
        var handler = new UnityEngine.GameObject().Attach(new TestHandler());

        handler.Enable();
        handler.Destroy();
        fixture.Manager.Publish(First);

        Check(handler.Calls == 0, "Destroy left an owned subscription registered.");
    }

    private static void PartialFailureRollsBack()
    {
        using var fixture = new Fixture();
        var expected = new InvalidOperationException("subscribe failure");
        var handler = new UnityEngine.GameObject().Attach(new FailingHandler(expected));

        handler.Enable();
        fixture.Manager.Publish(First);

        Check(handler.Calls == 0, "A partially registered hook was not rolled back.");
        var reported = fixture.Reported();
        Check(reported.Count == 1 && ReferenceEquals(reported[0], expected),
            "The hook failure was not reported exactly once with the original exception.");
    }

    private static void ThrowingRollbackStillRemovesSubscriptions()
    {
        using var fixture = new Fixture();
        var subscriptionFailure = new InvalidOperationException("subscribe failure");
        var cleanupFailure = new ApplicationException("cleanup failure");
        var handler = new UnityEngine.GameObject().Attach(new FailingHandler(subscriptionFailure, cleanupFailure));

        handler.Enable();
        fixture.Manager.Publish(First);

        Check(handler.Calls == 0, "A throwing cleanup callback prevented partial subscriptions from being removed.");
        Check(fixture.Reported().Count == 1, "The failure must be reported exactly once.");
    }

    private static void GlobalShutdownWhileWaiting()
    {
        using var fixture = new Fixture(true, false);
        var handler = new UnityEngine.GameObject().Attach(new TestHandler());

        handler.Enable();
        // Global 종료는 호스트의 매니저도 함께 종료하므로 Publish 대신 훅 호출 기록으로 구독 여부를 확인한다.
        Global.Instance.Shutdown();

        Check(handler.Calls == 0 && handler.SubscriptionTokens.Count == 0, "A behaviour subscribed while Global was shutting down.");
        Check(fixture.Reported().Count == 0, "Global shutdown cancellation was reported as an error.");
    }

    private static void ExpectedCancellationReportsCleanupFailure()
    {
        using var fixture = new Fixture();
        var cleanupFailure = new ApplicationException("cleanup failure");
        var handler = new UnityEngine.GameObject().Attach(new ExpectedCancellationHandler(cleanupFailure));

        handler.Enable();

        var delivered = fixture.Reported();
        Check(delivered.Count == 1 && ReferenceEquals(delivered[0], cleanupFailure), "The cleanup failure must be reported exactly once.");
        // ErrorReporter는 sink에 내부 예외별로 전달하므로 원래 집계는 콘솔에 한 번 쓴 예외로 확인한다.
        Check(UnityEngine.Debug.Errors.Count == 1, "The console must receive the failure exactly once.");
        var reported = UnityEngine.Debug.Errors[0] as AggregateException;
        Check(reported != null, "The cancellation callback failure must retain its cancellation aggregate.");

        var failures = reported.Flatten().InnerExceptions;
        Check(failures.Count == 1 && ReferenceEquals(failures[0], cleanupFailure),
            $"Expected cancellation leaked into or replaced the cleanup report: {reported}");
    }

    private static void OldFailureCannotCancelNewGeneration()
    {
        using var fixture = new Fixture();
        var expected = new InvalidOperationException("old generation failure");
        var handler = new UnityEngine.GameObject().Attach(new ReentrantHandler(expected));

        handler.Enable();
        fixture.Manager.Publish(First);

        Check(handler.Calls == 1, "The old failed generation canceled the re-enabled subscription.");
        var reported = fixture.Reported();
        Check(reported.Count == 1 && ReferenceEquals(reported[0], expected),
            "The old generation failure was not reported exactly once.");

        handler.Disable();
        fixture.Manager.Publish(First);
        Check(handler.Calls == 1, "The re-enabled subscription did not retain normal cleanup ownership.");
    }

    private static void UnexpectedHookCancellationReports()
    {
        using var fixture = new Fixture();
        using var source = new CancellationTokenSource();
        var handler = new UnityEngine.GameObject().Attach(new FailingHandler(new OperationCanceledException("unexpected hook cancellation", source.Token)));

        handler.Enable();
        fixture.Manager.Publish(First);

        Check(handler.Calls == 0, "An unexpectedly canceled hook retained its partial subscription.");
        var reported = fixture.Reported();
        Check(reported.Count == 1 && reported[0] is OperationCanceledException cancellation &&
            cancellation.CancellationToken == source.Token,
            "Unexpected hook cancellation was not reported exactly once with its original token.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
