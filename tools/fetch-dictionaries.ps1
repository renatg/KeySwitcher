# Downloads frequency word lists (hermitdave/FrequencyWords, OpenSubtitles 2018, CC-BY-SA 4.0)
# and writes filtered "word count" lists into src/KeySwitcher/Detection/Resources.
param([int]$Top = 40000)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'src/KeySwitcher/Detection/Resources'
New-Item -ItemType Directory -Force $out | Out-Null

$sources = @{
    ru = @{ Url = 'https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/ru/ru_50k.txt'
            Pattern = '^[а-я]+(-[а-я]+)?$'; Singles = @('я','в','и','к','с','у','о','а','ж','б') }
    en = @{ Url = 'https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/en/en_50k.txt'
            Pattern = "^[a-z]+('[a-z]+)?$"; Singles = @('a','i') }
}

foreach ($lang in $sources.Keys) {
    $src = $sources[$lang]
    Write-Host "Downloading $($src.Url)"
    $text = (Invoke-WebRequest -Uri $src.Url -UseBasicParsing).Content
    if ($text -is [byte[]]) { $text = [Text.Encoding]::UTF8.GetString($text) }

    $counts = [ordered]@{}
    foreach ($line in $text -split "`n") {
        $parts = $line.Trim() -split '\s+'
        if ($parts.Count -ne 2) { continue }
        $word = $parts[0].ToLowerInvariant().Replace('ё', 'е')
        $count = [long]$parts[1]
        if ($word -notmatch $src.Pattern) { continue }
        if ($word.Length -eq 1 -and $src.Singles -notcontains $word) { continue }
        if ($counts.Contains($word)) { $counts[$word] += $count } else { $counts[$word] = $count }
    }

    $lines = $counts.GetEnumerator() |
        Sort-Object -Property Value -Descending |
        Select-Object -First $Top |
        ForEach-Object { "$($_.Key) $($_.Value)" }
    $path = Join-Path $out "$lang.txt"
    [IO.File]::WriteAllLines($path, [string[]]$lines, (New-Object Text.UTF8Encoding($false)))
    Write-Host "$lang -> $path ($($lines.Count) words)"
}
