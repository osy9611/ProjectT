$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sourceDir = Join-Path $root 'Wiki/diagrams'
$config = Join-Path $sourceDir 'mermaid-config.json'
$outputDir = Join-Path $root 'Wiki/images'

New-Item -ItemType Directory -Force $outputDir | Out-Null

foreach ($source in Get-ChildItem $sourceDir -Filter *.mmd)
{
    $output = Join-Path $outputDir "$($source.BaseName).svg"
    npx -y @mermaid-js/mermaid-cli@12.0.0 -i $source.FullName -o $output -b white -c $config | Out-Null
    if ($LASTEXITCODE -ne 0)
    {
        throw "mermaid 변환 실패: $($source.Name)"
    }
    Write-Output "$($source.Name) -> Wiki/images/$($source.BaseName).svg"
}
