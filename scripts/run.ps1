[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = 'C:\Program Files\dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    throw "未找到 64 位 .NET：$dotnetPath"
}

$env:DOTNET_CLI_HOME = Join-Path $env:TEMP 'copytools-dotnet-home'
$env:NUGET_PACKAGES = Join-Path $env:TEMP 'copytools-nuget-packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'

Push-Location $projectRoot
try {
    & $dotnetPath run `
        --project 'src\CopyTools.Desktop\CopyTools.Desktop.csproj' `
        --configuration Debug

    if ($LASTEXITCODE -ne 0) {
        throw "CopyTools 启动失败，退出代码：$LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
