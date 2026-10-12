# CopyTools PowerShell module: wraps the copytools CLI for scripting.
# Usage: Import-Module .\scripts\CopyTools.psm1

$script:CliPath = if ($env:COPYTOOLS_CLI) {
    $env:COPYTOOLS_CLI
} else {
    Join-Path $PSScriptRoot '..\src\CopyTools.Cli\bin\Release\net8.0\win-x64\copytools.exe'
}

function Get-CopyToolsCli {
    $script:CliPath
}

function Invoke-CopyToolsCli {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & $script:CliPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "copytools command failed with exit code $LASTEXITCODE."
    }
}

function Get-CopyToolsTask {
    param([string]$DataDir)
    $args = @('tasks', '--json')
    if ($DataDir) { $args += '--data-dir', $DataDir }
    (Invoke-CopyToolsCli -Arguments $args) | ConvertFrom-Json
}

function Get-CopyToolsPreview {
    param(
        [Parameter(Mandatory)][guid]$Task,
        [string]$DataDir,
        [switch]$Details
    )
    $args = @('preview', '--task', $Task.ToString('D'), '--json')
    if ($Details) { $args += '--details' }
    if ($DataDir) { $args += '--data-dir', $DataDir }
    (Invoke-CopyToolsCli -Arguments $args) | ConvertFrom-Json
}

function Start-CopyToolsTask {
    param(
        [Parameter(Mandatory)][guid]$Task,
        [string]$DataDir,
        [switch]$AllowOverwrite,
        [switch]$AllowQuarantine
    )
    $args = @('run', '--task', $Task.ToString('D'), '--json')
    if ($AllowOverwrite) { $args += '--allow-overwrite' }
    if ($AllowQuarantine) { $args += '--allow-quarantine' }
    if ($DataDir) { $args += '--data-dir', $DataDir }
    (Invoke-CopyToolsCli -Arguments $args) | ConvertFrom-Json
}

Export-ModuleMember -Function Get-CopyToolsCli, Get-CopyToolsTask, Get-CopyToolsPreview, Start-CopyToolsTask
