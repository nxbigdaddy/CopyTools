[CmdletBinding()]
param(
    [string]$Version = '1.1.0',
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = 'C:\Program Files\dotnet\dotnet.exe'
$artifacts = Join-Path $projectRoot 'artifacts\release'
$staging = Join-Path $artifacts 'staging'
$portable = Join-Path $staging 'portable'
$setupOutput = Join-Path $staging 'setup'
$expectedRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$resolvedArtifacts = [IO.Path]::GetFullPath($artifacts)
if (-not $resolvedArtifacts.StartsWith($expectedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Release directory escaped artifacts root: $resolvedArtifacts"
}
function Remove-DirectoryWithRetry([string]$Path) {
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        if (-not (Test-Path -LiteralPath $Path)) { return }
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -eq 5) { throw }
            Start-Sleep -Milliseconds (500 * $attempt)
        }
    }
}
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
Remove-DirectoryWithRetry $staging
New-Item -ItemType Directory -Path $portable, $setupOutput -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $env:TEMP 'copytools-publish-dotnet-home'
$env:NUGET_PACKAGES = Join-Path $env:TEMP 'copytools-publish-nuget-packages'
$common = @('--configuration','Release','--runtime',$Runtime,'--self-contained','true','--nologo',"-p:Version=$Version",'-p:DebugType=None','-p:DebugSymbols=false')
Push-Location $projectRoot
try {
    & (Join-Path $PSScriptRoot 'build-help.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'CHM help build failed.' }
    & $dotnetPath publish 'src\CopyTools.Desktop\CopyTools.Desktop.csproj' @common '--output' $portable
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
    & $dotnetPath publish 'src\CopyTools.Service\CopyTools.Service.csproj' @common '--output' (Join-Path $portable 'service')
    if ($LASTEXITCODE -ne 0) { throw 'Service publish failed.' }
    & $dotnetPath publish 'src\CopyTools.Cli\CopyTools.Cli.csproj' @common '--output' (Join-Path $portable 'cli')
    if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed.' }
    Copy-Item -LiteralPath 'README.md' -Destination $portable
    Copy-Item -LiteralPath 'docs' -Destination (Join-Path $portable 'docs') -Recurse
    Copy-Item -LiteralPath 'scripts\install-service.ps1','scripts\uninstall-service.ps1' -Destination $portable
    $payload = Join-Path $staging 'CopyToolsPayload.zip'
    Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $payload -CompressionLevel Optimal
    & $dotnetPath publish 'src\CopyTools.Installer\CopyTools.Installer.csproj' @common '--output' $setupOutput `
        '--no-restore' '-p:PublishSingleFile=true' "-p:PayloadZip=$payload"
    if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed.' }

    $portableZip = Join-Path $artifacts "CopyTools-Portable-$Version-$Runtime.zip"
    Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $portableZip -CompressionLevel Optimal -Force
    $setup = Join-Path $artifacts "CopyTools-Setup-$Version-$Runtime.exe"
    Copy-Item -LiteralPath (Join-Path $setupOutput 'CopyTools-Setup.exe') -Destination $setup
    $portableHash = (Get-FileHash -LiteralPath $portableZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $setupHash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = [ordered]@{
        formatVersion = 1; version = $Version; downloadUrl = [IO.Path]::GetFileName($portableZip)
        sha256 = $portableHash; size = (Get-Item -LiteralPath $portableZip).Length
        releaseNotes = "CopyTools $Version complete release."
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'update-manifest.json') -Encoding utf8
    @(
        "$portableHash  $([IO.Path]::GetFileName($portableZip))"
        "$setupHash  $([IO.Path]::GetFileName($setup))"
    ) | Set-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii
    $verifyProcess = Start-Process -FilePath $setup -ArgumentList @('--verify-payload') -PassThru -WindowStyle Hidden
    if (-not $verifyProcess.WaitForExit(180000)) { Stop-Process -Id $verifyProcess.Id -Force; throw 'Installer payload validation timed out.' }
    if ($verifyProcess.ExitCode -ne 0) { throw 'Installer payload validation failed.' }
    $unsafeProcess = Start-Process -FilePath $setup -ArgumentList @('--validate-install-dir','C:\') -PassThru -WindowStyle Hidden
    $unsafeProcess.WaitForExit()
    if ($unsafeProcess.ExitCode -eq 0) { throw 'Installer accepted a disk root as install directory.' }
    $installTest = Join-Path $staging 'install-test'
    $quotedInstallTest = '"' + $installTest + '"'
    $installProcess = Start-Process -FilePath $setup -ArgumentList @('--quiet','--no-shortcuts','--no-register','--install-dir',$quotedInstallTest) -PassThru -WindowStyle Hidden
    if (-not $installProcess.WaitForExit(300000)) { Stop-Process -Id $installProcess.Id -Force; throw 'Isolated installer test timed out.' }
    if ($installProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $installTest 'CopyTools.exe'))) { throw 'Isolated installer test failed.' }
    & (Join-Path $installTest 'service\CopyTools.Service.exe') '--check' '--data-dir' (Join-Path $staging 'service-check')
    if ($LASTEXITCODE -ne 0) { throw 'Published service self-check failed.' }
    & (Join-Path $installTest 'cli\copytools.exe') '--help'
    if ($LASTEXITCODE -ne 0) { throw 'Published CLI self-check failed.' }
    $resolvedStaging = [IO.Path]::GetFullPath($staging)
    if (-not $resolvedStaging.StartsWith($resolvedArtifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Staging directory escaped release root: $resolvedStaging"
    }
    Remove-DirectoryWithRetry $resolvedStaging
    Write-Host "Release complete: $artifacts"
}
finally { Pop-Location }
