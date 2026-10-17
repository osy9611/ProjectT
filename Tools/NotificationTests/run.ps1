param(
    [string]$DotNetRoot = 'C:\Program Files\dotnet',
    [string]$NotificationManagerPath = (Join-Path $PSScriptRoot '../../Client/Assets/Script/Managers/NotificationManager.cs'),
    [string]$OutputPath = (Join-Path $PSScriptRoot 'bin')
)

$ErrorActionPreference = 'Stop'
$sdk = Get-ChildItem (Join-Path $DotNetRoot 'sdk') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$pack = Get-ChildItem (Join-Path $DotNetRoot 'packs/Microsoft.NETCore.App.Ref') -Directory | Where-Object Name -Like '9.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1

if (!$sdk -or !$pack) {
    throw '.NET SDK and .NET 9 reference pack are required.'
}

$testOutput = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force $testOutput | Out-Null
$testDll = Join-Path $testOutput 'NotificationTests.dll'
$testRsp = Join-Path $testOutput 'compile.rsp'
$arguments = @('-nologo', '-target:exe', '-langversion:9.0', ('-out:"' + $testDll + '"'))
$arguments += Get-ChildItem (Join-Path $pack.FullName 'ref/net9.0/*.dll') | ForEach-Object { '-r:"' + $_.FullName + '"' }

$uniTaskPackage = Get-ChildItem (Join-Path $PSScriptRoot '../../Client/Library/PackageCache') -Directory -Filter 'com.cysharp.unitask@*' | Select-Object -First 1
if (!$uniTaskPackage) {
    throw 'Open the Unity project once to restore the UniTask package.'
}

$uniTaskRuntime = Join-Path $uniTaskPackage.FullName 'Runtime'
$excluded = @('CancellationTokenSourceExtensions.cs', 'EnumeratorAsyncExtensions.cs', 'PlayerLoopHelper.cs', 'PlayerLoopTimer.cs', 'TimeoutController.cs', 'UniTask.Delay.cs', 'UniTask.Bridge.cs', 'UniTask.Run.cs', 'UniTask.WaitUntil.cs', 'UniTaskSynchronizationContext.cs')
$sources = Get-ChildItem $uniTaskRuntime -Filter '*.cs' | Where-Object { $_.Name -notlike 'Unity*' -and $_.Name -notin $excluded }
$sources += Get-ChildItem (Join-Path $uniTaskRuntime 'CompilerServices') -Filter '*.cs'
$sources += Get-ChildItem (Join-Path $uniTaskRuntime 'Internal') -Filter '*.cs' | Where-Object { $_.Name -notin @('ContinuationQueue.cs', 'PlayerLoopRunner.cs', 'UnityEqualityComparer.cs', 'UnityWebRequestExtensions.cs', 'DiagnosticsExtensions.cs') }
$arguments += $sources | ForEach-Object { '"' + $_.FullName + '"' }

$files = @(
    'Stubs.cs',
    'Program.cs',
    'BehaviourTests.cs',
    '../../Client/Assets/Script/Managers/Base/ManagerBase.cs',
    '../../Client/Assets/Script/Event/NotificationId.cs',
    '../../Client/Assets/Script/Event/NotifyHandlerBehaviour.cs'
)

foreach ($file in $files) {
    $arguments += '"' + (Join-Path $PSScriptRoot $file) + '"'
}

$arguments += '"' + [IO.Path]::GetFullPath($NotificationManagerPath) + '"'

$arguments | Set-Content -LiteralPath $testRsp
& dotnet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') "@$testRsp"
if ($LASTEXITCODE -ne 0) {
    throw 'Notification test compilation failed.'
}

'{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"9.0.0"}}}' | Set-Content (Join-Path $testOutput 'NotificationTests.runtimeconfig.json')
& dotnet $testDll
if ($LASTEXITCODE -ne 0) {
    throw 'Notification tests failed.'
}
