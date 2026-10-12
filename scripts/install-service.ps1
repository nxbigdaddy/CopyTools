[CmdletBinding()]
param(
    [string]$ServiceExecutable = '',
    [string]$DataDirectory = (Join-Path $env:ProgramData 'CopyTools')
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ServiceExecutable)) {
    $packagedExecutable = Join-Path $PSScriptRoot 'service\CopyTools.Service.exe'
    $ServiceExecutable = if (Test-Path -LiteralPath $packagedExecutable) {
        $packagedExecutable
    } else {
        Join-Path $PSScriptRoot '..\artifacts\service\CopyTools.Service.exe'
    }
}
$resolvedExecutable = (Resolve-Path -LiteralPath $ServiceExecutable).Path
$process = Start-Process -FilePath $resolvedExecutable -ArgumentList @('--install', '--data-dir', $DataDirectory) -Verb RunAs -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "服务安装程序退出码：$($process.ExitCode)" }
Write-Host "CopyTools 后台服务已安装并启动。数据目录：$DataDirectory"
