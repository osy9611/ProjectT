# Notification regression tests

`NotificationTests.csproj` compiles the production `NotificationManager`, `NotificationId`, lifecycle base, and `NotifyHandlerBehaviour` against the restored UniTask package. The next-frame, delay, and readiness player-loop boundaries are replaced with a deterministic scheduler, so this harness does not claim full Unity PlayerLoop coverage.

The public publish API is intended for the Unity main thread. Owner token cancellation may arrive from a worker thread, and the harness verifies that path does not deadlock. It also covers lifecycle admission, stable priority ordering, duplicate subscriptions, snapshot mutation, owner and manager cancellation, shutdown during publication, recursive publication, exception propagation, argument validation, identifier isolation, delayed argument ownership, and delayed cancellation.

The behaviour tests cover immediate and deferred Global readiness, disabling while readiness is pending, cancellation-backed unsubscription, re-enabling with a fresh owner token, destruction cleanup, partial subscription rollback, and a re-entrant disable/re-enable generation race.

Latest R1 regression assessment (2026-09-27): **54/54 passed before and after** reusing the zero-second scheduling path for next-frame publication. Sixteen added cases cover returned-task validation errors and next-frame/zero/positive-delay argument ownership, listener exceptions, pre-cancellation, cancellation while waiting, and shutdown. Runtime/editor Unity-reference compilation passed. See [the R1 report](../../reports/tests/NotificationTests/ScheduledPublishReport.md) for review details and PlayerLoop coverage limits.

Earlier A2 regression assessment (2026-09-27): **38/38 passed before and after** extracting the cancellation-registration handoff. The existing snapshot test now uses a cancelable owner. Two new scenarios pause worker cancellation before the manager callbacks and verify manual unsubscribe/renewal or shutdown while cancellation is pending. See [the A2 report](../../reports/tests/NotificationTests/CancellationRegistrationReport.md) for the final review and coverage limits.

The earlier **36/36** baseline followed corrections to reproduced production defects:

- `RenewSubscriptionDuringOwnerCancellation`: renewal from the old owner's cancellation callback uses the new owner and priority, preserves tie ordering, and unsubscribes when the new owner is canceled.
- `SubscriptionAndRollbackFailuresArePreserved`: subscription and cleanup failures retain their original exception objects/stacks in one report, while partial subscriptions are removed.
- `ExpectedCancellationReportsCleanupFailure`: expected shutdown cancellation remains silent, while a failing cleanup callback is reported exactly once.

Unity-reference runtime/editor compilation passed again for A2. The earlier lifecycle result was 107 passed with one pre-existing root-parent contract failure; all six contract tests passed. That suite was not rerun for A2. See `RefactoringReport.md` for the earlier scope and limitations.

Run from PowerShell:

```powershell
dotnet run --project Tools/NotificationTests
```

To compare an original source snapshot with the current implementation, run once with each source:

```powershell
dotnet run --project Tools/NotificationTests -p:NotificationManagerPath='<original NotificationManager.cs>'
dotnet run --project Tools/NotificationTests
```

The test runner uses real .NET cancellation tokens and worker threads. Unity readiness/frame boundaries remain stubs; the controlled cancellation cases do not prove all possible thread interleavings or in-flight callback/Dispose races on Mono or IL2CPP.
