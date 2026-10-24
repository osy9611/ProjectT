$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding($false)
$repoUrl = 'https://github.com/osy9611/ProjectT'
$wikiDir = Join-Path $root 'Wiki'

$files = @('README.md', 'AGENTS.md', 'REFACTORING_CRITERIA.md') | ForEach-Object { Join-Path $root $_ }
$files += Get-ChildItem (Join-Path $root 'Docs') -Filter *.md | ForEach-Object FullName
$files += Get-ChildItem (Join-Path $root 'Tools') -Directory |
    Where-Object { $_.Name -ne 'CodexMigration' } |
    ForEach-Object { Join-Path $_.FullName 'README.md' } |
    Where-Object { Test-Path $_ }
if (Test-Path $wikiDir)
{
    $files += Get-ChildItem $wikiDir -Filter *.md | ForEach-Object FullName
}

$glossary = [IO.File]::ReadAllText((Join-Path $root 'Docs/glossary.md'), $utf8)
$rulesBlock = [regex]::Match($glossary, '(?s)## 검사 규칙.*?```text\r?\n(.*?)```').Groups[1].Value
$rules = $rulesBlock -split '\r?\n' | Where-Object { $_ } | ForEach-Object {
    $cols = $_ -split "`t", 3
    [pscustomobject]@{ Name = $cols[0]; Scope = $cols[1]; Regex = [regex]$cols[2] }
}

$sourceLines = New-Object System.Collections.Generic.HashSet[string]
foreach ($dir in @('Client/Assets', 'Tools', 'DesignTable'))
{
    Get-ChildItem (Join-Path $root $dir) -Filter *.cs -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object {
            foreach ($sourceLine in [IO.File]::ReadAllLines($_.FullName, $utf8))
            {
                [void]$sourceLines.Add($sourceLine.Trim())
                [void]$sourceLines.Add(($sourceLine -replace '//.*$', '').Trim())
            }
        }
}

$errors = New-Object System.Collections.Generic.List[string]
$anchorCache = @{}

function Get-RelativePath($path)
{
    $path.Substring($root.Length + 1).Replace('\', '/')
}

function Get-Anchors($path)
{
    if ($anchorCache.ContainsKey($path))
    {
        return $anchorCache[$path]
    }
    $anchors = New-Object System.Collections.Generic.HashSet[string]
    $counts = @{}
    $inFence = $false
    foreach ($line in [IO.File]::ReadAllLines($path, $utf8))
    {
        if ($line -match '^\s*```')
        {
            $inFence = -not $inFence
            continue
        }
        if ($inFence -or $line -notmatch '^#{1,6}\s+(.+?)\s*#*\s*$')
        {
            continue
        }
        $text = $Matches[1] -replace '`', '' -replace '\[([^\]]*)\]\([^)]*\)', '$1'
        $slug = ($text.ToLowerInvariant() -replace '[^\p{L}\p{Nd}\p{Mn}\p{Mc} _-]', '') -replace ' ', '-'
        if ($counts.ContainsKey($slug))
        {
            $counts[$slug]++
            [void]$anchors.Add("$slug-$($counts[$slug])")
        }
        else
        {
            $counts[$slug] = 0
            [void]$anchors.Add($slug)
        }
    }
    $anchorCache[$path] = $anchors
    return $anchors
}

function Test-Target($file, $lineNo, $target)
{
    $parts = $target -split '#', 2
    $pathPart = [Uri]::UnescapeDataString($parts[0])
    $anchor = if ($parts.Count -gt 1) { [Uri]::UnescapeDataString($parts[1]) } else { $null }

    if ($pathPart -match "^$([regex]::Escape($repoUrl))/wiki/?(.*)$")
    {
        $page = if ($Matches[1]) { $Matches[1] } else { 'Home' }
        $resolved = Join-Path $wikiDir "$page.md"
    }
    elseif ($pathPart -match "^$([regex]::Escape($repoUrl))/(?:blob|tree)/main/(.+)$")
    {
        $resolved = Join-Path $root $Matches[1]
    }
    elseif ($pathPart -match "^$([regex]::Escape($repoUrl))/commit/([0-9a-f]+)$")
    {
        cmd /c "git -C `"$root`" cat-file -e $($Matches[1])^^{commit} 2>nul"
        if ($LASTEXITCODE -ne 0)
        {
            $errors.Add("$(Get-RelativePath $file):${lineNo}: 없는 커밋 $target")
        }
        return
    }
    elseif ($pathPart -match '^[a-z]+:')
    {
        return
    }
    elseif ($pathPart -eq '')
    {
        $resolved = $file
    }
    else
    {
        $resolved = Join-Path (Split-Path -Parent $file) $pathPart
    }

    if (-not (Test-Path -LiteralPath $resolved))
    {
        $errors.Add("$(Get-RelativePath $file):${lineNo}: 없는 경로 $target")
        return
    }
    if ($anchor -and $resolved -like '*.md' -and -not (Get-Anchors (Resolve-Path -LiteralPath $resolved).Path).Contains($anchor))
    {
        $errors.Add("$(Get-RelativePath $file):${lineNo}: 없는 앵커 $target")
    }
}

foreach ($file in $files)
{
    $rel = Get-RelativePath $file
    $isWiki = $rel -like 'Wiki/*'
    $inFence = $false
    $inCSharp = $false
    $lineNo = 0
    foreach ($line in [IO.File]::ReadAllLines($file, $utf8))
    {
        $lineNo++
        if ($line -match '^\s*```(\S*)')
        {
            $inFence = -not $inFence
            $inCSharp = $inFence -and $Matches[1] -eq 'csharp'
            continue
        }
        if ($inFence)
        {
            $code = $line.Trim()
            if ($isWiki -and $inCSharp -and $code -and -not $code.StartsWith('//') -and -not $sourceLines.Contains($code))
            {
                $errors.Add("${rel}:${lineNo}: 코드에 없는 예시 줄 $code")
            }
            continue
        }

        foreach ($m in [regex]::Matches($line, '\]\(([^)\s]+)(?:\s+"[^"]*")?\)'))
        {
            Test-Target $file $lineNo $m.Groups[1].Value
        }
        foreach ($m in [regex]::Matches($line, '`((?:Client|Tools|DesignTable)/[^`\s]+)`'))
        {
            $codePath = $m.Groups[1].Value.TrimEnd('/')
            if ($codePath -notmatch '[*{<]' -and -not (Test-Path -LiteralPath (Join-Path $root $codePath)))
            {
                $errors.Add("${rel}:${lineNo}: 없는 코드 경로 $codePath")
            }
        }

        if ($rel -eq 'Docs/glossary.md')
        {
            continue
        }
        $prose = $line -replace '`[^`]*`', '' -replace '\]\([^)]*\)', ']'
        foreach ($rule in $rules)
        {
            $applies = switch ($rule.Scope)
            {
                'all' { $true }
                'wiki' { $isWiki }
                'not-testing' { $rel -ne 'Docs/testing.md' }
            }
            if ($applies -and $rule.Regex.IsMatch($prose))
            {
                $errors.Add("${rel}:${lineNo}: 용어집 [$($rule.Name)] $($line.Trim())")
            }
        }
    }
}

$errors | ForEach-Object { Write-Output $_ }
if ($errors.Count -gt 0)
{
    Write-Output "FAILED: $($errors.Count) problems in $($files.Count) files"
    exit 1
}
Write-Output "ALL PASSED: $($files.Count) files"
