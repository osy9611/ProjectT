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
$assembly = Join-Path $output 'UploadFlowTests.dll'
$response = Join-Path $output 'upload.rsp'
$arguments = @('-nologo', '-target:exe', '-langversion:9.0', '-codepage:949', ('-out:"' + $assembly + '"'))
$arguments += Get-ChildItem (Join-Path $pack.FullName 'ref/net9.0/*.dll') | ForEach-Object { '-r:"' + $_.FullName + '"' }
$arguments += '"' + [IO.Path]::GetFullPath((Join-Path $ScriptRoot 'Server/Firebase/Editor/FirebaseUploader.cs')) + '"'
$arguments += '"' + (Join-Path $PSScriptRoot 'UploadFlowTests.cs') + '"'
$arguments | Set-Content -LiteralPath $response -Encoding UTF8
$dotnet = Join-Path $DotNetRoot 'dotnet.exe'
& $dotnet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') "@$response"
if ($LASTEXITCODE -ne 0) { throw 'Upload flow test compilation failed.' }
'{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"9.0.0"}}}' | Set-Content -LiteralPath (Join-Path $output 'UploadFlowTests.runtimeconfig.json') -Encoding UTF8
& $dotnet $assembly
if ($LASTEXITCODE -ne 0) { throw 'Upload flow test failed.' }
