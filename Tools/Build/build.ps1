param(
    [Parameter(Mandatory)][ValidateSet('Android', 'iOS', 'StandaloneWindows')][string]$Target,
    [Parameter(Mandatory)][string]$Version,
    # Jenkins BUILD_NUMBER. Android versionCode must increase and stay at or below 2100000000.
    [Parameter(Mandatory)][ValidateRange(1, 2100000000)][int]$BuildNumber,
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe',
    [string]$LogPath
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Client'))
# PowerShell 5.1 leaves $PSScriptRoot empty in parameter defaults of advanced scripts.
if (!$LogPath) { $LogPath = Join-Path $PSScriptRoot "bin/build-$Target.log" }
$log = [IO.Path]::GetFullPath($LogPath)
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
# Addressables are built for the active target, so the editor must start on the requested target.
$editorTarget = @{ Android = 'Android'; iOS = 'iOS'; StandaloneWindows = 'Win' }[$Target]
# No -quit: PreBuild is async and calls EditorApplication.Exit with the result.
$arguments = '-batchmode -projectPath "' + $project + '" -buildTarget ' + $editorTarget +
    ' -executeMethod ProjectT.Build.BuildMgr.PreBuild -BUILD_TARGET ' + $Target + ' -BUILD_VERSION "' + $Version + '" -BUILD_NUMBER ' + $BuildNumber + ' -logFile "' + $log + '"'
Write-Output "Unity build: $Target $Version ($BuildNumber) (log: $log)"
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -NoNewWindow -PassThru
# ExitCode stays empty unless the handle is opened before the process exits.
$null = $process.Handle
# Start-Process -Wait would also wait for helper processes Unity leaves running.
$process.WaitForExit()
Write-Output "Unity exit code: $($process.ExitCode)"
exit $process.ExitCode
