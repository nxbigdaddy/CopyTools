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
    & (Join-Path $PSScriptRoot 'build-help.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "CHM 帮助构建失败，退出代码：$LASTEXITCODE"
    }

    & $dotnetPath build 'CopyTools.sln' --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "Release 构建失败，退出代码：$LASTEXITCODE"
    }

    & $dotnetPath run `
        --project 'tests\CopyTools.Core.Tests\CopyTools.Core.Tests.csproj' `
        --configuration Release `
        --no-build
    if ($LASTEXITCODE -ne 0) {
        throw "核心测试失败，退出代码：$LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
