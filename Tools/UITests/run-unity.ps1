param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Client'))
if (Get-Process Unity -ErrorAction SilentlyContinue) { throw 'Close Unity before running the batch Play Mode smoke test.' }
$output = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $output | Out-Null
$log = Join-Path $output 'unity-playmode.log'
$process = $null
try
{
    $arguments = '-batchmode -nographics -projectPath "' + $project + '" -executeMethod ProjectTUIPlayMode.Run -logFile "' + $log + '"'
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(180000))
    {
        Stop-Process -Id $process.Id
        $process.WaitForExit()
        throw "Unity test timed out. See $log"
    }
    if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -SimpleMatch 'PROJECTT_UI_PLAYMODE_PASS' -Quiet))
    {
        throw "Unity test did not complete successfully. See $log (Editor exit code $($process.ExitCode))."
    }
    Write-Output 'Unity Play Mode smoke test passed.'
}
finally
{
    if ($process -and !$process.HasExited)
    {
        Stop-Process -Id $process.Id
        $process.WaitForExit()
    }
}
