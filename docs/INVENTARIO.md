# TVLauncher — Inventario técnico y matriz de características

> Documento de referencia para revisión externa. Refleja el estado real del repositorio
> en el commit `0125004` (30/10/2026) más este documento.
> Mantener actualizado junto con `docs/ROADMAP.md`.

- **Versión:** 1.0.0
- **Rama:** `main`
- **Remoto:** `github.com/tvboy07/TVLauncher`
- **Estado del árbol:** limpio

---

## 0. Snapshot ejecutivo

| Métrica | Valor |
|---|---|
| Stack | C# / .NET 8.0 · Avalonia UI 11.1.3 · MVVM (CommunityToolkit) · DI (Microsoft.Extensions) |
| Código fuente propio | **52 archivos / 8.396 líneas** (sin `bin`, `obj` ni tests) |
| Tests | **9 archivos / 750 líneas / 51 casos** (xUnit) |
| Dependencias NuGet | 20 (Avalonia, Silk.NET.OpenGL, SkiaSharp, Newtonsoft.Json, CmlLib.Core residual) |
| Commits | 9 previos + este documento |
| Build | **0 errores**, 9 advertencias preexistentes (`MVVMTK0034`, `AVLN3001`, nullable) |
| Superficie de UI | 8 ventanas/vistas, 9 ViewModels, 9 servicios singleton |
| Verificación de arranque | 8 combinaciones de versión/modloader llevadas al menú principal |

### Distribución del código

| Carpeta | Archivos | Líneas | Contenido |
|---|---|---|---|
| `Services/` | 11 | 3.770 | Núcleo de lanzamiento, auth, instancias, cosméticos, Modrinth, skins |
| `ViewModels/` | 9 | 1.525 | Lógica de presentación MVVM |
| `Views/` | 16 (`8 .axaml` + `8 .cs`) | 1.520 | Ventanas y vistas Avalonia |
| `Controls/` | 3 | 640 | Visor 3D OpenGL, banner |
| `Helpers/` | 4 | 494 | Argumentos, reglas, enmascarado de secretos, modelo de skin |
| `Models/` | 5 | 277 | Config, cuentas, instancias, metadata de versión, Java |
| Raíz | 4 | 170 | `Program.cs`, `App.axaml(.cs)`, `ViewLocator.cs` |
| `tests/TVLauncher.Tests/` | 9 | 750 | Suite xUnit |

### Historial

```
0125004 docs(roadmap): tests, SHA1 y deuda de cifrado de cuentas
7133b01 feat(downloads,test): verificacion SHA1 de descargas y suite xUnit del nucleo
dc1ae0a feat(security): enmascarado de tokens en los logs de autenticacion
7c3885d feat(auth,ui): OAuth de Microsoft con fallback a device code y gestion de cuentas/instancias
3152df7 feat(launch): nucleo de lanzamiento propio y arranque Forge/NeoForge/Fabric
2c7a95d chore(repo): gitignore completo, limpieza de obj.zip y hoja de ruta en docs/
baefb6a feat(armario): capa de Mojang automatica y cosmeticos embebidos en la ventana principal
35e7554 fix(visor3d): geometria cerrada sin huecos, encuadre centrado y rotacion con puntero
081a9f0 feat: visor 3D completo con cuerpo entero, capa exterior y transparencias; logging; gitignore y README
```

---

## 1. Core de lanzamiento y motor

**Decisión de arquitectura clave:** el launcher **no delega el arranque en CmlLib**. Se escribió
un motor propio de resolución de archivos y construcción de comandos. `CmlLib.Core.Auth`
sobrevive solo como tipo de sesión heredado (`MSession` en `AuthService`), no como motor.

| Capacidad | Estado | Implementación |
|---|---|---|
| Manifiesto oficial v2 (piston-meta) | ✅ | `GameFilesService.GetManifestAsync` — caché de **6 h**, guarda anti-caché-recortada (`MinimumManifestVersions = 500`) |
| Resolución de `inheritsFrom` | ✅ | `LoadVersionTreeAsync` — padre + hijo fusionados recursivamente |
| Reglas de librerías (`os`, `arch`, `features`) | ✅ | `Helpers/RuleEvaluator` — allow/disallow acumulativo, case-insensitive, feature ausente = `false` |
| Descarga de librerías | ✅ | `EnsureLibraryAsync` — artefacto principal + clasificadores nativos; URL desde `downloads.artifact`, `library.url` o Maven de Mojang |
| Nativos (extracción) | ✅ | `LaunchArgumentsBuilder.ExtractNatives` — formato legacy (`natives` + clasificador) y moderno (`:natives-windows`), respeta `extract.exclude`, omite `META-INF/` |
| Classpath | ✅ | `BuildClasspath` — jar del perfil primero, deduplicado case-insensitive, avisa de librerías ausentes |
| **Jar del perfil (ModLauncher)** | ✅ | `EnsureProfileJar` — copia atómica (`.part` → rename) del `client.jar` a `versions/{perfil}/{perfil}.jar` cuando los args JVM contienen `${version_name}`; nunca sobrescribe un jar propio (OptiFine) |
| **Dedupe de librerías (log4j)** | ✅ | `MergeVersions` + `LibraryKey` — clave `group:artifact[:classifier]`, gana el perfil hijo y sus librerías van primero; conserva variantes de arquitectura del mismo padre |
| Forge (moderno) | ✅ | Ejecuta el instalador oficial con `--installClient` y lee el perfil embebido |
| Forge (legacy 1.7.10–1.12.2) | ✅ | `TryLegacyInstallAsync` — extrae el `universal.jar` del instalador y compone el perfil |
| NeoForge | ✅ | Mismo pipeline que Forge, con fuente de metadata propia (`maven.neoforged.net`) y versionado con sufijo |
| Fabric | ✅ | `InstallFabricAsync` — perfil desde `meta.fabricmc.net`, resolución del loader más reciente |
| Assets | ✅ | Índice + objetos content-addressed, descarga paralela con **semáforo de 24** |
| Selección de Java | ✅ | `JavaService` — detecta `JAVA_HOME`, `PATH`, runtime de Mojang, Oracle/Adoptium/Microsoft/Zulu/Corretto; valida versión y elige por `javaVersion.majorVersion` |
| Argumentos JVM/juego | ✅ | `LaunchArgumentsBuilder` — placeholders oficiales, `MinecraftArguments` legacy, argumentos condicionales por reglas, `-Dlog4j2.formatMsgNoLookups=true`, `-Djava.library.path` solo si el metadata no lo trae |
| `-DignoreList` / `${version_name}` | ✅ | Resuelven al **id del perfil resuelto**, no al de la instancia (fix del `ResolutionException` `_1._20._1` vs módulo `minecraft`) |
| Diagnóstico de crash | ✅ | Cabecera por lanzamiento en `minecraft-console.log` (versión, Java, gameDir, nativos, nº de entradas de classpath, comando con token enmascarado), prefijo `[stderr]`, rotación a `.old.log` a los 5 MB, volcado de `crash-reports/*` y `hs_err_pid*` (máx. 256 KB por archivo), `WaitForExit` sin timeout para vaciar stdout/stderr |
| Cierre por el usuario vs crash | ✅ | Flag `_stopRequested` → se registra como INFO, no como crash |
| Progreso de descarga | ✅ | Eventos `StatusChanged`, `ProgressChanged`, `DownloadProgressChanged` |

### Matriz de arranque verificada empíricamente

| Versión / loader | Java | Resultado |
|---|---|---|
| Forge 1.20.1 (`1.20.1-forge-47.4.28`) | 17 | Menú ~20 s |
| Forge 1.20.1 legacy (`1.20.1-forge-47.1.106`) | 17 | Menú ~24 s |
| Forge 1.16.5 (`1.16.5-forge-36.2.42`) | 8 | Menú ~18 s |
| Forge 1.12.2 (`14.23.5.2864`) | 8 | Menú ~10 s |
| Forge 1.8.9 (`11.15.1.2318`) | 8 | Menú ~8 s |
| Forge 1.7.10 (`10.13.4.1614-1.7.10`) | 8 | Menú ~11 s |
| NeoForge 1.21.1 (`neoforge-21.1.265`) | 21 | Instalación + menú ~19 s |
| Fabric 1.21.11 (`fabric-loader-0.20.1-1.21.11`) | 21 | Menú ~22 s |

**Criterio de aceptación:** la instancia llega al menú principal y, si falla, ambos logs
contienen la causa (`[stderr]` y/o `crash-reports`).

---

## 2. Autenticación y cuentas

| Capacidad | Estado | Detalle |
|---|---|---|
| OAuth 2.0 Microsoft (v2, `consumers`) | ✅ | `MicrosoftAuthService`, `login.microsoftonline.com/consumers/oauth2/v2.0/*` |
| **Authorization Code + PKCE** | ✅ | `code_verifier` / `code_challenge` S256; **sin `client_secret`** (app pública) |
| Redirect loopback | ✅ | `TcpListener` en `http://localhost:{puerto}/`, puerto efímero, navegador del sistema |
| **Fallback a Device Code** | ✅ | `authorization_pending` / `slow_down` (incremento de intervalo) y expiración |
| Cadena Xbox → Minecraft | ✅ | XBL → XSTS → `login_with_xbox` → perfil |
| Client ID configurable | ✅ | `AppConfig.MicrosoftClientId` (vacío → app pública `c36a9fb6-…`); sustituible sin recompilar |
| Cuentas offline | ✅ | Nombre de usuario y UUID determinista |
| Gestión de cuentas | ✅ | Alta, selección activa, borrado, persistencia en `accounts.json` |
| Persistencia de sesión | ⚠️ | `AccessToken` y `RefreshToken` **en claro** en `%LOCALAPPDATA%\TVLauncher\accounts.json` (cifrado pendiente) |
| Enmascarado de tokens en logs | ✅ | `Helpers/SecretMasker` — `access_token`, `refresh_token`, `id_token`, `Token`, `AuthorizationToken`, `user_hash`, `uhs` → `***`; enmascara **antes** de truncar |
| Renovación de token | ⚠️ | Se persiste `refresh_token`, pero no hay refresh proactivo: al caducar hay que reautenticar |

---

## 3. Interfaz y módulos de UI (Avalonia)

`Program.cs` → `App.axaml.cs` (`ServiceCollection` + `ConfigureServices`) → `MainWindow` con
`MainWindowViewModel`. Servicios como singleton; ViewModels y ventanas como transient.

| Módulo | Vista / VM | Estado | Detalle |
|---|---|---|---|
| Ventana principal | `MainWindow` / `MainWindowViewModel` | ✅ | Cuenta activa, lista de instancias, acciones **JUGAR / EDITAR / CARPETA**, progreso, estado de juego |
| **Visor 3D de skins** | `Controls/SkinPreviewGLControl` (603 líneas) / `SkinsPreviewViewModel` | ✅ | OpenGL vía Silk.NET: cuerpo completo, **capa exterior/hat**, transparencias, geometría cerrada sin huecos, encuadre centrado, rotación con puntero, auto-rotación, animación de caminar, classic/slim |
| Skins y capas | `SkinService` | ✅ | `sessionserver.mojang.com` con fallback a Crafatar; caché de skin y capa |
| **Armario / cosméticos** | `Views/CosmeticsView` / `CosmeticsService` | ⚠️ | Catálogo local, equipar/desequipar, inyección del jar en la instancia. **No hay CDN operativo**: `cdn.tvlauncher.net` no resuelve y `147.15.17.142` no expone 80/443 (ver §5) |
| Gestor de instancias | `CreateInstanceWindow`, `InstanceEditWindow` | ✅ | Crear/editar/borrar; versión, modloader + versión de loader, RAM mín/máx, resolución, `JVMArgs`, Java, directorio |
| Wizard de primera ejecución | `SetupWizardWindow` / `SetupWizardViewModel` | ✅ | Gateado por `AppConfig.FirstRun`, `CompleteFirstRun()` |
| Ajustes | `SettingsWindow` / `SettingsViewModel` | ✅ | Java, RAM, tema, Modrinth, cosméticos, SMP por defecto, cerrar al lanzar / mantener abierto |
| **Modrinth** | `ModrinthBrowserWindow` / `ModrinthBrowserViewModel` + `ModrinthService` | ⚠️ | Búsqueda (query, versión, loader), detalle, versiones y **descarga del archivo primario a `{instance}/mods`**. **No resuelve dependencias** ni instala `.mrpack` |
| Servidor SMP por defecto | `InstanceManager.GenerateServersDat` | ⚠️ | Escribe `servers.dat`; por defecto apunta a `147.15.17.142:25565` (verificado: puerto abierto) |
| Banner | `Controls/AdsBanner` | ℹ️ | Componente de banner en la vista principal |
| i18n | — | ❌ | Texto hardcodeado en español; `Language` existe en config pero no se usa |
| Accesibilidad / teclado | — | ❌ | No implementada |

---

## 4. Integridad y testing

| Capacidad | Estado | Detalle |
|---|---|---|
| **SHA1 en descargas** | ✅ | `EnsureFileAsync(url, path, size, sha1, ct, fatal)` valida el `.part` **antes** de renombrarlo; si no coincide, borra el temporal y falla. Cubre `client.jar`, librerías, nativas, asset index, log config, metadata de versión y assets |
| Política de reutilización | ⚠️ | Los archivos presentes se reutilizan **por tamaño** (revalidar todo cuesta ~12 s: 1.214 MB de assets + 536 MB de librerías + 187 MB de versiones) |
| Rechazo de contenido corrupto | ✅ | Probado contra servidor HTTP local real: hash incorrecto → no se publica ni queda `.part` |
| **Suite xUnit** | ✅ | `tests/TVLauncher.Tests`, **51 casos**, sin red externa ni archivos reales del juego (`TempDirectory` aislado) |
| Aislamiento de tests | ✅ | `InternalsVisibleTo`, `tests/**` excluido del glob de compilación, `EnsureProfileJar` parametrizado |
| Verificación por commit | ✅ | Cada commit compilado y testeado en worktree aislado (7 tests en `dc1ae0a`, 51 en `7133b01`) |
| CI | ❌ | No hay pipeline |

### Composición de la suite (51 casos = 50 métodos, 1 `[Theory]` con 2 entradas)

| Archivo | Casos | Qué garantiza |
|---|---|---|
| `RuleEvaluatorTests` | 10 | allow/disallow, `os`, `arch`, `features`, feature ausente = falsa, case-insensitive |
| `SecretMaskerTests` | 6 | tokens Microsoft/XBL/XSTS/uhs, JSON compacto e **indentado**, enmascarar antes de truncar, nulos |
| `FileIntegrityTests` | 10 | SHA1 conocido (`"abc"`), mismo tamaño + hash distinto, mayúsculas, tamaño desconocido, ausente |
| `DownloadIntegrityTests` | 3 | Descarga real contra `TcpListener` local: hash correcto instala, incorrecto no publica, válido no re-descarga |
| `VersionMergeTests` | 10 | log4j 2.8.1 → 2.15.0 sin duplicar, librerías del hijo primero, variantes de arquitectura, `ClientSourceId`, `mainClass` |
| `LaunchArgumentsBuilderTests` | 7 | placeholders, `${version_name}` al perfil resuelto, reglas por features, legacy con `-cp`, `ToLogString` enmascarado |
| `ProfileJarTests` | 4 | copia atómica, respeta jar propio (OptiFine), no copia sin `${version_name}` |

### Correcciones reales cerradas

1. **`ResolutionException` en Forge 1.20.1**: classpath con `versions/{mc}/{mc}.jar` mientras el perfil esperaba `${version_name}.jar` en `-DignoreList` → módulo automático `_1._20._1` en conflicto con `minecraft`.
2. **`NoSuchMethodError: ThrowableProxy.formatExtendedStackTraceTo` en 1.16.5**: dos `log4j-core` en el classpath (2.8.1 padre + 2.15.0 perfil) → dedupe por `group:artifact[:classifier]`.
3. **Tokens en logs**: una sesión real dejó 4 entradas con `access_token`/`refresh_token` en `tvlauncher.log`; hoy se enmascaran y el log local fue limpiado retroactivamente.

### Auditoría de seguridad

- **0** secretos, API keys, contraseñas o claves privadas en los archivos trackeados **ni en los 9 commits del historial** (`git log --all -p`).
- Único identificador: `DefaultClientId` (público por diseño, app pública con PKCE). **Ningún `client_secret`**.
- Sin binarios versionados, sin `appsettings*.json`, sin credenciales en la URL del remoto.
- Riesgo residual **local, no de repositorio**: `accounts.json` en claro y tokens históricos en logs (limpiados).

---

## 5. Endpoints por defecto

| Endpoint | Valor actual | Verificación |
|---|---|---|
| Servidor SMP por defecto | `147.15.17.142:25565` | TCP **abierto** (comprobado) |
| CDN de cosméticos | `https://cdn.tvlauncher.net/cosmetics/tvl-cosmetics.jar` | El dominio **no resuelve**; `147.15.17.142` tiene **80 y 443 cerrados** |

Notas:

- El SMP por defecto es configurable en Ajustes y se escribe en `servers.dat` de cada instancia.
- Los cosméticos degradan de forma controlada: si la descarga falla se registra el error y la app
  sigue funcionando (`CosmeticsService.Initialize` captura la excepción).
- **Pendiente de decisión:** o se publica el jar en un host con 80/443 y certificado válido, o se
  retira el CDN de la configuración por defecto. Servir por IP con HTTPS falla la validación de
  certificado salvo que el certificado incluya la IP como SAN.

---

## 6. Lo que queda pendiente

### Seguridad / privacidad

| Pendiente | Notas |
|---|---|
| **Cifrado de `accounts.json`** | **AES-GCM** nativo (`System.Security.Cryptography.AesGcm`) para Windows/Linux/macOS, clave local con permisos restrictivos (chmod 600 en Unix) y **migración automática** del JSON plano. Decisión tomada: multiplataforma con AES-GCM. |
| Refresh proactivo de token | Evitar reautenticación manual al caducar |

### Descargas y robustez

| Pendiente | Notas |
|---|---|
| Reintentos con backoff | Hoy un hash incorrecto aborta directamente |
| Reanudación con `Range` | El `.part` existe pero no se reutiliza |
| Verificación de archivos ya instalados | Hoy solo por tamaño (trade-off medido: ~12 s) |
| Cancelación real en UI | El núcleo acepta `CancellationToken`; falta el flujo de cancelar y limpiar `.part` |
| SHA1 del instalador de Forge | No se publica hash en el metadata |

### Testing / CI

| Pendiente | Notas |
|---|---|
| **Automatizar la matriz de arranque** | Hoy es manual (8 combinaciones); falta script reproducible que detecte "menú alcanzado" |
| Cubrir 1.16.1 y 1.17–1.19 | Mismas rutas que 1.16.5/1.20.1, pero no probadas |
| CI (build + test) | No existe pipeline |
| Tests de `MinecraftService` | La orquestación de proceso no está cubierta |

### Producto

| Pendiente | Notas |
|---|---|
| **Quilt real** | Solo etiqueta del modelo y filtro de Modrinth |
| **OptiFine** | Solo se respeta un jar ya instalado |
| **Modpacks `.mrpack` y dependencias de Modrinth** | Hoy descarga un mod suelto |
| Auto-descarga de JRE | Solo detección |
| Auto-actualización del launcher | No existe |
| Empaquetado y firma | Portable/MSIX pendiente |
| i18n y accesibilidad | Pendientes |
| `README.md` desactualizado | Sigue diciendo que Forge/Fabric/NeoForge se lanzan vía CmlLib |
| Log de Minecraft acumulativo | Rota a `.old.log` a los 5 MB; una sesión previa puede convivir |

---

## 7. Cómo reproducir el estado exacto

```powershell
git clone https://github.com/tvboy07/TVLauncher.git
cd TVLauncher
git checkout <commit>

dotnet build TVLauncher.csproj --configuration Debug       # 0 errores
dotnet test tests\TVLauncher.Tests\TVLauncher.Tests.csproj  # 51/51

# Datos de usuario (fuera del repo):
#   %LOCALAPPDATA%\TVLauncher\{config.json,accounts.json,instances\,assets\,libraries\,versions\}
#   %LOCALAPPDATA%\TVLauncher\logs\{tvlauncher.log,minecraft-console.log}
```

## 8. Lectura para el consultor (resumen de riesgos)

1. **Funciona de verdad**: 8 combinaciones de versión/modloader arrancan hasta el menú, incluidos Forge legacy (1.7.10) y NeoForge 1.21.1.
2. **El activo principal es el motor propio**: control total de classpath, nativos, argumentos y diagnóstico; permite arreglar crashes que un launcher de librería oculta.
3. **La deuda más cara es la operativa**: sin CI ni matriz automatizada, cada regresión se detecta jugando.
4. **Riesgo de seguridad contenido**: repo limpio; el problema está en el disco del usuario (`accounts.json` en claro) y ya tiene plan (AES-GCM + migración).
5. **Un endpoint del producto no existe** (CDN de cosméticos): decidir entre publicarlo o retirarlo antes de distribuir.
