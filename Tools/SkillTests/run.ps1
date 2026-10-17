param([string]$DotNetRoot = 'C:\Program Files\dotnet')
$ErrorActionPreference = 'Stop'
$sdk = Get-ChildItem (Join-Path $DotNetRoot 'sdk') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$pack = Get-ChildItem (Join-Path $DotNetRoot 'packs/Microsoft.NETCore.App.Ref') -Directory | Where-Object Name -Like '9.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$sdk -or !$pack) { throw '.NET SDK and .NET 9 reference pack are required.' }
$testOutput = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $testOutput | Out-Null
$testDll = Join-Path $testOutput 'SkillTests.dll'
$testRsp = Join-Path $testOutput 'compile.rsp'
$arguments = @('-nologo', '-target:exe', '-langversion:latest', '-nowarn:CS0169,CS0414,CS0649', ('-out:"' + $testDll + '"'))
$arguments += Get-ChildItem (Join-Path $pack.FullName 'ref/net9.0/*.dll') | ForEach-Object { '-r:"' + $_.FullName + '"' }
$files = @(
    'Stubs.cs',
    'Program.cs',
    '../../Client/Assets/Script/SkillAdapter/SkillDefinitions.cs',
    '../../Client/Assets/Script/SkillAdapter/Action_Melee.cs',
    '../../Client/Assets/Script/SkillAdapter/Action_Normal.cs',
    '../../Client/Assets/Script/SkillAdapter/Action_Range.cs'
)
foreach ($file in $files) {
    $arguments += '"' + (Join-Path $PSScriptRoot $file) + '"'
}
# The core assembly is compiled into the test assembly, so its internals need no InternalsVisibleTo here.
$arguments += Get-ChildItem (Join-Path $PSScriptRoot '../../Client/Assets/Script/AbilitySystem') -Filter '*.cs' -Recurse | ForEach-Object { '"' + $_.FullName + '"' }
$arguments | Set-Content -LiteralPath $testRsp
& dotnet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') "@$testRsp"
if ($LASTEXITCODE -ne 0) { throw 'Skill test compilation failed.' }
'{"runtimeOptions":{"tfm":"net9.0","framework":{"name":"Microsoft.NETCore.App","version":"9.0.0"}}}' | Set-Content (Join-Path $testOutput 'SkillTests.runtimeconfig.json')
& dotnet $testDll
if ($LASTEXITCODE -ne 0) { throw 'Skill tests failed.' }
