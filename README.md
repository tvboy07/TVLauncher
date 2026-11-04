# TVLauncher

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-UI-blue.svg)](https://avaloniaui.net/)

TVLauncher es un launcher ligero y de código abierto para Minecraft, creado con **C#**, **.NET 8.0** y **Avalonia UI**. El arranque es **propio**: controla el manifiesto de versiones, el classpath, los argumentos JVM, los nativos, los assets y el diagnóstico de fallos sin delegar el lanzamiento en CmlLib.

> Estado: **beta**. Consultá [`docs/BETA.md`](docs/BETA.md) para limitaciones conocidas y cómo probarla.

## Características

- **Interfaz moderna** con Avalonia UI y shell oscuro.
- **Gestión de instancias**: crear, editar y ejecutar instancias aisladas.
- **Autenticación dual**: cuentas offline y Microsoft (OAuth 2.0 con PKCE, fallback a device code).
- **Modloaders**: Forge, NeoForge y Fabric con resolución de `inheritsFrom`, librerías, nativos y jar del perfil.
- **Java**: detección, validación y selección automática de JRE/JDK por versión requerida.
- **Diagnóstico de arranque**: `tvlauncher.log`, `minecraft-console.log` y crash reports.
- **Visor 3D de skins** con capa exterior, sombrero, transparencias, animación y modelo classic/slim.
- **Cosméticos in-game** para Fabric 1.21.11: capa y sombrero TVLauncher, skin local y subida de skin a Mojang para cuentas premium.
- **Buscador de mods (Modrinth)** con descarga a `{instancia}/mods`, cancelación y verificación SHA-512.
- **Asignación de RAM** por instancia y optimización de memoria opcional.
- **Servidor SMP** inyectado en `servers.dat`.
- **Wizard de primera ejecución**.

## Tecnologías

| Tecnología | Uso |
|---|---|
| C# / .NET 8.0 | Núcleo del launcher |
| Avalonia UI 11.1.3 | Interfaz multiplataforma (MVVM con CommunityToolkit.Mvvm) |
| Microsoft.Extensions.DependencyInjection | Composición de servicios |
| Silk.NET.OpenGL + SkiaSharp | Visor 3D y generación de texturas |
| Newtonsoft.Json | Metadata de versiones y configuración |
| CmlLib.Core.Auth | Solo modelos de sesión heredados; el arranque es propio |
| Fabric Loader / Fabric API | Mod cliente `tvl-cosmetics` para Minecraft 1.21.11 |

## Requisitos

- Windows 10/11 (el paquete beta está pensado para Windows x64).
- .NET 8.0 SDK para desarrollo; .NET 8.0 Desktop Runtime para el paquete portable.
- Java compatible con la versión de Minecraft (el launcher detecta instalaciones).
- OpenGL 2.1+ / OpenGL ES 2.0+ para el visor.
- Para cosméticos in-game: Minecraft 1.21.11 con Fabric Loader y Fabric API.

## Compilar y ejecutar

```powershell
dotnet restore
dotnet build TVLauncher.csproj
dotnet run --project TVLauncher.csproj
```

Tests:

```powershell
dotnet test tests\TVLauncher.Tests\TVLauncher.Tests.csproj
```

El mod Fabric se compila aparte:

```powershell
cd fabric-mod
$env:JAVA_HOME = "C:\ruta\a\jdk-21"   # Fabric Loom 1.19.3 requiere Java 25 para construir
.\gradlew.bat build
```

El jar resultante se copia a `Resources/tvl-cosmetics.jar` y se embebe en el `.exe`.

## Estructura

```text
TVLauncher/
├── Assets/                  Recursos gráficos
├── Controls/                Visor 3D OpenGL
├── Helpers/                 Argumentos, reglas, enmascarado de secretos
├── Models/                  Config, instancias, cuentas, metadata
├── Services/                Lanzamiento, Java, auth, cosméticos, Modrinth
├── ViewModels/              MVVM
├── Views/                   Ventanas y vistas Avalonia
├── fabric-mod/              Mod cliente Fabric 1.21.11
├── tests/TVLauncher.Tests/  Suite xUnit
└── docs/                    Roadmap, inventario y guía de beta
```

## Documentación

- [`docs/ROADMAP.md`](docs/ROADMAP.md): visión, fases y estado.
- [`docs/INVENTARIO.md`](docs/INVENTARIO.md): inventario técnico detallado.
- [`docs/BETA.md`](docs/BETA.md): limitaciones y pruebas de la beta.
- [`docs/SECURITY.md`](docs/SECURITY.md): cuentas, tokens, DPAPI y Supabase.
- [`docs/SUPABASE_SETUP.md`](docs/SUPABASE_SETUP.md): backend opcional de cosméticos.

## Licencia

Este proyecto está licenciado bajo la [Licencia MIT](LICENSE).

## Créditos

- [Mojang API](https://wiki.vg/Mojang_API) por perfiles, skins y sesiones.
- [Crafatar](https://crafatar.com/) como fallback de skins.
- [Modrinth API](https://docs.modrinth.com/api-spec/) para el buscador de mods.
- [CmlLib.Core.Auth](https://github.com/CmlLib/CmlLib.Core) por los modelos de sesión heredados.
- [Fabric](https://fabricmc.net/) por el ecosistema del mod cliente.

---

Hecho con ❤️ por el equipo de TVLauncher.
