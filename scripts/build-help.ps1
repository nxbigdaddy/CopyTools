[CmdletBinding()]
param([string]$CompilerPath = '')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceDirectory = Join-Path $projectRoot 'docs\help-src'
$projectFile = Join-Path $sourceDirectory 'CopyTools.hhp'
$outputDirectory = Join-Path $projectRoot 'src\CopyTools.Desktop\help'
$outputFile = Join-Path $outputDirectory 'CopyTools.chm'

if (-not $CompilerPath) {
    $fastChmCandidates = @(
        (Join-Path $projectRoot 'artifacts\tools\fastchm-api.exe'),
        (Join-Path $projectRoot 'artifacts\tools\fastchm.exe')
    )
    $workshopCandidates = @(
        'C:\Program Files (x86)\HTML Help Workshop\hhc.exe',
        'C:\Program Files\HTML Help Workshop\hhc.exe'
    )
    $fastChm = $fastChmCandidates | Where-Object {
        (Test-Path -LiteralPath $_) -and (Get-Item -LiteralPath $_).Length -gt 100000
    } | Select-Object -First 1
    if ($fastChm) {
        $CompilerPath = $fastChm
    } else {
        $CompilerPath = $workshopCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}

if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath)) {
    throw 'CHM compiler not found. Place fastchm.exe in artifacts\tools or pass -CompilerPath.'
}
$CompilerPath = [IO.Path]::GetFullPath($CompilerPath)

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$compilerName = [IO.Path]::GetFileName($CompilerPath)
$sourceEncoding = [Text.Encoding]::GetEncoding(936)
$encodedSourceFiles = @(
    'CopyTools.hhp', 'CopyTools.hhc', 'CopyTools.hhk',
    'index.html', 'quick-start.html', 'copy-sync.html', 'backup.html',
    'automation.html', 'storage.html', 'safety.html', 'settings.html',
    'troubleshooting.html'
)
foreach ($relativePath in $encodedSourceFiles) {
    $path = Join-Path $sourceDirectory $relativePath
    $text = [IO.File]::ReadAllText($path, $sourceEncoding)
    [IO.File]::WriteAllText($path, $text, $sourceEncoding)
}
Push-Location $sourceDirectory
try {
    if ($compilerName -ieq 'hhc.exe') {
        & $CompilerPath $projectFile
        $workshopOutput = Join-Path $sourceDirectory 'CopyTools.chm'
        if (-not (Test-Path -LiteralPath $workshopOutput)) { throw 'HTML Help Workshop did not create CopyTools.chm.' }
        Copy-Item -LiteralPath $workshopOutput -Destination $outputFile -Force
        Remove-Item -LiteralPath $workshopOutput -Force
    } else {
        & $CompilerPath $projectFile '-o' $outputFile
        if ($LASTEXITCODE -ne 0) { throw "CHM compiler failed with exit code $LASTEXITCODE." }
    }
} finally {
    Pop-Location
}

$bytes = [IO.File]::ReadAllBytes($outputFile)
if ($bytes.Length -lt 4 -or [Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne 'ITSF') {
    throw 'Generated help file is not a valid CHM container.'
}

if ($compilerName -ine 'hhc.exe') {
    $listing = & $CompilerPath '--list' $outputFile
    $listingText = $listing -join "`n"
    if ($LASTEXITCODE -ne 0 -or $listingText -notmatch 'index\.html' -or $listingText -notmatch 'images/01-copy-task\.png') {
        throw 'Generated CHM is missing required help content.'
    }
}

Write-Host "CHM help complete: $outputFile ($($bytes.Length) bytes)"
