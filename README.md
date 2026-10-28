# TVLauncher

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-UI-blue.svg)](https://avaloniaui.net/)

TVLauncher es un launcher ligero, moderno y de código abierto para Minecraft, creado con **C#**, **.NET 8.0** y **Avalonia UI**. Diseñado para ser rápido, con bajo consumo de recursos y con una experiencia limpia y minimalista.

## Características

- **Interfaz moderna con Avalonia UI** - Multiplataforma y fluida.
- **Gestión de instancias** - Crea, edita y ejecuta múltiples instancias de Minecraft aisladas.
- **Autenticación dual** - Soporta cuentas offline y autenticación con Microsoft (OAuth2).
- **Soporte para modloaders** - Integración con Forge, Fabric y NeoForge vía [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core).
- **Visor 3D de Skins** - Previsualización en tiempo real del skin con modelo completo (cabeza, cuerpo, brazos, piernas), capa exterior/hat y soporte para transparencias.
- **Sistema de cosméticos** - Inyección automática de cosméticos personalizados visible en multijugador.
- **Buscador de mods con Modrinth** - Busca, filtra y descarga mods directamente a tu instancia.
- **Asignación de RAM** - Control total sobre la memoria asignada por instancia.
- **Inyección automática de servidor SMP** - Añade automáticamente servidores a `servers.dat`.
- **Asistente de configuración** - Wizard de primera ejecución para configurar módulos opcionales.
- **Ligero y optimizado** - Pensado para consumir menos recursos.

## Tecnologías

| Tecnología | Uso |
|------------|-----|
| [C# / .NET 8.0](https://dotnet.microsoft.com/) | Núcleo de la aplicación |
| [Avalonia UI](https://avaloniaui.net/) | Interfaz gráfica multiplataforma |
| [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) | Lanzamiento de Minecraft y gestión de versiones |
| [Silk.NET](https://github.com/dotnet/Silk.NET) | Renderizado OpenGL para el visor 3D |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | Decodificación y procesamiento de texturas PNG |
| [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) | Arquitectura MVVM |

## Requisitos

- .NET 8.0 SDK o Runtime
- Windows 10/11, Linux o macOS (soportado por Avalonia)
- OpenGL 2.1+ / OpenGL ES 2.0+ (compatible con ANGLE)

## Instalación y desarrollo

```bash
# Clonar el repositorio
git clone https://github.com/tvboy07/TVLauncher.git
cd TVLauncher

# Restaurar dependencias
dotnet restore

# Compilar
dotnet build

# Ejecutar
dotnet run
```

## Estructura del proyecto

```
TVLauncher/
├── Assets/        # Recursos gráficos
├── Controls/      # Controles personalizados (SkinPreviewGLControl)
├── Models/        # Modelos de datos
├── Services/      # Lógica de negocio (Auth, Instance, Minecraft, Skin, etc.)
├── ViewModels/    # ViewModels MVVM
├── Views/         # Ventanas y vistas Avalonia XAML
└── App.axaml.cs   # Punto de entrada y configuración DI
```

## Licencia

Este proyecto está licenciado bajo la [Licencia MIT](LICENSE).

## Créditos

- [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) por la API de lanzamiento de Minecraft
- [Mojang API](https://wiki.vg/Mojang_API) por los datos de perfiles y skins
- [Crafatar](https://crafatar.com/) como fallback para skins
- [Modrinth API](https://docs.modrinth.com/api-spec/) para el buscador de mods

---

**Hecho con ❤️ por el equipo de TVLauncher**
