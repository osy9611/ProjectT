using System;
using System.Collections.Generic;
using System.Threading;
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
        Run("listeners run in registration order", RegistrationOrder);
        Run("duplicate subscribe preserves the original lifetime and position", DuplicateSubscriptionIsNoOp);
        Run("a canceled owner's listener can be renewed with a new lifetime", RenewAfterOwnerCancellation);
        Run("owner cancellation can renew the same listener with a new lifetime", RenewSubscriptionDuringOwnerCancellation);
        Run("publish mutation uses a stable listener snapshot", SnapshotMutationPolicy);
        Run("owner cancellation suppresses callbacks remaining in a snapshot", OwnerCancellationDuringPublish);
        Run("worker-thread owner cancellation completes without deadlock", WorkerCancellationDuringPublish);
        Run("renewal completes while worker cancellation is pending", () => WorkerCancellationDuringCleanup(false));
        Run("shutdown completes while worker cancellation is pending", () => WorkerCancellationDuringCleanup(true));
        Run("manager lifetime cancellation suppresses callbacks remaining in a snapshot", LifetimeCancellationDuringPublish);
        Run("shutdown during publish suppresses remaining callbacks", ShutdownDuringPublish);
        Run("recursive publish is isolated and deterministic", RecursivePublish);
        Run("listener exceptions escape exactly once", ListenerExceptionPolicy);
        Run("invalid identifiers, listeners, and owners are rejected before mutation", ArgumentValidation);
        Run("notification identifiers remain isolated", IdentifierIsolation);

        Console.WriteLine($"{count - failures}/{count} notification tests passed");
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        count++;
        Exception failure = null;
        try
        {
            test();
        }
        catch (Exception error)
        {
            failure = error;
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
        Action listener = () => { };
        Throws<InvalidOperationException>(() => manager.Subscribe(First, listener, default));
        Throws<InvalidOperationException>(() => manager.Publish(First));

        using var lifetime = new CancellationTokenSource();
        manager.InitializeAsync(lifetime.Token).GetAwaiter().GetResult();
        manager.Subscribe(First, listener, default);
        manager.Shutdown(ShutdownReason.Normal);
        Throws<InvalidOperationException>(() => manager.Subscribe(First, listener, default));
        Throws<InvalidOperationException>(() => manager.Publish(First));

        var canceledManager = new NotificationManager();
        using var canceledLifetime = new CancellationTokenSource();
        canceledManager.InitializeAsync(canceledLifetime.Token).GetAwaiter().GetResult();
        canceledLifetime.Cancel();
        Throws<OperationCanceledException>(() => canceledManager.Subscribe(First, listener, default));
        Throws<OperationCanceledException>(() => canceledManager.Publish(First));
        canceledManager.Shutdown(ShutdownReason.Normal);
    }

    private static void RegistrationOrder()
    {
        using var fixture = new Fixture();
        var calls = new List<string>();
        fixture.Manager.Subscribe(First, () => calls.Add("first"), default);
        fixture.Manager.Subscribe(First, () => calls.Add("second"), default);
        fixture.Manager.Subscribe(First, () => calls.Add("third"), default);

        fixture.Manager.Publish(First);

        Equal("first,second,third", string.Join(",", calls), "Listener order changed.");
    }

    private static void DuplicateSubscriptionIsNoOp()
    {
        using var fixture = new Fixture();
        using var originalOwner = new CancellationTokenSource();
        using var duplicateOwner = new CancellationTokenSource();
        var calls = new List<string>();
        Action duplicate = () => calls.Add("duplicate");
        fixture.Manager.Subscribe(First, duplicate, originalOwner.Token);
        fixture.Manager.Subscribe(First, () => calls.Add("middle"), default);
        fixture.Manager.Subscribe(First, duplicate, duplicateOwner.Token);
        duplicateOwner.Cancel();

        fixture.Manager.Publish(First);
        Equal("duplicate,middle", string.Join(",", calls), "Duplicate subscribe replaced the original position or owner token.");

        calls.Clear();
        using var canceledDuplicateOwner = new CancellationTokenSource();
        canceledDuplicateOwner.Cancel();
        fixture.Manager.Subscribe(First, duplicate, canceledDuplicateOwner.Token);
        fixture.Manager.Publish(First);
        Equal("duplicate,middle", string.Join(",", calls), "A duplicate with a canceled replacement owner changed the original subscription.");

        calls.Clear();
        originalOwner.Cancel();
        fixture.Manager.Publish(First);
        Equal("middle", string.Join(",", calls), "Original owner cancellation did not remove the subscription.");
    }

    private static void RenewAfterOwnerCancellation()
    {
        using var fixture = new Fixture();
        using var oldOwner = new CancellationTokenSource();
        using var newOwner = new CancellationTokenSource();
        var calls = new List<string>();
        Action listener = () => calls.Add("renewed");
        fixture.Manager.Subscribe(First, listener, oldOwner.Token);
        fixture.Manager.Subscribe(First, () => calls.Add("other"), default);

        oldOwner.Cancel();
        fixture.Manager.Subscribe(First, listener, newOwner.Token);
        fixture.Manager.Publish(First);
        Equal("other,renewed", string.Join(",", calls), "The renewed listener was not registered once with the new owner.");

        newOwner.Cancel();
        calls.Clear();
        fixture.Manager.Publish(First);
        Equal("other", string.Join(",", calls), "The renewed subscription did not belong exclusively to the new owner.");
    }

    private static void RenewSubscriptionDuringOwnerCancellation()
    {
        using var fixture = new Fixture();
        using var oldOwner = new CancellationTokenSource();
        using var newOwner = new CancellationTokenSource();
        var calls = new List<string>();
        int renewals = 0;
        Action listener = () => calls.Add("renewed");
        fixture.Manager.Subscribe(First, listener, oldOwner.Token);
        fixture.Manager.Subscribe(First, () => calls.Add("first"), default);
        fixture.Manager.Subscribe(First, () => calls.Add("second"), default);

        // Register last so renewal runs before the old subscription's cancellation callback.
        using var renewal = oldOwner.Token.Register(() =>
        {
            renewals++;
            fixture.Manager.Subscribe(First, listener, newOwner.Token);
        });

        oldOwner.Cancel();
        Check(renewals == 1 && oldOwner.IsCancellationRequested && !newOwner.IsCancellationRequested,
            "Renewal did not run with a canceled old owner and a live new owner.");

        fixture.Manager.Publish(First);
        Equal("first,second,renewed", string.Join(",", calls),
            "The renewed listener was removed by the old owner's cancellation callback.");

        newOwner.Cancel();
        calls.Clear();
        fixture.Manager.Publish(First);
        Equal("first,second", string.Join(",", calls),
            "The renewed subscription did not belong exclusively to the new owner.");
    }

    private static void SnapshotMutationPolicy()
    {
        using var fixture = new Fixture();
        var calls = new List<string>();
        Action added = () => calls.Add("added");
        fixture.Manager.Subscribe(First, () =>
        {
            calls.Add("mutator");
            fixture.Manager.Subscribe(First, added, default);
        }, default);
        fixture.Manager.Subscribe(First, () => calls.Add("existing"), default);

        fixture.Manager.Publish(First);
        Equal("mutator,existing", string.Join(",", calls), "Current publication did not retain its snapshot.");

        calls.Clear();
        fixture.Manager.Publish(First);
        Equal("mutator,existing,added", string.Join(",", calls), "A subscription added during publication did not apply to the next publication.");
    }

    private static void OwnerCancellationDuringPublish()
    {
        using var fixture = new Fixture();
        using var owner = new CancellationTokenSource();
        var calls = new List<string>();
        fixture.Manager.Subscribe(First, () =>
        {
            calls.Add("cancel");
            owner.Cancel();
        }, default);
        fixture.Manager.Subscribe(First, () => calls.Add("dead-owner"), owner.Token);

        fixture.Manager.Publish(First);

        Equal("cancel", string.Join(",", calls), "A canceled owner was invoked later in the same publication.");
    }

    private static void WorkerCancellationDuringPublish()
    {
        using var fixture = new Fixture();
        using var owner = new CancellationTokenSource();
        var calls = new List<string>();
        Exception workerError = null;
        fixture.Manager.Subscribe(First, () =>
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
        }, default);
        fixture.Manager.Subscribe(First, () => calls.Add("dead-owner"), owner.Token);

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
        Action listener = () => received++;
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
                fixture.Manager.Shutdown(ShutdownReason.Normal);
            else
                fixture.Manager.Subscribe(First, listener, newOwner.Token);
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
        manager.Subscribe(First, () =>
        {
            calls.Add("cancel-lifetime");
            lifetime.Cancel();
        }, default);
        manager.Subscribe(First, () => calls.Add("late"), default);

        manager.Publish(First);

        Equal("cancel-lifetime", string.Join(",", calls), "Manager lifetime cancellation allowed a remaining callback.");
        manager.Shutdown(ShutdownReason.Normal);
    }

    private static void ShutdownDuringPublish()
    {
        using var fixture = new Fixture();
        var calls = new List<string>();
        fixture.Manager.Subscribe(First, () =>
        {
            calls.Add("shutdown");
            fixture.Manager.Shutdown(ShutdownReason.Normal);
        }, default);
        fixture.Manager.Subscribe(First, () => calls.Add("late"), default);

        fixture.Manager.Publish(First);

        Equal("shutdown", string.Join(",", calls), "Shutdown allowed a remaining snapshot callback.");
    }

    private static void RecursivePublish()
    {
        using var fixture = new Fixture();
        var calls = new List<string>();
        var nesting = 0;
        fixture.Manager.Subscribe(First, () =>
        {
            calls.Add("A" + nesting);
            if (nesting == 0)
            {
                nesting++;
                fixture.Manager.Publish(First);
                nesting--;
            }
        }, default);
        fixture.Manager.Subscribe(First, () => calls.Add("B" + nesting), default);

        fixture.Manager.Publish(First);

        Equal("A0,A1,B1,B0", string.Join(",", calls), "Recursive publication corrupted the outer iteration.");
    }

    private static void ListenerExceptionPolicy()
    {
        using var fixture = new Fixture();
        var expected = new InvalidOperationException("listener");
        var invocations = 0;
        fixture.Manager.Subscribe(First, () =>
        {
            invocations++;
            throw expected;
        }, default);
        fixture.Manager.Subscribe(First, () => invocations++, default);

        var actual = Throws<InvalidOperationException>(() => fixture.Manager.Publish(First));

        Check(ReferenceEquals(expected, actual), "Listener exception was wrapped or replaced.");
        Check(invocations == 1, "Publication continued or retried after a listener exception.");
    }

    private static void ArgumentValidation()
    {
        using var fixture = new Fixture();
        using var canceledOwner = new CancellationTokenSource();
        canceledOwner.Cancel();
        int calls = 0;
        Action listener = () => calls++;
        Throws<ArgumentOutOfRangeException>(() => fixture.Manager.Subscribe(NotificationId.None, listener, default));
        Throws<ArgumentOutOfRangeException>(() => fixture.Manager.Publish(NotificationId.None));
        Throws<ArgumentNullException>(() => fixture.Manager.Subscribe(First, null, default));
        Throws<OperationCanceledException>(() => fixture.Manager.Subscribe(First, listener, canceledOwner.Token));
        fixture.Manager.Publish(First);
        Check(calls == 0, "A rejected subscription was registered.");
    }

    private static void IdentifierIsolation()
    {
        using var fixture = new Fixture();
        var calls = 0;
        fixture.Manager.Subscribe(First, () => calls++, default);
        fixture.Manager.Publish(Second);
        Check(calls == 0, "A listener received a different notification identifier.");
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
