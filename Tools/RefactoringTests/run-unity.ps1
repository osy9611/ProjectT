param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe',
    [string]$OutputPath = (Join-Path $PSScriptRoot 'bin')
)

$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Client'))
$target = [IO.Path]::GetFullPath((Join-Path $project 'Assets/ProjectTRefactoringHarness'))
$expected = Join-Path $project 'Assets/ProjectTRefactoringHarness'
if ($target -ne $expected -or (Test-Path -LiteralPath $target) -or (Test-Path -LiteralPath ($target + '.meta'))) {
    throw 'Temporary harness directory must be unused and inside this project.'
}
if (Get-Process Unity -ErrorAction SilentlyContinue) {
    throw 'Unity is already running; batch validation cannot open the same project.'
}
$output = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$log = Join-Path $output 'unity-editor.log'
$process = $null
try {
    New-Item -ItemType Directory -Path (Join-Path $target 'Editor') | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Unity/RefactoringProbe.cs') -Destination (Join-Path $target 'Editor/RefactoringProbe.cs')
    $arguments = '-batchmode -nographics -projectPath "' + $project + '" -executeMethod RefactoringProbe.Run -logFile "' + $log + '"'
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddMinutes(4)
    while (!$process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $deadline) {
            throw "Unity validation timed out. See $log"
        }
    }
    if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -SimpleMatch 'PROJECTT_REFACTORING_PASS' -Quiet)) {
        throw "Unity validation failed (exit $($process.ExitCode)). See $log"
    }
    Select-String -LiteralPath $log -Pattern 'REFACTOR_CASE|PROJECTT_REFACTORING_PASS' | ForEach-Object Line
}
finally {
    if ($process -and !$process.HasExited) {
        Stop-Process -Id $process.Id
        $process.WaitForExit()
    }
    if ([IO.Path]::GetFullPath($target) -ne $expected) {
        throw 'Refusing unexpected cleanup path.'
    }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    if (Test-Path -LiteralPath ($target + '.meta')) { Remove-Item -LiteralPath ($target + '.meta') -Force }
}
