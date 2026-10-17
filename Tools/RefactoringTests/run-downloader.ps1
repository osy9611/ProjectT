param(
    [string]$DownloaderPath = (Join-Path $PSScriptRoot '../../Client/Assets/Script/Addressable/Downloader.cs'),
    [string]$DotNetRoot = 'C:\Program Files\dotnet',
    [string]$OutputPath = (Join-Path $PSScriptRoot 'bin/downloader')
)

$ErrorActionPreference = 'Stop'
$sdk = Get-ChildItem (Join-Path $DotNetRoot 'sdk') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$pack = Get-ChildItem (Join-Path $DotNetRoot 'packs/Microsoft.NETCore.App.Ref') -Directory | Where-Object Name -Like '9.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$sdk -or !$pack) {
    throw '.NET SDK and .NET 9 reference pack are required.'
}

$uniTaskPackage = Get-ChildItem (Join-Path $PSScriptRoot '../../Client/Library/PackageCache') -Directory -Filter 'com.cysharp.unitask@*' | Select-Object -First 1
if (!$uniTaskPackage) {
    throw 'Open the Unity project once to restore the UniTask package.'
}

$output = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$assembly = Join-Path $output 'DownloaderTests.dll'
$response = Join-Path $output 'compile.rsp'
$arguments = @('-nologo', '-target:exe', '-langversion:9.0', ('-out:"' + $assembly + '"'))
$arguments += Get-ChildItem (Join-Path $pack.FullName 'ref/net9.0/*.dll') | ForEach-Object { '-r:"' + $_.FullName + '"' }
$uniTaskRuntime = Join-Path $uniTaskPackage.FullName 'Runtime'
$excluded = @('CancellationTokenSourceExtensions.cs', 'EnumeratorAsyncExtensions.cs', 'PlayerLoopHelper.cs', 'PlayerLoopTimer.cs', 'TimeoutController.cs', 'UniTask.Delay.cs', 'UniTask.Bridge.cs', 'UniTask.Run.cs', 'UniTask.WaitUntil.cs', 'UniTaskSynchronizationContext.cs')
$sources = Get-ChildItem $uniTaskRuntime -Filter '*.cs' | Where-Object { $_.Name -notlike 'Unity*' -and $_.Name -notin $excluded }
$sources += Get-ChildItem (Join-Path $uniTaskRuntime 'CompilerServices') -Filter '*.cs'
$sources += Get-ChildItem (Join-Path $uniTaskRuntime 'Internal') -Filter '*.cs' | Where-Object { $_.Name -notin @('ContinuationQueue.cs', 'PlayerLoopRunner.cs', 'UnityEqualityComparer.cs', 'UnityWebRequestExtensions.cs', 'DiagnosticsExtensions.cs') }
$arguments += $sources | ForEach-Object { '"' + $_.FullName + '"' }
$arguments += '"' + [IO.Path]::GetFullPath($DownloaderPath) + '"'
$arguments += '"' + (Join-Path $PSScriptRoot 'DownloaderStubs.cs') + '"'
$arguments += '"' + (Join-Path $PSScriptRoot 'DownloaderTests.cs') + '"'
$arguments | Set-Content -LiteralPath $response -Encoding UTF8
$dotnet = Join-Path $DotNetRoot 'dotnet.exe'
& $dotnet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') "@$response"
if ($LASTEXITCODE -ne 0) {
    throw 'Downloader test compilation failed.'
}

'{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"9.0.0"}}}' | Set-Content -LiteralPath (Join-Path $output 'DownloaderTests.runtimeconfig.json') -Encoding UTF8
& $dotnet $assembly | Tee-Object -FilePath (Join-Path $output 'results.txt')
if ($LASTEXITCODE -ne 0) {
    throw 'Downloader characterization failed.'
}
