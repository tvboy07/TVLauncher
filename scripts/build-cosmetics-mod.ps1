# Compila el mod Fabric y copia el jar a Resources/ para que quede embebido
# en el launcher. Requiere un JDK 25 o superior porque Fabric Loom 1.19.3
# exige Gradle 9.8, y Gradle 9.8 exige Java 25 para ejecutarse. El mod sigue
# compilándose con Java 21 (options.release = 21).

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$modDir = Join-Path $root "fabric-mod"
$jar = Join-Path $modDir "build\libs\tvl-cosmetics-0.1.0.jar"
$target = Join-Path $root "Resources\tvl-cosmetics.jar"

if (-not $env:JAVA_HOME) {
    throw "JAVA_HOME no está definido. Apunta a un JDK 25 o superior para compilar el mod."
}

Write-Host "JAVA_HOME: $env:JAVA_HOME"

Push-Location $modDir
try {
    & .\gradlew.bat build
    if ($LASTEXITCODE -ne 0) {
        throw "La compilación de Gradle falló (código $LASTEXITCODE)"
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item $jar $target -Force
    Write-Host "Jar copiado a $target"
}
finally {
    Pop-Location
}
