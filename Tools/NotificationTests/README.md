# Notification regression tests

`NotificationTests.csproj` compiles the production `NotificationManager`, `NotificationId`, and lifecycle base against the restored UniTask package.

The public API is intended for the Unity main thread. Owner token cancellation may arrive from a worker thread, and the harness verifies that path does not deadlock. It covers lifecycle admission, registration order, duplicate subscriptions, renewal after owner cancellation, snapshot mutation, owner, worker-thread, and manager cancellation during publication, shutdown during publication, recursive publication, exception propagation, argument validation, and identifier isolation.

Run from PowerShell:

```powershell
dotnet run --project Tools/NotificationTests
```

To compare an original source snapshot with the current implementation, run once with each source:

```powershell
dotnet run --project Tools/NotificationTests -p:NotificationManagerPath='<original NotificationManager.cs>'
dotnet run --project Tools/NotificationTests
```

The test runner uses real .NET cancellation tokens and worker threads. The controlled cancellation cases do not prove all possible thread interleavings or in-flight callback/Dispose races on Mono or IL2CPP.
