# Hoja de ruta — TVLauncher

> Documento vivo. Refleja la visión del proyecto y el estado real del repositorio.
> Última actualización: 03/11/2026.

## Visión

Un launcher de Minecraft ligero, rápido y de código abierto que no dependa de librerías
de terceros para el arranque: control total del manifiesto, classpath, argumentos JVM,
nativos y diagnóstico de fallos. La prioridad es que **cualquier instancia arranque hasta
el menú principal**, con logs que permitan diagnosticar el fallo exacto si no lo hace.

## Stack

| Pieza | Uso |
|---|---|
| C# / .NET 8.0 | Núcleo |
| Avalonia UI 11.1.3 | Interfaz (MVVM con CommunityToolkit.Mvvm) |
| Microsoft.Extensions.DependencyInjection | Composición de servicios |
| Silk.NET.OpenGL + SkiaSharp | Visor 3D de skins y texturas |
| Newtonsoft.Json | Metadata de versiones y configuración |
| CmlLib.Core.Auth | Solo modelos de sesión heredados (el arranque es propio) |
| Fabric 1.21.11 | Mod cliente de cosméticos (`tvl-cosmetics`) |

## Arquitectura actual

```text
Services/
  GameFilesService.cs      Manifiesto, versiones, librerías, nativos, assets,
                           instalación de Forge/NeoForge, jar del perfil
  MinecraftService.cs      Orquestación del lanzamiento, logs y diagnóstico de crash
  JavaService.cs           Detección, validación y selección de JRE/JDK
  AuthService.cs           Cuentas y sesiones (offline + Microsoft)
  MicrosoftAuthService.cs  OAuth 2.0 (localhost + fallback a device code)
  InstanceManager.cs       CRUD de instancias
  CosmeticsService.cs      Cosméticos, sincronización y skin local
  MojangSkinUploader.cs    Subida de skin a la cuenta premium
  SkinImageNormalizer.cs   Normalización y remapeo de skins 64x32
  ModrinthService.cs       Búsqueda y descarga de mods
  SkinService.cs           Skins y capas
Helpers/
  LaunchArgumentsBuilder.cs  Construcción de argumentos y classpath
  RuleEvaluator.cs           Reglas os/arch/features del metadata
Controls/                 Visor 3D (SkinPreviewGLControl), banner
ViewModels/ + Views/      MVVM
Models/                   Config, instancias, cuentas, metadata de versión, Java
fabric-mod/               Mod cliente Fabric 1.21.11
```

## Estado actual

- Build: **0 errores** (advertencias preexistentes de MVVM/Avalonia/nullable).
- Tests .NET: **207** en `tests/TVLauncher.Tests` (xUnit, sin red ni archivos reales del juego).
- Tests del mod: **35** (JUnit + Gradle).
- Arranque verificado de extremo a extremo (hasta el menú principal):

| Versión | Java | Resultado |
|---|---|---|
| Forge 1.20.1 (`1.20.1-forge-47.4.28`) | 17 | Menú ~20 s |
| Forge 1.20.1 legacy (`1.20.1-forge-47.1.106`) | 17 | Menú ~24 s |
| Forge 1.16.5 (`1.16.5-forge-36.2.42`) | 8 | Menú ~18 s |
| Forge 1.12.2 (`14.23.5.2864`) | 8 | Menú ~10 s |
| Forge 1.8.9 (`11.15.1.2318`) | 8 | Menú ~8 s |
| Forge 1.7.10 (`10.13.4.1614-1.7.10`) | 8 | Menú ~11 s |
| NeoForge 1.21.1 (`neoforge-21.1.265`) | 21 | Instalación + menú ~19 s |
| Fabric 1.21.11 (`fabric-loader-0.20.1-1.21.11`) | 21 | Menú ~22 s |

Fallo real simulado (JVM con flag inválido): el stack de `stderr` aparece en
`minecraft-console.log` y `tvlauncher.log`, y se adjunta el `crash-report` si existe.

## Cosméticos in-game (beta)

- Capa y sombrero TVLauncher renderizados en Fabric 1.21.11.
- Skin local: el launcher copia la skin equipada a la instancia y el mod la aplica al
  jugador local, tanto en tercera como en primera persona.
- Cuentas premium: al equipar una skin se sube a Mojang; las cuentas offline solo la
  aplican en el cliente.
- Backend global opcional con Supabase (`cosmetics_profiles` + Edge Function).
- El checkbox **Cosméticos** de Ajustes actúa como interruptor real: escribe
  `enabled:false` en la instancia y el mod deja de renderizar.
- Limitación conocida: el mod añade una regresión de rendimiento de ~1–2 ms/frame en
  algunos equipos (medida en un Ryzen 7 7730U con iGPU). Queda para una actualización.

## Fases

### Fase 0 — Base de producto ✅

- [x] UI principal, gestión de instancias (crear/editar/eliminar)
- [x] Visor 3D de skins con capa exterior y transparencias
- [x] Sistema de cosméticos y armario
- [x] Buscador de mods (Modrinth)
- [x] Wizard de primera ejecución y ajustes
- [x] Inyección de servidor SMP en `servers.dat`
- [x] Shell oscuro, sidebar y design system

### Fase 1 — Núcleo de lanzamiento propio ✅

- [x] Manifiesto completo de versiones (921 versiones) con caché
- [x] Resolución de `inheritsFrom` (Fabric/Forge/NeoForge)
- [x] Reglas de librerías (`os`, `arch`, `features`) y nativos
- [x] Assets e índices de assets
- [x] Detección/validación de Java y selección por versión requerida
- [x] Forge y NeoForge: moderno, 1.20.1 y legacy (1.7.10 – 1.12.2)
- [x] Fabric
- [x] OAuth Microsoft (localhost con fallback a device code)
- [x] Jar del perfil para ModLauncher (`versions/{perfil}/{perfil}.jar`)
- [x] Fusión de librerías con prioridad al perfil (evita log4j duplicado)
- [x] Diagnóstico de crash en ambos logs

### Fase 2 — Estabilización 🔵 (en curso)

- [x] `.gitignore` corregido y limpieza de artefactos (`obj.zip`)
- [x] Historial por bloques lógicos (núcleo, autorización/UI)
- [x] Proyecto de tests (xUnit) para `RuleEvaluator`, `LaunchArgumentsBuilder`,
      `MergeVersions`/`LibraryKey`, `EnsureProfileJar`, `SecretMasker`, SHA1/SHA-512,
      cuentas cifradas, cosméticos, skins y Modrinth
- [x] Verificación **SHA1** de las descargas del juego: el `.part` se valida contra el
      hash publicado antes de pasar a definitivo
- [x] Verificación **SHA-512** de los jars de Modrinth
- [x] Tokens enmascarados en los logs (`SecretMasker`)
- [x] Cancelación real de descargas de Modrinth
- [ ] Matriz de arranque automatizada (script reproducible, sin harness manual)
- [ ] Cubrir 1.16.1 y 1.17–1.19 en la matriz
- [ ] Reintentos/reanudación de descargas del juego
- [ ] Verificar integridad de archivos ya presentes (hoy la reutilización se decide
      por tamaño para no re-hashear 1.2 GB en cada arranque: ~12 s medidos)

### Fase 3 — Calidad de producto 🟡

- [x] Shell oscuro y design system
- [x] Mensajes de estado durante el lanzamiento y la descarga
- [x] Diagnóstico de crash en logs
- [ ] Diagnóstico visible en la UI (ver/copiar log, abrir crash report)
- [ ] Manejo de errores accionable en toda la UI
- [ ] i18n (todo el texto está hardcodeado en español)
- [ ] Accesibilidad y navegación por teclado
- [ ] Pulido final del visor 3D y del armario

### Fase 4 — Distribución 🟡

- [x] README corregido (el arranque ya no usa CmlLib)
- [x] Paquete beta portable y build reproducible
- [ ] Firma de código y instalador (Smart App Control bloquea el `.exe` sin firmar)
- [ ] Auto-actualización del launcher
- [ ] CI (build + release) y changelog

### Fase 5 — Alcance futuro ⬜

- [ ] Quilt real (hoy solo aparece en filtros de Modrinth)
- [ ] OptiFine (hoy solo se respeta un jar ya instalado)
- [ ] Modpacks `.mrpack` y resolución de dependencias de Modrinth
- [ ] Auto-descarga de JRE (hoy solo detección)
- [ ] Perfiles de servidores/multijugador ampliados

## Deuda técnica conocida

1. **Rendimiento de cosméticos**: el mod añade ~1–2 ms/frame en algunos equipos.
   Pendiente de perfilado A/B y optimización en una actualización.
2. **Skins legacy 64x32**: se remapean brazos/piernas, pero conviene probar más variantes
   antes de considerarlo cerrado.
3. **Matriz de arranque**: verificada a mano; falta automatizarla.
4. **Descargas del juego**: sin reanudación/retry; los archivos presentes se reutilizan
   por tamaño.
5. **Firma**: el `.exe` sin firmar es bloqueado por Smart App Control en Windows 11.
6. **Logs de Minecraft**: acumulativos con rotación a `.old.log` al superar 5 MB.
7. **Supabase**: el backend es opcional; sin URL/anon key los cosméticos remotos no
   funcionan, pero el juego y los cosméticos locales sí.

## Cómo verificar un cambio

```powershell
dotnet build TVLauncher.csproj --configuration Debug
dotnet test tests\TVLauncher.Tests\TVLauncher.Tests.csproj
# Mod:
cd fabric-mod
.\gradlew.bat build
# Arrancar la app y lanzar una instancia; revisar:
#   %LOCALAPPDATA%\TVLauncher\logs\tvlauncher.log
#   %LOCALAPPDATA%\TVLauncher\logs\minecraft-console.log
```

Criterio de aceptación de un cambio en el núcleo: la instancia llega al menú principal y,
si falla, ambos logs contienen la causa (`[stderr]` y/o `crash-reports`).
