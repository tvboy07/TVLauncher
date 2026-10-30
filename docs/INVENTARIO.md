# TVLauncher — Inventario técnico y matriz de características

> Documento de referencia para revisión externa. Refleja el estado real del repositorio
> en la rama `main` a 30/10/2026.
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
| Código fuente propio | **54 archivos / 8.740 líneas** (sin `bin`, `obj` ni tests) |
| Tests | **12 archivos / 1.107 líneas / 82 casos** (xUnit) |
| Dependencias NuGet | 20 (Avalonia, Silk.NET.OpenGL, SkiaSharp, Newtonsoft.Json, CmlLib.Core residual) |
| Commits | 11 previos + este documento |
| Build | **0 errores**, advertencias preexistentes (`MVVMTK0034`, `AVLN3001`, nullable) |
| Superficie de UI | 8 ventanas/vistas, 9 ViewModels, 9 servicios singleton |
| Verificación de arranque | 8 combinaciones de versión/modloader llevadas al menú principal |
| Cifrado en disco | **AES-256-GCM** para `accounts.json` con clave local y migración automática |

### Distribución del código

| Carpeta | Archivos | Líneas | Contenido |
|---|---|---|---|
| `Services/` | 13 | 4.107 | Núcleo de lanzamiento, auth, cuentas cifradas, instancias, cosméticos, Modrinth, skins |
| `ViewModels/` | 9 | 1.525 | Lógica de presentación MVVM |
| `Views/` | 16 (`8 .axaml` + `8 .cs`) | 1.520 | Ventanas y vistas Avalonia |
| `Controls/` | 3 | 640 | Visor 3D OpenGL, banner |
| `Helpers/` | 4 | 494 | Argumentos, reglas, enmascarado de secretos, modelo de skin |
| `Models/` | 5 | 284 | Config, cuentas, instancias, metadata de versión, Java |
| Raíz | 4 | 170 | `Program.cs`, `App.axaml(.cs)`, `ViewLocator.cs` |
| `tests/TVLauncher.Tests/` | 12 | 1.107 | Suite xUnit |

### Historial

```
56392c3 feat(auth): Client ID propio de TVLauncher y verificacion del loopback
431e10f docs: inventario tecnico completo y endpoint SMP por defecto
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
| Redirect loopback | ✅ | `TcpListener` en `http://localhost:{puerto}/` (prefiere 1337, cae a puerto efímero), navegador del sistema. Requiere `http://localhost` registrado en Azure; Entra ID ignora el puerto pero **compara la ruta** (`/`) |
| **Fallback a Device Code** | ✅ | `authorization_pending` / `slow_down` (incremento de intervalo) y expiración |
| Cadena Xbox → Minecraft | ✅ | XBL → XSTS → `login_with_xbox` → perfil |
| Client ID configurable | ✅ | `AppConfig.MicrosoftClientId` (vacío → app pública `38c4f9ed-…`, registrada como **TVLauncher**); sustituible sin recompilar |
| Cuentas offline | ✅ | Nombre de usuario y UUID determinista |
| Gestión de cuentas | ✅ | Alta, selección activa, borrado, persistencia en `accounts.json` |
| Persistencia de sesión | ✅ | `accounts.json` cifrado con **AES-256-GCM** (`System.Security.Cryptography.AesGcm`): sobre `{version, alg, nonce, tag, data}` en Base64, nonce aleatorio de 12 bytes por escritura y tag de 16 |
| Clave de cifrado | ✅ | 32 bytes aleatorios en `%LOCALAPPDATA%\TVLauncher\accounts.key`; `chmod 600` en Unix (`File.SetUnixFileMode`), ACL del perfil en Windows; escritura atómica |
| Migración de `accounts.json` | ✅ | Un array JSON plano se lee, se reescribe cifrado y se continúa sin intervención. Si falta la clave o no corresponde, falla de forma explícita y **no** sobrescribe las cuentas |
| Enmascarado de tokens en logs | ✅ | `Helpers/SecretMasker` — `access_token`, `refresh_token`, `id_token`, `Token`, `AuthorizationToken`, `user_hash`, `uhs` → `***`; enmascara **antes** de truncar |
| Renovación de token | ⚠️ | Se persiste `refresh_token`, pero no hay refresh proactivo: al caducar hay que reautenticar |

### Registro de la app en Azure (verificado el 30/10/2026)

La app **TVLauncher** (`38c4f9ed-0484-4a22-a192-9be8b172ab16`) responde en el endpoint
`/consumers/`, es decir, admite **cuentas personales**. Para que el flujo de Authorization Code
funcione, la app debe tener además:

| Requisito | Valor | Motivo |
|---|---|---|
| Redirect URI (plataforma **Mobile and desktop applications**) | `http://localhost` | El código envía `http://localhost:{puerto}/`; Entra ID ignora el puerto en loopback pero **no** la ruta |
| Allow public client flows | **Yes** | Device Code Flow (respaldo) y PKCE sin `client_secret` |

Si falta el redirect URI, Entra devuelve `invalid_request: The provided value for the input
parameter 'redirect_uri' is not valid` en la página del navegador, el listener nunca recibe el
callback y, tras 3 minutos de espera, se cae al Device Code. `http://127.0.0.1:puerto` **no**
es equivalente a `http://localhost:puerto` para Entra ID.

### Cifrado en disco de las cuentas

| Pieza | Implementación |
|---|---|
| Algoritmo | AES-256-GCM nativo (`AesGcm`), no DPAPI: mismo código en Windows, Linux y macOS |
| Sobre | `{"version":1,"alg":"AES-256-GCM","nonce":"…","tag":"…","data":"…"}` (Base64); la cabecera se autentica como datos asociados |
| Clave | 32 bytes de `RandomNumberGenerator` en `accounts.key`; `chmod 600` en Unix, ACL del perfil en Windows |
| Escritura | Temporal + rename atómico (`.part`), nunca deja el archivo a medias |
| Migración | Array JSON plano → se cifra en el primer arranque; idempotente (el archivo no se reescribe si ya es un sobre) |
| Fallo seguro | Sin clave o con clave incorrecta se lanza un error explícito y se bloquea el guardado, para no sobrescribir las cuentas |

Verificado sobre una copia de un `accounts.json` real: 1 cuenta migrada sin pérdida de campos,
sin rastro del token en el archivo y segunda carga sin reescribir.

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
| **Suite xUnit** | ✅ | `tests/TVLauncher.Tests`, **82 casos**, sin red externa ni archivos reales del juego (`TempDirectory` aislado) |
| Aislamiento de tests | ✅ | `InternalsVisibleTo`, `tests/**` excluido del glob de compilación, `EnsureProfileJar` parametrizado, `ConfigService` con directorio inyectable |
| Verificación por commit | ✅ | Cada commit compilado y testeado en worktree aislado (7 tests en `dc1ae0a`, 51 en `7133b01`, 82 con cifrado) |
| CI | ❌ | No hay pipeline |

### Composición de la suite (82 casos = 78 métodos, 2 `[Theory]` con 6 entradas en total)

| Archivo | Casos | Qué garantiza |
|---|---|---|
| `RuleEvaluatorTests` | 10 | allow/disallow, `os`, `arch`, `features`, feature ausente = falsa, case-insensitive |
| `SecretMaskerTests` | 6 | tokens Microsoft/XBL/XSTS/uhs, JSON compacto e **indentado**, enmascarar antes de truncar, nulos |
| `FileIntegrityTests` | 10 | SHA1 conocido (`"abc"`), mismo tamaño + hash distinto, mayúsculas, tamaño desconocido, ausente |
| `DownloadIntegrityTests` | 3 | Descarga real contra `TcpListener` local: hash correcto instala, incorrecto no publica, válido no re-descarga |
| `VersionMergeTests` | 10 | log4j 2.8.1 → 2.15.0 sin duplicar, librerías del hijo primero, variantes de arquitectura, `ClientSourceId`, `mainClass` |
| `LaunchArgumentsBuilderTests` | 7 | placeholders, `${version_name}` al perfil resuelto, reglas por features, legacy con `-cp`, `ToLogString` enmascarado |
| `ProfileJarTests` | 4 | copia atómica, respeta jar propio (OptiFine), no copia sin `${version_name}` |
| `AccountsCipherTests` | 14 | round-trip, sobre autocontenido, nonce distinto por sellado, sin fuga de texto plano, clave errónea, cifrado y tag manipulados, versión/algoritmo no soportados, sobres malformados, clave de tamaño incorrecto |
| `AccountsStoreTests` | 12 | guardar/cargar, sobre en disco sin tokens, migración desde JSON plano, idempotencia (no reescribe), clave ausente o corrupta sin tocar los datos, archivo legacy corrupto, sin temporales, `chmod 600` en Unix |
| `ConfigMigrationTests` | 5 | `play.tvlauncher.net` → IP real (también en mayúsculas), respeta hosts personalizados, no reescribe si no hay nada que migrar, instalación nueva con la IP real |

### Correcciones reales cerradas

1. **`ResolutionException` en Forge 1.20.1**: classpath con `versions/{mc}/{mc}.jar` mientras el perfil esperaba `${version_name}.jar` en `-DignoreList` → módulo automático `_1._20._1` en conflicto con `minecraft`.
2. **`NoSuchMethodError: ThrowableProxy.formatExtendedStackTraceTo` en 1.16.5**: dos `log4j-core` en el classpath (2.8.1 padre + 2.15.0 perfil) → dedupe por `group:artifact[:classifier]`.
3. **Tokens en logs**: una sesión real dejó 4 entradas con `access_token`/`refresh_token` en `tvlauncher.log`; hoy se enmascaran y el log local fue limpiado retroactivamente.

### Auditoría de seguridad

- **0** secretos, API keys, contraseñas o claves privadas en los archivos trackeados **ni en todo el historial** (`git log --all -p`).
- Único identificador: `DefaultClientId` (público por diseño, app pública con PKCE). **Ningún `client_secret`**.
- Sin binarios versionados, sin `appsettings*.json`, sin credenciales en la URL del remoto.
- `accounts.json` cifrado con AES-256-GCM: una copia suelta del archivo no expone tokens. La clave vive en el mismo directorio, así que esto protege ante lectores del archivo o backups, **no** ante código que se ejecute como el mismo usuario.

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
dotnet test tests\TVLauncher.Tests\TVLauncher.Tests.csproj  # 82/82

# Datos de usuario (fuera del repo):
#   %LOCALAPPDATA%\TVLauncher\{config.json,accounts.json,accounts.key,instances\,assets\,libraries\,versions\}
#   %LOCALAPPDATA%\TVLauncher\logs\{tvlauncher.log,minecraft-console.log}
```

## 8. Lectura para el consultor (resumen de riesgos)

1. **Funciona de verdad**: 8 combinaciones de versión/modloader arrancan hasta el menú, incluidos Forge legacy (1.7.10) y NeoForge 1.21.1.
2. **El activo principal es el motor propio**: control total de classpath, nativos, argumentos y diagnóstico; permite arreglar crashes que un launcher de librería oculta.
3. **La deuda más cara es la operativa**: sin CI ni matriz automatizada, cada regresión se detecta jugando.
4. **Riesgo de seguridad contenido**: repo limpio y `accounts.json` cifrado con AES-256-GCM (clave local, migración automática). La clave vive en el mismo directorio: protege el archivo, no a un atacante que ya ejecute código como el usuario.
5. **Un endpoint del producto no existe** (CDN de cosméticos): decidir entre publicarlo o retirarlo antes de distribuir.
