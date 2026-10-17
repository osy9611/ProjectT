using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT;

internal static class Program
{
    private static readonly NotificationId First = (NotificationId)1;
    private static readonly NotificationId Second = (NotificationId)2;
    private static int count;
    private static int failures;

    private sealed class Fixture : IDisposable
    {
        public readonly CancellationTokenSource Lifetime = new CancellationTokenSource();
        public readonly NotificationManager Manager = new NotificationManager();

        public Fixture()
        {
            Manager.InitializeAsync(Lifetime.Token).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            if (Manager.State != ManagerState.Stopped)
                Manager.Shutdown(ShutdownReason.Normal);
            Lifetime.Cancel();
            Lifetime.Dispose();
        }
    }

    private static int Main()
    {
        Run("ready lifecycle gates new operations and shutdown clears subscriptions", LifecyclePolicy);
        Run("priority is descending and ties preserve registration order", StablePriorityOrder);
        Run("duplicate subscribe preserves the original lifetime and priority", DuplicateSubscriptionIsNoOp);
        Run("owner cancellation can renew the same listener with a new lifetime", RenewSubscriptionDuringOwnerCancellation);
        Run("unsubscribe removes only the requested listener and is idempotent", UnsubscribePolicy);
        Run("publish mutation uses a stable listener snapshot", SnapshotMutationPolicy);
        Run("owner cancellation suppresses callbacks remaining in a snapshot", OwnerCancellationDuringPublish);
        Run("owner cancellation survives manual unsubscribe during publish", OwnerCancellationAfterUnsubscribe);
        Run("worker-thread owner cancellation completes without deadlock", WorkerCancellationDuringPublish);
        Run("unsubscribe and renewal complete while worker cancellation is pending", () => WorkerCancellationDuringCleanup(false));
        Run("shutdown completes while worker cancellation is pending", () => WorkerCancellationDuringCleanup(true));
        Run("manager lifetime cancellation suppresses callbacks remaining in a snapshot", LifetimeCancellationDuringPublish);
        Run("shutdown during publish suppresses remaining callbacks", ShutdownDuringPublish);
        Run("recursive publish is isolated and deterministic", RecursivePublish);
        Run("listener exceptions escape exactly once", ListenerExceptionPolicy);
        Run("null argument arrays normalize to empty arrays", NullArgumentsNormalizeToEmpty);
        Run("null payload elements pass through unchanged", NullPayloadElementIsPreserved);
        Run("invalid identifiers and listeners are rejected before mutation", ArgumentValidation);
        Run("notification identifiers remain isolated", IdentifierIsolation);
        Run("next-frame publish waits one frame and copies arguments", NextFrameAndArgumentCopy);
        Run("delayed publish waits for its duration and copies arguments", DelayAndArgumentCopy);
        Run("zero-second publication waits until the next frame", ZeroDelayUsesNextFrame);
        Run("non-finite and negative delays are rejected", InvalidDelayPolicy);
        Run("caller cancellation prevents delayed publication", CallerCancellation);
        Run("manager lifetime cancellation prevents delayed publication", ManagerCancellation);
        Run("scheduled validation returns failed tasks and preserves validation order", ScheduledValidationOrder);
        foreach (int mode in new[] { 0, 1, 2 })
        {
            string prefix = mode == 0 ? "next-frame" : mode == 1 ? "zero-second" : "positive-delay";
            Run(prefix + " schedule preserves shallow and empty argument ownership", () => ScheduledArgumentOwnership(mode));
            Run(prefix + " schedule returns listener exceptions through its task", () => ScheduledListenerException(mode));
            Run(prefix + " schedule observes already canceled caller tokens", () => ScheduledPreCancellation(mode));
            Run(prefix + " schedule observes caller and lifetime cancellation while waiting", () => ScheduledCancellationWhileWaiting(mode));
            Run(prefix + " schedule rejects publication after shutdown without token cancellation", () => ScheduledShutdown(mode));
        }
        BehaviourTests.RunAll(Run);

        Console.WriteLine($"{count - failures}/{count} notification tests passed");
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        count++;
        Exception failure = null;
        try
        {
            ManualNotificationScheduler.Reset();
            test();
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            try
            {
                ManualNotificationScheduler.Reset();
            }
            catch (Exception error)
            {
                failure = failure == null ? error : new AggregateException(failure, error);
            }
        }

        if (failure == null)
        {
            Console.WriteLine("PASS " + name);
        }
        else
        {
            failures++;
            Console.WriteLine("FAIL " + name + " :: " + failure);
        }
    }

    private static void LifecyclePolicy()
    {
        var manager = new NotificationManager();
        Action<object[]> listener = _ => { };
        Throws<InvalidOperationException>(() => manager.Subscribe(First, listener));
        Throws<InvalidOperationException>(() => manager.Publish(First));
        ThrowsAsync<InvalidOperationException>(() => manager.PublishNextFrameAsync(First));

        using var lifetime = new CancellationTokenSource();
        manager.InitializeAsync(lifetime.Token).GetAwaiter().GetResult();
        manager.Subscribe(First, listener);
        manager.Shutdown(ShutdownReason.Normal);
        manager.Unsubscribe(First, listener);
        Throws<InvalidOperationException>(() => manager.Subscribe(First, listener));
        Throws<InvalidOperationException>(() => manager.Publish(First));
        ThrowsAsync<InvalidOperationException>(() => manager.PublishAfterSecondsAsync(First, 1f));

        var canceledManager = new NotificationManager();
        using var canceledLifetime = new CancellationTokenSource();
        canceledManager.InitializeAsync(canceledLifetime.Token).GetAwaiter().GetResult();
        canceledLifetime.Cancel();
        Throws<OperationCanceledException>(() => canceledManager.Subscribe(First, listener));
        Throws<OperationCanceledException>(() => canceledManager.Publish(First));
        ThrowsAsync<OperationCanceledException>(() => canceledManager.PublishNextFrameAsync(First));
        canceledManager.Unsubscribe(First, listener);
        canceledManager.Shutdown(ShutdownReason.Normal);
    }

    private static void StablePriorityOrder()
    {
        using var fixture = new Fixture();
        var calls = new List<string>();
        fixture.Manager.Subscribe(First, _ => calls.Add("low-first"), priority: -1);
        fixture.Manager.Subscribe(First, _ => calls.Add("high-first"), priority: 10);
        fixture.Manager.Subscribe(First, _ => calls.Add("high-second"), priority: 10);
        fixture.Manager.Subscribe(First, _ => calls.Add("normal"));

        fixture.Manager.Publish(First);

        Equal("high-first,high-second,normal,low-first", string.Join(",", calls), "Listener order changed.");
    }

    private static void DuplicateSubscriptionIsNoOp()
    {
        using var fixture = new Fixture();
        using var originalOwner = new CancellationTokenSource();
        using var duplicateOwner = new CancellationTokenSource();
        var calls = new List<string>();
        Action<object[]> duplicate = _ => calls.Add("duplicate");
        fixture.Manager.Subscribe(First, _ => calls.Add("middle"), priority: 5);
        fixture.Manager.Subscribe(First, duplicate, originalOwner.Token, 10);
        fixture.Manager.Subscribe(First, duplicate, duplicateOwner.Token, -10);
        duplicateOwner.Cancel();

        fixture.Manager.Publish(First);
        Equal("duplicate,middle", string.Join(",", calls), "Duplicate subscribe replaced the original priority or owner token.");

        calls.Clear();
        using var canceledDuplicateOwner = new CancellationTokenSource();
        canceledDuplicateOwner.Cancel();
        fixture.Manager.Subscribe(First, duplicate, canceledDuplicateOwner.Token, 100);
        fixture.Manager.Publish(First);
        Equal("duplicate,middle", string.Join(",", calls), "A duplicate with a canceled replacement owner changed the original subscription.");

        calls.Clear();
        originalOwner.Cancel();
        fixture.Manager.Publish(First);
        Equal("middle", string.Join(",", calls), "Original owner cancellation did not remove the subscription.");

        Action<object[]> canceledNewListener = _ => { };
        Throws<OperationCanceledException>(() => fixture.Manager.Subscribe(First, canceledNewListener, canceledDuplicateOwner.Token));
    }

    private static void RenewSubscriptionDuringOwnerCancellation()
    {
        using var fixture = new Fixture();
        using var oldOwner = new CancellationTokenSource();
        using var newOwner = new CancellationTokenSource();
        var calls = new List<string>();
        int renewals = 0;
        Action<object[]> listener = _ => calls.Add("renewed");
        fixture.Manager.Subscribe(First, listener, oldOwner.Token, -10);
        fixture.Manager.Subscribe(First, _ => calls.Add("same-priority-first"), priority: 10);
        fixture.Manager.Subscribe(First, _ => calls.Add("low"), priority: -5);

        // Register last so renewal runs before the old subscription's cancellation callback.
        using var renewal = oldOwner.Token.Register(() =>
        {
            renewals++;
            fixture.Manager.Subscribe(First, listener, newOwner.Token, 10);
        });

        oldOwner.Cancel();
        Check(renewals == 1 && oldOwner.IsCancellationRequested && !newOwner.IsCancellationRequested,
            "Renewal did not run with a canceled old owner and a live new owner.");

        fixture.Manager.Publish(First);
        Equal("same-priority-first,renewed,low", string.Join(",", calls),
            "The renewed listener lost its new priority or stable registration order.");

        newOwner.Cancel();
        calls.Clear();
        fixture.Manager.Publish(First);
        Equal("same-priority-first,low", string.Join(",", calls),
            "The renewed subscription did not belong exclusively to the new owner.");
    }

    private static void UnsubscribePolicy()
    {
        using var fixture = new Fixture();
        var first = 0;
        var second = 0;
        Action<object[]> firstListener = _ => first++;
        Action<object[]> secondListener = _ => second++;
        fixture.Manager.Subscribe(First, firstListener);
        fixture.Manager.Subscribe(First, secondListener);
        fixture.Manager.Subscribe(Second, firstListener);

        fixture.Manager.Unsubscribe(First, firstListener);
        fixture.Manager.Unsubscribe(First, firstListener);
        fixture.Manager.Publish(First);
        fixture.Manager.Publish(Second);

        Check(first == 1 && second == 1, "Unsubscribe affected another listener or identifier.");
    }

    private static void SnapshotMutationPolicy()
    {
        using var fixture = new Fixture();
        using var owner = new CancellationTokenSource();
        var calls = new List<string>();
        Action<object[]> removed = _ => calls.Add("removed");
        Action<object[]> added = _ => calls.Add("added");
        Action<object[]> mutator = null;
        mutator = _ =>
        {
            calls.Add("mutator");
            fixture.Manager.Unsubscribe(First, removed);
            fixture.Manager.Subscribe(First, added);
            fixture.Manager.Unsubscribe(First, mutator);
        };
        fixture.Manager.Subscribe(First, mutator, priority: 10);
        fixture.Manager.Subscribe(First, removed, owner.Token);

        fixture.Manager.Publish(First);
        Equal("mutator,removed", string.Join(",", calls), "Current publication did not retain its manual-mutation snapshot.");
        Check(!owner.IsCancellationRequested, "Manual unsubscribe must not cancel the owner's token.");

        calls.Clear();
        fixture.Manager.Publish(First);
        Equal("added", string.Join(",", calls), "Manual mutations did not apply to the next publication.");
    }

    private static void OwnerCancellationDuringPublish()
    {
        using var fixture = new Fixture();
        using var owner = new CancellationTokenSource();
        var calls = new List<string>();
        fixture.Manager.Subscribe(First, _ =>
        {
            calls.Add("cancel");
            owner.Cancel();
        }, priority: 10);
        fixture.Manager.Subscribe(First, _ => calls.Add("dead-owner"), owner.Token);

        fixture.Manager.Publish(First);

        Equal("cancel", string.Join(",", calls), "A canceled owner was invoked later in the same publication.");
    }

    private static void OwnerCancellationAfterUnsubscribe()
    {
        using var fixture = new Fixture();
        using var owner = new CancellationTokenSource();
        var calls = new List<string>();
        Action<object[]> owned = _ => calls.Add("dead-owner");
        fixture.Manager.Subscribe(First, _ =>
        {
            calls.Add("remove-and-cancel");
            fixture.Manager.Unsubscribe(First, owned);
            owner.Cancel();
        }, priority: 10);
        fixture.Manager.Subscribe(First, owned, owner.Token);

        fixture.Manager.Publish(First);

        Equal("remove-and-cancel", string.Join(",", calls), "Disposing registration during unsubscribe lost the owner's canceled state for the active snapshot.");
    }

    private static void WorkerCancellationDuringPublish()
    {
        using var fixture = new Fixture();
        using var owner = new CancellationTokenSource();
        var calls = new List<string>();
        Exception workerError = null;
        fixture.Manager.Subscribe(First, _ =>
        {
            calls.Add("cancel-worker");
            var worker = new Thread(() =>
            {
                try
                {
                    owner.Cancel();
                }
                catch (Exception error)
                {
                    workerError = error;
                }
            });
            worker.Start();
            Check(worker.Join(TimeSpan.FromSeconds(2)), "Worker cancellation deadlocked against publication.");
        }, priority: 10);
        fixture.Manager.Subscribe(First, _ => calls.Add("dead-owner"), owner.Token);

        fixture.Manager.Publish(First);

        if (workerError != null)
            throw new Exception("Worker cancellation failed.", workerError);
        Equal("cancel-worker", string.Join(",", calls), "Worker-canceled owner received a later callback.");
    }

    private static void WorkerCancellationDuringCleanup(bool shutdown)
    {
        using var fixture = new Fixture();
        using var oldOwner = new CancellationTokenSource();
        using var newOwner = new CancellationTokenSource();
        using var cancellationEntered = new ManualResetEventSlim();
        using var resumeCancellation = new ManualResetEventSlim();
        int received = 0;
        Action<object[]> listener = _ => received++;
        fixture.Manager.Subscribe(First, listener, oldOwner.Token);
        fixture.Manager.Subscribe(Second, listener, oldOwner.Token);

        // Cancellation callbacks run last-in-first-out; pause before the manager's callbacks.
        using var gate = oldOwner.Token.Register(() =>
        {
            cancellationEntered.Set();
            if (!resumeCancellation.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Main-thread cleanup did not release cancellation.");
        });

        Exception workerError = null;
        var worker = new Thread(() =>
        {
            try
            {
                oldOwner.Cancel();
            }
            catch (Exception error)
            {
                workerError = error;
            }
        }) { IsBackground = true };

        worker.Start();
        try
        {
            Check(cancellationEntered.Wait(TimeSpan.FromSeconds(5)), "Worker did not enter cancellation.");
            if (shutdown)
            {
                fixture.Manager.Shutdown(ShutdownReason.Normal);
            }
            else
            {
                fixture.Manager.Unsubscribe(First, listener);
                fixture.Manager.Unsubscribe(First, listener);
                fixture.Manager.Subscribe(First, listener, newOwner.Token);
            }
        }
        finally
        {
            resumeCancellation.Set();
            Check(worker.Join(TimeSpan.FromSeconds(5)), "Worker cancellation did not complete after cleanup.");
        }

        if (workerError != null)
            throw new Exception("Worker cancellation failed during cleanup.", workerError);

        Check(received == 0, "Cleanup must not publish notifications.");
        if (shutdown)
        {
            Check(fixture.Manager.State == ManagerState.Stopped, "Shutdown did not reach Stopped.");
            fixture.Manager.Unsubscribe(First, listener);
            fixture.Manager.Unsubscribe(Second, listener);
            return;
        }

        fixture.Manager.Publish(First);
        fixture.Manager.Publish(Second);
        Check(received == 1, "Old cancellation removed the renewed listener or left an old listener active.");
        newOwner.Cancel();
        fixture.Manager.Publish(First);
        Check(received == 1, "Renewed listener was not owned by the new token.");
    }

    private static void LifetimeCancellationDuringPublish()
    {
        using var lifetime = new CancellationTokenSource();
        var manager = new NotificationManager();
        manager.InitializeAsync(lifetime.Token).GetAwaiter().GetResult();
        var calls = new List<string>();
        manager.Subscribe(First, _ =>
        {
            calls.Add("cancel-lifetime");
            lifetime.Cancel();
        }, priority: 10);
        manager.Subscribe(First, _ => calls.Add("late"));

        manager.Publish(First);

        Equal("cancel-lifetime", string.Join(",", calls), "Manager lifetime cancellation allowed a remaining callback.");
        manager.Shutdown(ShutdownReason.Normal);
    }

    private static void ShutdownDuringPublish()
    {
        using var fixture = new Fixture();
        var calls = new List<string>();
        fixture.Manager.Subscribe(First, _ =>
        {
            calls.Add("shutdown");
            fixture.Manager.Shutdown(ShutdownReason.Normal);
        }, priority: 10);
        fixture.Manager.Subscribe(First, _ => calls.Add("late"));

        fixture.Manager.Publish(First);

        Equal("shutdown", string.Join(",", calls), "Shutdown allowed a remaining snapshot callback.");
    }

    private static void RecursivePublish()
    {
        using var fixture = new Fixture();
        var calls = new List<string>();
        var nesting = 0;
        fixture.Manager.Subscribe(First, _ =>
        {
            calls.Add("A" + nesting);
            if (nesting == 0)
            {
                nesting++;
                fixture.Manager.Publish(First);
                nesting--;
            }
        }, priority: 10);
        fixture.Manager.Subscribe(First, _ => calls.Add("B" + nesting));

        fixture.Manager.Publish(First);

        Equal("A0,A1,B1,B0", string.Join(",", calls), "Recursive publication corrupted the outer iteration.");
    }

    private static void ListenerExceptionPolicy()
    {
        using var fixture = new Fixture();
        var expected = new InvalidOperationException("listener");
        var invocations = 0;
        fixture.Manager.Subscribe(First, _ =>
        {
            invocations++;
            throw expected;
        }, priority: 10);
        fixture.Manager.Subscribe(First, _ => invocations++);

        var actual = Throws<InvalidOperationException>(() => fixture.Manager.Publish(First));

        Check(ReferenceEquals(expected, actual), "Listener exception was wrapped or replaced.");
        Check(invocations == 1, "Publication continued or retried after a listener exception.");
    }

    private static void NullArgumentsNormalizeToEmpty()
    {
        using var fixture = new Fixture();
        object[] received = null;
        fixture.Manager.Subscribe(First, args => received = args);

        fixture.Manager.Publish(First, (object[])null);

        Check(received != null && received.Length == 0, "Null params reached a listener without normalization.");
    }

    private static void NullPayloadElementIsPreserved()
    {
        using var fixture = new Fixture();
        object[] received = null;
        fixture.Manager.Subscribe(First, args => received = args);

        fixture.Manager.Publish(First, "before", null, "after");

        Check(received != null && received.Length == 3 && (string)received[0] == "before" && received[1] == null && (string)received[2] == "after",
            "A null payload element was removed or replaced.");
    }

    private static void ArgumentValidation()
    {
        using var fixture = new Fixture();
        Action<object[]> listener = _ => { };
        Throws<ArgumentOutOfRangeException>(() => fixture.Manager.Subscribe(NotificationId.None, listener));
        Throws<ArgumentOutOfRangeException>(() => fixture.Manager.Unsubscribe(NotificationId.None, listener));
        Throws<ArgumentOutOfRangeException>(() => fixture.Manager.Publish(NotificationId.None));
        ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Manager.PublishNextFrameAsync(NotificationId.None));
        ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Manager.PublishAfterSecondsAsync(NotificationId.None, 1f));
        Throws<ArgumentNullException>(() => fixture.Manager.Subscribe(First, null));
        Throws<ArgumentNullException>(() => fixture.Manager.Unsubscribe(First, null));
        Check(ManualNotificationScheduler.PendingCount == 0, "Invalid arguments scheduled asynchronous work.");
    }

    private static void IdentifierIsolation()
    {
        using var fixture = new Fixture();
        var calls = 0;
        fixture.Manager.Subscribe(First, _ => calls++);
        fixture.Manager.Publish(Second);
        Check(calls == 0, "A listener received a different notification identifier.");
    }

    private static void NextFrameAndArgumentCopy()
    {
        using var fixture = new Fixture();
        object received = null;
        fixture.Manager.Subscribe(First, args => received = args[0]);
        var args = new object[] { "original" };
        var publish = fixture.Manager.PublishNextFrameAsync(First, default, args);
        args[0] = "mutated";

        Check(received == null && !publish.Status.IsCompleted(), "Next-frame publication completed synchronously.");
        ManualNotificationScheduler.StepFrame();
        publish.GetAwaiter().GetResult();

        Equal("original", received as string, "Next-frame publication retained the caller's mutable array.");
    }

    private static void DelayAndArgumentCopy()
    {
        using var fixture = new Fixture();
        object received = null;
        fixture.Manager.Subscribe(First, args => received = args[0]);
        var args = new object[] { "original" };
        var publish = fixture.Manager.PublishAfterSecondsAsync(First, 1f, default, args);
        args[0] = "mutated";

        ManualNotificationScheduler.AdvanceSeconds(0.5d);
        Check(received == null && !publish.Status.IsCompleted(), "Delayed publication completed too early.");
        ManualNotificationScheduler.AdvanceSeconds(0.5d);
        publish.GetAwaiter().GetResult();

        Equal("original", received as string, "Delayed publication retained the caller's mutable array.");
    }

    private static void ZeroDelayUsesNextFrame()
    {
        using var fixture = new Fixture();
        var calls = 0;
        fixture.Manager.Subscribe(First, _ => calls++);
        var publish = fixture.Manager.PublishAfterSecondsAsync(First, 0f);

        ManualNotificationScheduler.AdvanceSeconds(0d);
        Check(calls == 0 && !publish.Status.IsCompleted(), "A zero-second delay did not preserve next-frame semantics.");
        ManualNotificationScheduler.StepFrame();
        publish.GetAwaiter().GetResult();
        Check(calls == 1, "A zero-second delay did not publish on the next frame.");
    }

    private static void InvalidDelayPolicy()
    {
        using var fixture = new Fixture();
        ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Manager.PublishAfterSecondsAsync(First, -0.01f));
        ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Manager.PublishAfterSecondsAsync(First, float.NaN));
        ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Manager.PublishAfterSecondsAsync(First, float.PositiveInfinity));
        ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Manager.PublishAfterSecondsAsync(First, float.NegativeInfinity));
        Check(ManualNotificationScheduler.PendingCount == 0, "An invalid delay scheduled asynchronous work.");
    }

    private static void CallerCancellation()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        fixture.Manager.Subscribe(First, _ => calls++);
        var publish = fixture.Manager.PublishNextFrameAsync(First, cancellation.Token);

        cancellation.Cancel();
        ManualNotificationScheduler.StepFrame();
        ThrowsAsync<OperationCanceledException>(() => publish);
        Check(calls == 0, "Caller cancellation still published a notification.");
    }

    private static void ManagerCancellation()
    {
        using var lifetime = new CancellationTokenSource();
        var manager = new NotificationManager();
        manager.InitializeAsync(lifetime.Token).GetAwaiter().GetResult();
        var calls = 0;
        manager.Subscribe(First, _ => calls++);
        var publish = manager.PublishAfterSecondsAsync(First, 1f);

        lifetime.Cancel();
        ManualNotificationScheduler.AdvanceSeconds(1d);
        ThrowsAsync<OperationCanceledException>(() => publish);
        Check(calls == 0, "Manager lifetime cancellation still published a notification.");
        manager.Shutdown(ShutdownReason.Normal);
    }

    private static UniTask Schedule(NotificationManager manager, int mode, CancellationToken token = default, params object[] args)
    {
        return mode == 0 ? manager.PublishNextFrameAsync(First, token, args)
            : manager.PublishAfterSecondsAsync(First, mode == 1 ? 0f : 1f, token, args);
    }

    private static void CompleteSchedule(int mode)
    {
        if (mode == 2)
            ManualNotificationScheduler.AdvanceSeconds(1d);
        else
            ManualNotificationScheduler.StepFrame();
    }

    private static void ScheduledValidationOrder()
    {
        var manager = new NotificationManager();
        var invalidId = manager.PublishNextFrameAsync(NotificationId.None);
        Check(invalidId.Status == UniTaskStatus.Faulted, "Invalid ID did not return a faulted UniTask.");
        Equal("id", ThrowsAsync<ArgumentOutOfRangeException>(() => invalidId).ParamName, "ID validation order changed.");
        var notReady = manager.PublishNextFrameAsync(First);
        ThrowsAsync<InvalidOperationException>(() => notReady);
        var invalidBoth = manager.PublishAfterSecondsAsync(NotificationId.None, -1f);
        Equal("id", ThrowsAsync<ArgumentOutOfRangeException>(() => invalidBoth).ParamName, "Seconds were validated before ID.");
        var invalidSeconds = manager.PublishAfterSecondsAsync(First, -1f);
        Equal("seconds", ThrowsAsync<ArgumentOutOfRangeException>(() => invalidSeconds).ParamName, "Readiness was validated before seconds.");

        using var fixture = new Fixture();
        fixture.Lifetime.Cancel();
        var canceledInvalidId = fixture.Manager.PublishNextFrameAsync(NotificationId.None);
        Equal("id", ThrowsAsync<ArgumentOutOfRangeException>(() => canceledInvalidId).ParamName, "Lifetime cancellation preceded ID validation.");
        foreach (int mode in new[] { 0, 1, 2 })
        {
            var canceled = Schedule(fixture.Manager, mode);
            Check(canceled.Status == UniTaskStatus.Canceled, "Canceled lifetime did not return a canceled task.");
            var error = ThrowsAsync<OperationCanceledException>(() => canceled);
            Check(error.CancellationToken == fixture.Lifetime.Token, "Admission lost the lifetime cancellation token.");
        }
        Check(ManualNotificationScheduler.PendingCount == 0, "Rejected work created a pending wait.");
    }

    private static void ScheduledArgumentOwnership(int mode)
    {
        using var fixture = new Fixture();
        object[] received = null;
        fixture.Manager.Subscribe(First, args => received = args);
        var payload = new List<int> { 1 };
        var input = new object[] { payload, null };
        var task = Schedule(fixture.Manager, mode, default, input);
        input[0] = "replaced";
        payload.Add(2);
        Check(received == null && !task.Status.IsCompleted(), "Scheduling published synchronously.");
        CompleteSchedule(mode);
        task.GetAwaiter().GetResult();
        Check(!ReferenceEquals(input, received) && ReferenceEquals(received[0], payload) && payload.Count == 2 && received[1] == null,
            "The outer array must be copied immediately while nested objects and null elements stay unchanged.");

        foreach (object[] empty in new[] { (object[])null, Array.Empty<object>() })
        {
            received = null;
            var emptyTask = Schedule(fixture.Manager, mode, default, empty);
            CompleteSchedule(mode);
            emptyTask.GetAwaiter().GetResult();
            Check(received != null && received.Length == 0, "Empty scheduled arguments were not normalized.");
        }
    }

    private static void ScheduledListenerException(int mode)
    {
        using var fixture = new Fixture();
        var expected = new InvalidOperationException("scheduled listener failure");
        int calls = 0;
        int laterCalls = 0;
        fixture.Manager.Subscribe(First, _ => { calls++; throw expected; }, priority: 1);
        fixture.Manager.Subscribe(First, _ => laterCalls++);
        var task = Schedule(fixture.Manager, mode);
        CompleteSchedule(mode);
        Check(task.Status == UniTaskStatus.Faulted, "Listener failure did not fault the returned task.");
        var actual = ThrowsAsync<InvalidOperationException>(() => task);
        Check(ReferenceEquals(expected, actual) && calls == 1 && laterCalls == 0, "Listener failure was wrapped, retried, or swallowed.");
        Check(ManualNotificationScheduler.PendingCount == 0, "A failed publication left pending work.");
    }

    private static void ScheduledPreCancellation(int mode)
    {
        using var fixture = new Fixture();
        using var caller = new CancellationTokenSource();
        int calls = 0;
        fixture.Manager.Subscribe(First, _ => calls++);
        caller.Cancel();
        var task = Schedule(fixture.Manager, mode, caller.Token);
        Check(task.Status == UniTaskStatus.Canceled, "Pre-canceled caller did not return a canceled task.");
        var error = ThrowsAsync<OperationCanceledException>(() => task);
        Check(error.CancellationToken.IsCancellationRequested && calls == 0 && ManualNotificationScheduler.PendingCount == 0,
            "Pre-canceled scheduling published or queued work.");
    }

    private static void ScheduledCancellationWhileWaiting(int mode)
    {
        foreach (bool cancelLifetime in new[] { false, true })
        {
            using var fixture = new Fixture();
            using var caller = new CancellationTokenSource();
            int calls = 0;
            fixture.Manager.Subscribe(First, _ => calls++);
            var task = Schedule(fixture.Manager, mode, caller.Token);
            if (cancelLifetime)
                fixture.Lifetime.Cancel();
            else
                caller.Cancel();
            Check(!task.Status.IsCompleted(), "Cancellation became immediate instead of being observed by the player-loop wait.");
            ManualNotificationScheduler.StepFrame();
            var error = ThrowsAsync<OperationCanceledException>(() => task);
            Check(error.CancellationToken.IsCancellationRequested && calls == 0 && ManualNotificationScheduler.PendingCount == 0,
                "Cancellation did not suppress publication and complete the wait.");
        }
    }

    private static void ScheduledShutdown(int mode)
    {
        using var fixture = new Fixture();
        int calls = 0;
        fixture.Manager.Subscribe(First, _ => calls++);
        var task = Schedule(fixture.Manager, mode);
        fixture.Manager.Shutdown(ShutdownReason.Normal);
        CompleteSchedule(mode);
        ThrowsAsync<InvalidOperationException>(() => task);
        Check(calls == 0 && ManualNotificationScheduler.PendingCount == 0, "A stopped manager accepted scheduled publication.");
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T error)
        {
            return error;
        }

        throw new Exception("Expected " + typeof(T).Name + ".");
    }

    private static T ThrowsAsync<T>(Func<UniTask> action) where T : Exception
    {
        try
        {
            action().GetAwaiter().GetResult();
        }
        catch (T error)
        {
            return error;
        }

        throw new Exception("Expected " + typeof(T).Name + ".");
    }

    private static void Equal(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new Exception(message + " Expected: " + expected + ", actual: " + actual);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
