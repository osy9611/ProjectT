param(
    [string]$UnityData = 'C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Data'
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Client'))
$output = Join-Path $PSScriptRoot 'bin/unity-compile'
New-Item -ItemType Directory -Force $output | Out-Null
$runtimeResponse = Get-ChildItem (Join-Path $project 'Library/Bee/artifacts') -Filter 'Assembly-CSharp.rsp' -Recurse |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (!$runtimeResponse) { throw 'Open the Unity project once to generate the Bee compiler response files.' }
$dotnet = Join-Path $UnityData 'NetCoreRuntime/dotnet.exe'
$compiler = Join-Path $UnityData 'DotNetSdkRoslyn/csc.dll'
# AbilitySystem has no asmdef and compiles into Assembly-CSharp. A response file from the asmdef era may still reference its dll.
$coreReferencePattern = '^[-/]r:.*[/\\]ProjectT\.AbilitySystem(\.ref)?\.dll"?$'
Push-Location $project
try
{
    foreach ($assembly in @('Assembly-CSharp', 'Assembly-CSharp-Editor'))
    {
        $response = Join-Path $runtimeResponse.DirectoryName ($assembly + '.rsp')
        if (!(Test-Path -LiteralPath $response)) { throw "Missing compiler response: $response" }
        $lines = foreach ($line in Get-Content -LiteralPath $response)
        {
            if ($line -match '^[-/]out:') { '-out:"' + (Join-Path $output ($assembly + '.dll')) + '"' }
            elseif ($line -match '^[-/]refout:') { '-refout:"' + (Join-Path $output ($assembly + '.ref.dll')) + '"' }
            elseif ($line -match '^[-/]r:.*[/\\]Assembly-CSharp\.ref\.dll"?$')
            {
                '-r:"' + (Join-Path $output 'Assembly-CSharp.ref.dll') + '"'
            }
            elseif ($line -match $coreReferencePattern) { }
            else { $line }
        }
        # Bee may not have regenerated its source list after a new input, manager, skill adapter or ability system source was added.
        if ($assembly -eq 'Assembly-CSharp')
        {
            foreach ($sourceFolder in @('Input', 'Managers', 'SkillAdapter', 'AbilitySystem'))
            {
                foreach ($inputSource in Get-ChildItem (Join-Path $project ('Assets/Script/' + $sourceFolder)) -Filter '*.cs' -Recurse)
                {
                    $relativeSource = $inputSource.FullName.Substring($project.Length + 1).Replace('\', '/')
                    $alreadyIncluded = $lines | Where-Object { $_.Trim('"').Replace('\', '/') -eq $relativeSource }
                    if (!$alreadyIncluded)
                    {
                        $lines += '"' + $relativeSource + '"'
                    }
                }
            }
        }
        # Bee may still list a source that was deleted after its last run.
        $lines = $lines | Where-Object { $_ -notmatch '^"Assets/.*\.cs"$' -or (Test-Path -LiteralPath $_.Trim('"')) }
        $isolatedResponse = Join-Path $output ($assembly + '.rsp')
        $lines | Set-Content -LiteralPath $isolatedResponse -Encoding UTF8
        & $dotnet $compiler "@$isolatedResponse"
        if ($LASTEXITCODE -ne 0) { throw "$assembly Unity-reference compilation failed." }
        Write-Output "$assembly Unity-reference compilation passed."
    }
}
finally
{
    Pop-Location
}
