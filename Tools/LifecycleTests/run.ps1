param([string]$DotNetRoot = 'C:\Program Files\dotnet')
$ErrorActionPreference = 'Stop'
$sdk = Get-ChildItem (Join-Path $DotNetRoot 'sdk') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$pack = Get-ChildItem (Join-Path $DotNetRoot 'packs/Microsoft.NETCore.App.Ref') -Directory | Where-Object Name -Like '9.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$sdk -or !$pack) { throw '.NET SDK and .NET 9 reference pack are required.' }
$testOutput = Join-Path $PSScriptRoot 'bin/direct'
New-Item -ItemType Directory -Force $testOutput | Out-Null
$testDll = Join-Path $testOutput 'LifecycleTests.dll'
$testRsp = Join-Path $testOutput 'compile.rsp'
$arguments = @('-nologo', '-target:exe', '-langversion:latest', ('-out:"' + $testDll + '"'))
$arguments += Get-ChildItem (Join-Path $pack.FullName 'ref/net9.0/*.dll') | ForEach-Object { '-r:"' + $_.FullName + '"' }
$uniTaskPackage = Get-ChildItem (Join-Path $PSScriptRoot '../../Client/Library/PackageCache') -Directory -Filter 'com.cysharp.unitask@*' | Select-Object -First 1
if (!$uniTaskPackage) { throw 'Open the Unity project once to restore the UniTask package.' }
$uniTaskRuntime = Join-Path $uniTaskPackage.FullName 'Runtime'
$excluded = @('CancellationTokenSourceExtensions.cs', 'EnumeratorAsyncExtensions.cs', 'PlayerLoopHelper.cs', 'PlayerLoopTimer.cs', 'TimeoutController.cs', 'UniTask.Delay.cs', 'UniTask.Bridge.cs', 'UniTask.Run.cs', 'UniTask.WaitUntil.cs', 'UniTaskSynchronizationContext.cs')
$sources = Get-ChildItem $uniTaskRuntime -Filter '*.cs' | Where-Object { $_.Name -notlike 'Unity*' -and $_.Name -notin $excluded }
$sources += Get-ChildItem (Join-Path $uniTaskRuntime 'CompilerServices') -Filter '*.cs'
$sources += Get-ChildItem (Join-Path $uniTaskRuntime 'Internal') -Filter '*.cs' | Where-Object { $_.Name -notin @('ContinuationQueue.cs', 'PlayerLoopRunner.cs', 'UnityEqualityComparer.cs', 'UnityWebRequestExtensions.cs', 'DiagnosticsExtensions.cs') }
$arguments += $sources | ForEach-Object { '"' + $_.FullName + '"' }
$files = @('PoolConsumerStubs.cs', '../../Client/Assets/Script/Server/Socket/Default/BytesPool.cs', '../../Client/Assets/Script/Server/Socket/Default/PooledBytes.cs', 'PoolTests.cs', '../../Client/Assets/Script/Managers/PoolManager.cs', '../../Client/Assets/Script/Pool/GameObjectPool.cs', '../../Client/Assets/Script/Pool/PooledObject.cs', '../../Client/Assets/Script/Server/Socket/Concurrent/ConcurrentPool.cs', 'Program.cs', 'ControllerTests.cs', 'SceneContractTests.cs', '../../Client/Assets/Script/Managers/NotificationManager.cs', '../../Client/Assets/Script/Event/NotificationId.cs', '../../Client/Assets/Script/Input/Controller.cs', '../../Client/Assets/Script/Managers/InputManager.cs', '../../Client/Assets/Script/Addressable/Resource.cs', '../../Client/Assets/Script/Addressable/SpriteAtlasResource.cs', '../../Client/Assets/Script/Common/ErrorCollector.cs', '../../Client/Assets/Script/Common/Global.cs', '../../Client/Assets/Script/Common/ErrorReport/ErrorReporter.cs', '../../Client/Assets/Script/Common/ErrorReport/ErrorEvent.cs', '../../Client/Assets/Script/Common/ErrorReport/IErrorSink.cs', '../../Client/Assets/Script/Common/ErrorReport/FileErrorSink.cs', 'ErrorReporterTests.cs', 'UnityStubs.cs', 'ResourceStubs.cs', 'SceneStubs.cs', 'SceneTests.cs', '../../Client/Assets/Script/Scene/SceneBase.cs', '../../Client/Assets/Script/Managers/SceneManager.cs', 'ResourceTests.cs', '../../Client/Assets/Script/Managers/ResourceManager.cs', '../../Client/Assets/Script/Addressable/ResourceBase.cs', '../../Client/Assets/Script/Addressable/ResourceScope.cs', '../../Client/Assets/Script/Managers/Base/ManagerBase.cs', '../../Client/Assets/Script/Managers/Base/ManagerHost.cs', '../../Client/Assets/Script/Pool/Pool.cs', '../../Client/Assets/Script/Pool/IPoolable.cs')
$files += @('ActorTests.cs', '../../Client/Assets/Script/Actor/ComBaseActor.cs', '../../Client/Assets/Script/Input/InputContext.cs')
$files += @('../../Client/Assets/Script/SkillAdapter/SkillDefinitions.cs', '../../Client/Assets/Script/SkillAdapter/Action_Melee.cs', '../../Client/Assets/Script/SkillAdapter/Action_Normal.cs', '../../Client/Assets/Script/SkillAdapter/Action_Range.cs')
foreach ($file in $files) {
    $arguments += '"' + (Join-Path $PSScriptRoot $file) + '"'
}
$arguments += Get-ChildItem (Join-Path $PSScriptRoot '../../Client/Assets/Script/AbilitySystem') -Filter '*.cs' -Recurse | ForEach-Object { '"' + $_.FullName + '"' }
$arguments | Set-Content -LiteralPath $testRsp
& dotnet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') "@$testRsp"
if ($LASTEXITCODE -ne 0) { throw 'Lifecycle test compilation failed.' }
'{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"9.0.0"}}}' | Set-Content (Join-Path $testOutput 'LifecycleTests.runtimeconfig.json')
& dotnet $testDll
if ($LASTEXITCODE -ne 0) { throw 'Lifecycle tests failed.' }

