param(
    [Parameter(Mandatory = $true)][string]$GeneratorPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$PeerBytesPath,
    [string]$InputPath = (Join-Path $PSScriptRoot '../../DesignTable/Data')
)

$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
$compiler = Join-Path $installation 'MSBuild/Current/Bin/Roslyn/csc.exe'
$generator = [IO.Path]::GetFullPath($GeneratorPath)
$generatorFolder = Split-Path $generator
$protobuf = Join-Path $generatorFolder 'protobuf-net.dll'
$executable = Join-Path $generatorFolder 'TableGeneratorTests.exe'
$output = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path $output | Out-Null
& $compiler /nologo /target:exe "/out:$executable" "/reference:$generator" "/reference:$protobuf" (Join-Path $PSScriptRoot 'TableGeneratorTests.cs')
if ($LASTEXITCODE -ne 0) {
    throw 'TableGenerator test compilation failed.'
}
Copy-Item -LiteralPath ($generator + '.config') -Destination ($executable + '.config') -Force
$testArguments = @([IO.Path]::GetFullPath($InputPath), $output)
if ($PeerBytesPath) {
    $testArguments += [IO.Path]::GetFullPath($PeerBytesPath)
}
& $executable @testArguments | Tee-Object -FilePath (Join-Path $output 'results.txt')
if ($LASTEXITCODE -ne 0) {
    throw 'TableGenerator characterization failed.'
}

if ($PeerBytesPath) {
    $peer = Split-Path ([IO.Path]::GetFullPath($PeerBytesPath))
    $currentFiles = @(Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object { $_.Extension -in @('.cs', '.bytes') -or $_.Name -eq 'diagnostic.txt' } | ForEach-Object { $_.FullName.Substring($output.Length).TrimStart('\', '/') } | Sort-Object)
    $peerFiles = @(Get-ChildItem -LiteralPath $peer -Recurse -File | Where-Object { $_.Extension -in @('.cs', '.bytes') -or $_.Name -eq 'diagnostic.txt' } | ForEach-Object { $_.FullName.Substring($peer.Length).TrimStart('\', '/') } | Sort-Object)
    if (Compare-Object $currentFiles $peerFiles) {
        throw 'Generated artifact file sets differ.'
    }
    foreach ($relative in $currentFiles) {
        $currentBytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $output $relative)))
        $peerBytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $peer $relative)))
        if ($currentBytes -cne $peerBytes) {
            throw "Generated artifact differs: $relative"
        }
    }
    "PASS: $($currentFiles.Count) artifacts byte-identical to peer (C#, protobuf data, known-defect diagnostics)." | Tee-Object -FilePath (Join-Path $output 'comparison.txt')
}
