param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Client'))
$target = Join-Path $project 'Assets/ProjectTUITestHarness'
$expected = [IO.Path]::GetFullPath((Join-Path $project 'Assets/ProjectTUITestHarness'))
if ([IO.Path]::GetFullPath($target) -ne $expected) { throw 'Invalid temporary test directory.' }
if (Test-Path -LiteralPath $target) { throw "Temporary directory already exists: $target" }
if (Test-Path -LiteralPath ($target + '.meta')) { throw 'Temporary folder metadata already exists.' }
if (Get-Process Unity -ErrorAction SilentlyContinue) { throw 'Close Unity before running the batch Play Mode smoke test.' }
$output = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $output | Out-Null
$log = Join-Path $output 'unity-playmode.log'
$process = $null
try
{
    New-Item -ItemType Directory -Force (Join-Path $target 'Editor') | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Unity/ProjectTUIPlayProbe.cs') -Destination (Join-Path $target 'ProjectTUIPlayProbe.cs')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Unity/Editor/ProjectTUIPlayMode.cs') -Destination (Join-Path $target 'Editor/ProjectTUIPlayMode.cs')
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
    if ([IO.Path]::GetFullPath($target) -ne $expected) { throw 'Refusing to clean unexpected path.' }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    if (Test-Path -LiteralPath ($target + '.meta')) { Remove-Item -LiteralPath ($target + '.meta') -Force }
}