$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

Push-Location $projectRoot
try {
    [xml]$project = Get-Content (Join-Path $projectRoot 'ValoTools.csproj')
    $projectVersion = [string]$project.Project.PropertyGroup.Version
    $installerSource = Get-Content (Join-Path $PSScriptRoot 'ValoTools.iss') -Raw
    if ($installerSource -notmatch '(?m)^#define AppVersion "([^"]+)"$' -or $Matches[1] -ne $projectVersion) {
        throw 'Proje ve kurulum sürüm numaraları eşleşmiyor.'
    }

    dotnet publish ValoTools.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
        -o publish\ValoTools\win-x64
    if ($LASTEXITCODE -ne 0) { throw 'ValoTools yayını başarısız oldu.' }

    $compilerCandidates = @(
        (Join-Path $projectRoot '.tools\InnoSetup\ISCC.exe'),
        (Join-Path ${env:ProgramFiles} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe')
    )
    $compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $compiler) { throw 'Inno Setup 7 ISCC.exe bulunamadı.' }

    & $compiler --quiet-progress (Join-Path $PSScriptRoot 'ValoTools.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Kurulum dosyası derlenemedi.' }
}
finally {
    Pop-Location
}
