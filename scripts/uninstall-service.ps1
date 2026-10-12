[CmdletBinding()]
param(
    [string]$ServiceExecutable = ''
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
$process = Start-Process -FilePath $resolvedExecutable -ArgumentList @('--uninstall') -Verb RunAs -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "服务卸载程序退出码：$($process.ExitCode)" }
Write-Host 'CopyTools 后台服务已卸载；ProgramData 中的配置和日志保留。'
