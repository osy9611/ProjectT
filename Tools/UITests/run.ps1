param([string]$DotNetRoot = 'C:\Program Files\dotnet')
$ErrorActionPreference = 'Stop'
$sdk = Get-ChildItem "$DotNetRoot/sdk" -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$pack = Get-ChildItem "$DotNetRoot/packs/Microsoft.NETCore.App.Ref" -Directory | Where-Object Name -Like '9.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$testOutput = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $testOutput | Out-Null
$arguments = @('-nologo','-target:exe','-langversion:latest',('-out:"'+$testOutput+'\UITests.dll"'))
$arguments += Get-ChildItem "$($pack.FullName)/ref/net9.0/*.dll" | ForEach-Object { '-r:"'+$_.FullName+'"' }
$package = Get-ChildItem "$PSScriptRoot/../../Client/Library/PackageCache" -Directory -Filter 'com.cysharp.unitask@*' | Select-Object -First 1
$runtime = Join-Path $package.FullName 'Runtime'
$excluded = @('CancellationTokenSourceExtensions.cs','EnumeratorAsyncExtensions.cs','PlayerLoopHelper.cs','PlayerLoopTimer.cs','TimeoutController.cs','UniTask.Delay.cs','UniTask.Bridge.cs','UniTask.Run.cs','UniTask.WaitUntil.cs','UniTaskSynchronizationContext.cs')
$sources = Get-ChildItem $runtime -Filter '*.cs' | Where-Object { $_.Name -notlike 'Unity*' -and $_.Name -notin $excluded }
$sources += Get-ChildItem "$runtime/CompilerServices" -Filter '*.cs'
$sources += Get-ChildItem "$runtime/Internal" -Filter '*.cs' | Where-Object { $_.Name -notin @('ContinuationQueue.cs','PlayerLoopRunner.cs','UnityEqualityComparer.cs','UnityWebRequestExtensions.cs','DiagnosticsExtensions.cs') }
$arguments += $sources | ForEach-Object { '"'+$_.FullName+'"' }
foreach($file in @('Stubs.cs','Program.cs','InputTests.cs','BindingTests.cs','../../Client/Assets/Script/UGUI/UI/UIEventHandler.cs','../../Client/Assets/Script/UGUI/UI/UIContainer.cs','../../Client/Assets/Script/UGUI/UI/UIBase.cs','../../Client/Assets/Script/UGUI/UI/UIDefine.cs','../../Client/Assets/Script/Managers/UIManager.cs','../../Client/Assets/Script/Common/ErrorCollector.cs')) { $arguments += '"'+(Join-Path $PSScriptRoot $file)+'"' }
$arguments | Set-Content "$testOutput/compile.rsp"
& dotnet "$($sdk.FullName)/Roslyn/bincore/csc.dll" "@$testOutput/compile.rsp"
if($LASTEXITCODE -ne 0) { throw 'UI test compilation failed.' }
'{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"9.0.0"}}}' | Set-Content "$testOutput/UITests.runtimeconfig.json"
& dotnet "$testOutput/UITests.dll"
if($LASTEXITCODE -ne 0) { throw 'UI tests failed.' }
