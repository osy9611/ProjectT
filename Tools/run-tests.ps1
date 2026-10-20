$ErrorActionPreference = 'Stop'
$projects = @(
    'LifecycleTests/LifecycleTests.csproj',
    'NotificationTests/NotificationTests.csproj',
    'SkillTests/SkillTests.csproj',
    'SoundTests/SoundTests.csproj',
    'UITests/UITests.csproj',
    'RefactoringTests/Downloader/Downloader.csproj',
    'RefactoringTests/FirebaseCache/FirebaseCache.csproj',
    'RefactoringTests/UISoundTool/UISoundTool.csproj',
    'RefactoringTests/UploadFlow/UploadFlow.csproj'
)
$failed = @()
foreach ($project in $projects) {
    & dotnet run --project (Join-Path $PSScriptRoot $project)
    if ($LASTEXITCODE -ne 0) { $failed += $project }
}
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'RefactoringTests/run-build.ps1')
if ($LASTEXITCODE -ne 0) { $failed += 'RefactoringTests/run-build.ps1' }
if ($failed) {
    Write-Output "FAILED: $($failed -join ', ')"
    exit 1
}
Write-Output "ALL PASSED: $($projects.Count + 1) test runs"
