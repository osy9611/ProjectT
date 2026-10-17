param(
    [string]$ScriptRoot = (Join-Path $PSScriptRoot '../../Client/Assets/Script'),
    [string]$DotNetRoot = 'C:\Program Files\dotnet'
)
$ErrorActionPreference = 'Stop'
$sdk = Get-ChildItem (Join-Path $DotNetRoot 'sdk') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$pack = Get-ChildItem (Join-Path $DotNetRoot 'packs/Microsoft.NETCore.App.Ref') -Directory | Where-Object Name -Like '9.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$sdk -or !$pack) { throw '.NET SDK and .NET 9 reference pack are required.' }
$output = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$assembly = Join-Path $output 'BuildFlowTests.dll'
$response = Join-Path $output 'build.rsp'
$arguments = @('-nologo', '-target:exe', '-langversion:9.0', '-codepage:949', ('-out:"' + $assembly + '"'))
$arguments += Get-ChildItem (Join-Path $pack.FullName 'ref/net9.0/*.dll') | ForEach-Object { '-r:"' + $_.FullName + '"' }
$arguments += '"' + [IO.Path]::GetFullPath((Join-Path $ScriptRoot 'Build/Editor/BuildMgr.cs')) + '"'
$arguments += '"' + (Join-Path $PSScriptRoot 'BuildFlowTests.cs') + '"'
$arguments | Set-Content -LiteralPath $response -Encoding UTF8
$dotnet = Join-Path $DotNetRoot 'dotnet.exe'
& $dotnet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') "@$response"
if ($LASTEXITCODE -ne 0) { throw 'Build flow test compilation failed.' }
'{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"9.0.0"}}}' | Set-Content -LiteralPath (Join-Path $output 'BuildFlowTests.runtimeconfig.json') -Encoding UTF8
& $dotnet $assembly -BUILD_TARGET StandaloneWindows -BUILD_VERSION 3.2.1 -BUILD_NUMBER 7
if ($LASTEXITCODE -ne 0) { throw 'Build flow characterization failed.' }
foreach ($case in @(
    @('Defaults'),
    @('UnsupportedTarget', '-BUILD_TARGET', 'WebGL', '-BUILD_VERSION', '1.0', '-BUILD_NUMBER', '7'),
    @('InvalidTarget', '-BUILD_TARGET', 'invalid'),
    @('MissingTarget', '-BUILD_TARGET'),
    @('MissingVersion', '-BUILD_VERSION'),
    @('MissingNumber', '-BUILD_NUMBER'),
    @('NoVersion', '-BUILD_TARGET', 'Android', '-BUILD_NUMBER', '7'),
    @('NoNumber', '-BUILD_TARGET', 'Android', '-BUILD_VERSION', '1.0'),
    @('ZeroNumber', '-BUILD_TARGET', 'Android', '-BUILD_VERSION', '1.0', '-BUILD_NUMBER', '0'),
    @('InvalidNumber', '-BUILD_TARGET', 'Android', '-BUILD_VERSION', '1.0', '-BUILD_NUMBER', 'abc'),
    @('Repeated', '-BUILD_TARGET', 'Android', '-BUILD_VERSION', '1.0', '-BUILD_NUMBER', '1', '-BUILD_TARGET', 'StandaloneWindows', '-BUILD_VERSION', '3.2.1', '-BUILD_NUMBER', '7'),
    @('IOS', '-BUILD_TARGET', 'iOS', '-BUILD_VERSION', '1.0', '-BUILD_NUMBER', '9')
)) {
    & $dotnet $assembly --argument-case @case
    if ($LASTEXITCODE -ne 0) { throw "Build argument case failed: $($case[0])" }
}
Write-Output 'ALL PASSED: 12 build argument scenarios'
