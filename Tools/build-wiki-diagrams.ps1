$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sourceDir = Join-Path $root 'Wiki/diagrams'
$config = Join-Path $sourceDir 'mermaid-config.json'
$outputDir = Join-Path $root 'Wiki/images'
# check-docs는 위키 이전이 끝날 때까지 Docs/wiki-drafts를 검사하므로 같은 이미지를 그쪽에도 둔다.
$draftsDir = Join-Path $root 'Docs/wiki-drafts/images'

New-Item -ItemType Directory -Force $outputDir, $draftsDir | Out-Null

foreach ($source in Get-ChildItem $sourceDir -Filter *.mmd)
{
    $output = Join-Path $outputDir "$($source.BaseName).svg"
    npx -y @mermaid-js/mermaid-cli@12.0.0 -i $source.FullName -o $output -b white -c $config | Out-Null
    if ($LASTEXITCODE -ne 0)
    {
        throw "mermaid 변환 실패: $($source.Name)"
    }
    Copy-Item $output $draftsDir -Force
    Write-Output "$($source.Name) -> Wiki/images/$($source.BaseName).svg"
}
