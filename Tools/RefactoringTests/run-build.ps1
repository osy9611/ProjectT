param([string]$ScriptRoot)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'BuildFlow/BuildFlow.csproj'
$buildArguments = @('build', $project, '-nologo', '-v:q')
if ($ScriptRoot) { $buildArguments += '-p:ScriptRoot=' + [IO.Path]::GetFullPath($ScriptRoot) }
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) { throw 'Build flow test compilation failed.' }
$assembly = Join-Path $PSScriptRoot 'BuildFlow/bin/Debug/net9.0/BuildFlow.dll'
& dotnet $assembly -BUILD_TARGET StandaloneWindows -BUILD_VERSION 3.2.1 -BUILD_NUMBER 7
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
    & dotnet $assembly --argument-case @case
    if ($LASTEXITCODE -ne 0) { throw "Build argument case failed: $($case[0])" }
}
Write-Output 'ALL PASSED: 12 build argument scenarios'
