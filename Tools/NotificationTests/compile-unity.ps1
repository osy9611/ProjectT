param(
    [string]$UnityData = 'C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Data'
)

$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Client'))
$output = Join-Path $PSScriptRoot 'bin/unity-compile'
New-Item -ItemType Directory -Force $output | Out-Null
$runtimeResponse = Get-ChildItem (Join-Path $project 'Library/Bee/artifacts') -Filter 'Assembly-CSharp.rsp' -Recurse |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1

if (!$runtimeResponse) {
    throw 'Open the Unity project once to generate the Bee compiler response files.'
}

$dotnet = Join-Path $UnityData 'NetCoreRuntime/dotnet.exe'
$compiler = Join-Path $UnityData 'DotNetSdkRoslyn/csc.dll'
$removedSources = @(
    'Assets/Script/Event/INotify.cs',
    'Assets/Script/Event/NotifyHandler.cs',
    'Assets/Script/Event/NotifyHandlerBehaviour.cs'
)

Push-Location $project
try
{
    foreach ($assembly in @('Assembly-CSharp', 'Assembly-CSharp-Editor'))
    {
        $response = Join-Path $runtimeResponse.DirectoryName ($assembly + '.rsp')
        if (!(Test-Path -LiteralPath $response)) {
            throw "Missing compiler response: $response"
        }

        $lines = @(foreach ($line in Get-Content -LiteralPath $response)
        {
            $source = $line.Trim('"').Replace('\', '/')
            if ($source -in $removedSources) {
                continue
            }

            if ($line -match '^[-/]out:') {
                '-out:"' + (Join-Path $output ($assembly + '.dll')) + '"'
            }
            elseif ($line -match '^[-/]refout:') {
                '-refout:"' + (Join-Path $output ($assembly + '.ref.dll')) + '"'
            }
            elseif ($line -match '^[-/]r:.*[/\\]Assembly-CSharp\.ref\.dll"?$') {
                '-r:"' + (Join-Path $output 'Assembly-CSharp.ref.dll') + '"'
            }
            else {
                $line
            }
        })

        if ($assembly -eq 'Assembly-CSharp') {
            foreach ($notificationSource in @('"Assets/Script/Event/NotificationId.cs"')) {
                if (!($lines | Where-Object { $_.Replace('\', '/') -eq $notificationSource })) {
                    $lines += $notificationSource
                }
            }
        }

        $isolatedResponse = Join-Path $output ($assembly + '.rsp')
        $lines | Set-Content -LiteralPath $isolatedResponse -Encoding UTF8
        & $dotnet $compiler "@$isolatedResponse"
        if ($LASTEXITCODE -ne 0) {
            throw "$assembly Unity-reference compilation failed."
        }

        Write-Output "$assembly Unity-reference compilation passed."
    }
}
finally
{
    Pop-Location
}
