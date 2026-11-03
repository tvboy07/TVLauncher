param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root

try {
    Write-Host "Publicando build self-contained..." -ForegroundColor Cyan
    dotnet publish TVLauncher.csproj `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        --output "publish\win-x64" `
        --nologo

    Write-Host "Publicando paquete framework-dependent (workaround Smart App Control)..." -ForegroundColor Cyan
    dotnet publish TVLauncher.csproj `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained false `
        -p:PublishSingleFile=false `
        --output "publish\beta-dotnet" `
        --nologo

    $beta = Join-Path $root "publish\beta"
    Remove-Item $beta -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $beta | Out-Null

    Copy-Item "publish\win-x64\TVLauncher.exe" (Join-Path $beta "TVLauncher.exe")
    if (Test-Path "publish\win-x64\TVLauncher.pdb") {
        Copy-Item "publish\win-x64\TVLauncher.pdb" (Join-Path $beta "TVLauncher.pdb")
    }
    Copy-Item "publish\beta-dotnet" (Join-Path $beta "beta-dotnet") -Recurse
    Copy-Item "docs\BETA.md" (Join-Path $beta "LEEME-BETA.md")

    @"
@echo off
cd /d "%~dp0beta-dotnet"
dotnet TVLauncher.dll
"@ | Set-Content (Join-Path $beta "Iniciar-TVLauncher.cmd") -Encoding ASCII

    $hash = (Get-FileHash (Join-Path $beta "TVLauncher.exe") -Algorithm SHA256).Hash
    @"
TVLauncher beta
SHA-256 TVLauncher.exe: $hash

Si Windows bloquea TVLauncher.exe por Smart App Control, ejecutá
Iniciar-TVLauncher.cmd (requiere .NET 8 Desktop Runtime).
"@ | Set-Content (Join-Path $beta "SHA256.txt") -Encoding UTF8

    New-Item -ItemType Directory -Force (Join-Path $root "artifacts") | Out-Null
    $zip = Join-Path $root "artifacts\TVLauncher-beta-win-x64.zip"
    Compress-Archive -Path (Join-Path $beta "*") -DestinationPath $zip -Force

    Write-Host ""
    Write-Host "Beta lista:" -ForegroundColor Green
    Write-Host "  Carpeta: $beta"
    Write-Host "  ZIP:     $zip"
    Write-Host "  SHA256:  $hash"
}
finally {
    Pop-Location
}
