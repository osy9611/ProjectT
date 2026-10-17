param(
    [string]$UISoundToolPath = (Join-Path $PSScriptRoot '../../Client/Assets/Script/Sound/Editor/UISoundTool.cs'),
    [string]$OutputPath = (Join-Path $PSScriptRoot 'bin/ui-sound'),
    [string]$DotNetRoot = 'C:\Program Files\dotnet'
)

$ErrorActionPreference = 'Stop'
$sdk = Get-ChildItem (Join-Path $DotNetRoot 'sdk') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$pack = Get-ChildItem (Join-Path $DotNetRoot 'packs/Microsoft.NETCore.App.Ref') -Directory -Filter '9.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$output = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$assembly = Join-Path $output 'UISoundToolTests.dll'
$arguments = @('-nologo', '-target:exe', '-langversion:9.0', '-codepage:949', ('-out:"' + $assembly + '"'))
$arguments += Get-ChildItem (Join-Path $pack.FullName 'ref/net9.0/*.dll') | ForEach-Object { '-r:"' + $_.FullName + '"' }
$arguments += '"' + [IO.Path]::GetFullPath($UISoundToolPath) + '"'
$arguments += '"' + (Join-Path $PSScriptRoot 'UISoundToolStubs.cs') + '"'
$arguments += '"' + (Join-Path $PSScriptRoot 'UISoundToolTests.cs') + '"'
$response = Join-Path $output 'compile.rsp'
$arguments | Set-Content -LiteralPath $response -Encoding UTF8
$dotnet = Join-Path $DotNetRoot 'dotnet.exe'
& $dotnet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') "@$response"
if ($LASTEXITCODE -ne 0) {
    throw 'UISoundTool test compilation failed.'
}
'{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"9.0.0"}}}' | Set-Content -LiteralPath (Join-Path $output 'UISoundToolTests.runtimeconfig.json') -Encoding UTF8
& $dotnet $assembly | Tee-Object -FilePath (Join-Path $output 'results.txt')
if ($LASTEXITCODE -ne 0) {
    throw 'UISoundTool tests failed.'
}
