# TVLauncher â€” Inventario tÃ©cnico y matriz de caracterÃ­sticas

> Documento de referencia para revisiÃ³n externa. Refleja el estado real del repositorio
> en la rama `main` a 03/11/2026.
> Mantener actualizado junto con `docs/ROADMAP.md`.

- **VersiÃ³n:** 1.0.0
- **Rama:** `main`
- **Remoto:** `github.com/tvboy07/TVLauncher`
- **Estado del Ã¡rbol:** limpio

---

## 0. Snapshot ejecutivo

| MÃ©trica | Valor |
|---|---|
| Stack | C# / .NET 8.0 Â· Avalonia UI 11.1.3 Â· MVVM (CommunityToolkit) Â· DI (Microsoft.Extensions) |
| CÃ³digo fuente propio | **55 archivos / 9.443 lÃ­neas** (sin `bin`, `obj` ni tests) |
| Tests .NET | **25 archivos / 2.775 líneas / 203 casos** (xUnit) |
| Tests del mod | **35 casos** (JUnit/Gradle) |
| Dependencias NuGet | 20 (Avalonia, Silk.NET.OpenGL, SkiaSharp, Newtonsoft.Json, CmlLib.Core residual) |
| Commits | 15 previos + este documento |
| Build | **0 errores**, advertencias preexistentes (`MVVMTK0034`, `AVLN3001`, nullable) |
| Superficie de UI | 5 ventanas + 3 vistas embebidas, 10 ViewModels, 9 servicios singleton |
| VerificaciÃ³n de arranque | 8 combinaciones de versiÃ³n/modloader llevadas al menÃº principal |
| Cifrado en disco | **AES-256-GCM** para `accounts.json` con clave local y migraciÃ³n automÃ¡tica |

### DistribuciÃ³n del cÃ³digo

| Carpeta | Archivos | LÃ­neas | Contenido |
|---|---|---|---|
| `Services/` | 13 | 4.315 | NÃºcleo de lanzamiento, auth, cuentas cifradas, instancias, cosmÃ©ticos, Modrinth, skins |
| `ViewModels/` | 10 | 1.754 | LÃ³gica de presentaciÃ³n MVVM, incluido `AppPage` |
| `Views/` | 16 (`8 .axaml` + `8 .cs`) | 1.415 | Shell, ventanas modales y pÃ¡ginas embebidas |
| `Controls/` | 3 | 641 | Visor 3D OpenGL, banner |
| `Helpers/` | 4 | 494 | Argumentos, reglas, enmascarado de secretos, modelo de skin |
| `Models/` | 5 | 284 | Config, cuentas, instancias, metadata de versiÃ³n, Java |
| RaÃ­z | 4 | 540 | `Program.cs`, `App.axaml(.cs)` con el design system, `ViewLocator.cs` |
| `tests/TVLauncher.Tests/` | 25 | 2.775 | Suite xUnit |

### Historial

```
este commit  feat(ui): shell con sidebar, dashboard de instancias y design system oscuro
422b824 fix(auth): scopes OIDC completos, PKCE publico y client ID aprobado
097e72e feat(modrinth): cancelar descarga y verificar SHA-512 del jar
673595d fix(ui,modrinth): scroll en ventanas de instancia y descarga en streaming
3992653 feat(accounts): cifrado AES-256-GCM de accounts.json y migraciones
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

**DecisiÃ³n de arquitectura clave:** el launcher **no delega el arranque en CmlLib**. Se escribiÃ³
un motor propio de resoluciÃ³n de archivos y construcciÃ³n de comandos. `CmlLib.Core.Auth`
sobrevive solo como tipo de sesiÃ³n heredado (`MSession` en `AuthService`), no como motor.

| Capacidad | Estado | ImplementaciÃ³n |
|---|---|---|
| Manifiesto oficial v2 (piston-meta) | âœ… | `GameFilesService.GetManifestAsync` â€” cachÃ© de **6 h**, guarda anti-cachÃ©-recortada (`MinimumManifestVersions = 500`) |
| ResoluciÃ³n de `inheritsFrom` | âœ… | `LoadVersionTreeAsync` â€” padre + hijo fusionados recursivamente |
| Reglas de librerÃ­as (`os`, `arch`, `features`) | âœ… | `Helpers/RuleEvaluator` â€” allow/disallow acumulativo, case-insensitive, feature ausente = `false` |
| Descarga de librerÃ­as | âœ… | `EnsureLibraryAsync` â€” artefacto principal + clasificadores nativos; URL desde `downloads.artifact`, `library.url` o Maven de Mojang |
| Nativos (extracciÃ³n) | âœ… | `LaunchArgumentsBuilder.ExtractNatives` â€” formato legacy (`natives` + clasificador) y moderno (`:natives-windows`), respeta `extract.exclude`, omite `META-INF/` |
| Classpath | âœ… | `BuildClasspath` â€” jar del perfil primero, deduplicado case-insensitive, avisa de librerÃ­as ausentes |
| **Jar del perfil (ModLauncher)** | âœ… | `EnsureProfileJar` â€” copia atÃ³mica (`.part` â†’ rename) del `client.jar` a `versions/{perfil}/{perfil}.jar` cuando los args JVM contienen `${version_name}`; nunca sobrescribe un jar propio (OptiFine) |
| **Dedupe de librerÃ­as (log4j)** | âœ… | `MergeVersions` + `LibraryKey` â€” clave `group:artifact[:classifier]`, gana el perfil hijo y sus librerÃ­as van primero; conserva variantes de arquitectura del mismo padre |
| Forge (moderno) | âœ… | Ejecuta el instalador oficial con `--installClient` y lee el perfil embebido |
| Forge (legacy 1.7.10â€“1.12.2) | âœ… | `TryLegacyInstallAsync` â€” extrae el `universal.jar` del instalador y compone el perfil |
| NeoForge | âœ… | Mismo pipeline que Forge, con fuente de metadata propia (`maven.neoforged.net`) y versionado con sufijo |
| Fabric | âœ… | `InstallFabricAsync` â€” perfil desde `meta.fabricmc.net`, resoluciÃ³n del loader mÃ¡s reciente |
| Assets | âœ… | Ãndice + objetos content-addressed, descarga paralela con **semÃ¡foro de 24** |
| SelecciÃ³n de Java | âœ… | `JavaService` â€” detecta `JAVA_HOME`, `PATH`, runtime de Mojang, Oracle/Adoptium/Microsoft/Zulu/Corretto; valida versiÃ³n y elige por `javaVersion.majorVersion` |
| Argumentos JVM/juego | âœ… | `LaunchArgumentsBuilder` â€” placeholders oficiales, `MinecraftArguments` legacy, argumentos condicionales por reglas, `-Dlog4j2.formatMsgNoLookups=true`, `-Djava.library.path` solo si el metadata no lo trae |
| `-DignoreList` / `${version_name}` | âœ… | Resuelven al **id del perfil resuelto**, no al de la instancia (fix del `ResolutionException` `_1._20._1` vs mÃ³dulo `minecraft`) |
| DiagnÃ³stico de crash | âœ… | Cabecera por lanzamiento en `minecraft-console.log` (versiÃ³n, Java, gameDir, nativos, nÂº de entradas de classpath, comando con token enmascarado), prefijo `[stderr]`, rotaciÃ³n a `.old.log` a los 5 MB, volcado de `crash-reports/*` y `hs_err_pid*` (mÃ¡x. 256 KB por archivo), `WaitForExit` sin timeout para vaciar stdout/stderr |
| Cierre por el usuario vs crash | âœ… | Flag `_stopRequested` â†’ se registra como INFO, no como crash |
| Progreso de descarga | âœ… | Eventos `StatusChanged`, `ProgressChanged`, `DownloadProgressChanged` |

### Matriz de arranque verificada empÃ­ricamente

| VersiÃ³n / loader | Java | Resultado |
|---|---|---|
| Forge 1.20.1 (`1.20.1-forge-47.4.28`) | 17 | MenÃº ~20 s |
| Forge 1.20.1 legacy (`1.20.1-forge-47.1.106`) | 17 | MenÃº ~24 s |
| Forge 1.16.5 (`1.16.5-forge-36.2.42`) | 8 | MenÃº ~18 s |
| Forge 1.12.2 (`14.23.5.2864`) | 8 | MenÃº ~10 s |
| Forge 1.8.9 (`11.15.1.2318`) | 8 | MenÃº ~8 s |
| Forge 1.7.10 (`10.13.4.1614-1.7.10`) | 8 | MenÃº ~11 s |
| NeoForge 1.21.1 (`neoforge-21.1.265`) | 21 | InstalaciÃ³n + menÃº ~19 s |
| Fabric 1.21.11 (`fabric-loader-0.20.1-1.21.11`) | 21 | MenÃº ~22 s |

**Criterio de aceptaciÃ³n:** la instancia llega al menÃº principal y, si falla, ambos logs
contienen la causa (`[stderr]` y/o `crash-reports`).

---

## 2. AutenticaciÃ³n y cuentas

| Capacidad | Estado | Detalle |
|---|---|---|
| OAuth 2.0 Microsoft (v2, `consumers`) | âœ… | `MicrosoftAuthService`, `login.microsoftonline.com/consumers/oauth2/v2.0/*` |
| **Scopes** | âœ… | `openid profile offline_access XboxLive.signin` en authorize, token, device code y device token (`MicrosoftAuthService.Scope`) |
| **Authorization Code + PKCE** | âœ… | `code_verifier` / `code_challenge` S256; **sin `client_secret`** (app pÃºblica) |
| Redirect loopback | âœ… | `TcpListener` en `http://localhost:{puerto}/` (prefiere 1337, cae a puerto efÃ­mero), navegador del sistema. Requiere `http://localhost` registrado en Azure; Entra ID ignora el puerto pero **compara la ruta** (`/`) |
| **Fallback a Device Code** | âœ… | `authorization_pending` / `slow_down` (incremento de intervalo) y expiraciÃ³n |
| Cadena Xbox â†’ Minecraft | âœ… | XBL â†’ XSTS â†’ `login_with_xbox` â†’ perfil |
| Client ID configurable | âœ… | `AppConfig.MicrosoftClientId` (vacÃ­o â†’ `c36a9fb6-â€¦`, client pÃºblico ya aprobado para la API de Minecraft); la app propia `38c4f9ed-â€¦` se activa poniendo su GUID cuando Microsoft la apruebe |
| Cuentas offline | âœ… | Nombre de usuario y UUID determinista |
| GestiÃ³n de cuentas | âœ… | Alta, selecciÃ³n activa, borrado, persistencia en `accounts.json` |
| Persistencia de sesiÃ³n | âœ… | `accounts.json` cifrado con **AES-256-GCM** (`System.Security.Cryptography.AesGcm`): sobre `{version, alg, nonce, tag, data}` en Base64, nonce aleatorio de 12 bytes por escritura y tag de 16 |
| Clave de cifrado | âœ… | 32 bytes aleatorios en `%LOCALAPPDATA%\TVLauncher\accounts.key`; `chmod 600` en Unix (`File.SetUnixFileMode`), ACL del perfil en Windows; escritura atÃ³mica |
| MigraciÃ³n de `accounts.json` | âœ… | Un array JSON plano se lee, se reescribe cifrado y se continÃºa sin intervenciÃ³n. Si falta la clave o no corresponde, falla de forma explÃ­cita y **no** sobrescribe las cuentas |
| Enmascarado de tokens en logs | âœ… | `Helpers/SecretMasker` â€” `access_token`, `refresh_token`, `id_token`, `Token`, `AuthorizationToken`, `user_hash`, `uhs` â†’ `***`; enmascara **antes** de truncar |
| RenovaciÃ³n de token | âš ï¸ | Se persiste `refresh_token`, pero no hay refresh proactivo: al caducar hay que reautenticar |

### Registro de la app en Azure (verificado el 30/10/2026)

La app **TVLauncher** (`38c4f9ed-0484-4a22-a192-9be8b172ab16`) responde en el endpoint
`/consumers/`, es decir, admite **cuentas personales**. Para que el flujo de Authorization Code
funcione, la app debe tener ademÃ¡s:

| Requisito | Valor | Motivo |
|---|---|---|
| Redirect URI (plataforma **Mobile and desktop applications**) | `http://localhost` | El cÃ³digo envÃ­a `http://localhost:{puerto}/`; Entra ID ignora el puerto en loopback pero **no** la ruta |
| Allow public client flows | **Yes** | Device Code Flow (respaldo) y PKCE sin `client_secret` |
| **AprobaciÃ³n de la API de Minecraft** | Formulario `https://aka.ms/mce-reviewappid` | Sin ella, `login_with_xbox` devuelve **403 `Invalid app registration`** aunque OAuth, XBL y XSTS hayan ido bien |

Si falta el redirect URI, Entra devuelve `invalid_request: The provided value for the input
parameter 'redirect_uri' is not valid` en la pÃ¡gina del navegador, el listener nunca recibe el
callback y, tras 3 minutos de espera, se cae al Device Code. `http://127.0.0.1:puerto` **no**
es equivalente a `http://localhost:puerto` para Entra ID.

#### 403 `Invalid app registration` (diagnosticado el 30/10/2026)

Los logs de dos ejecuciones reales separan con claridad la capa que falla:

| EjecuciÃ³n | Client ID | Token MS | XBL | XSTS | `login_with_xbox` |
|---|---|---|---|---|---|
| 23:33 | `c36a9fb6-â€¦` | 200 | 200 | 200 | **200** âœ… |
| 02:17 | `38c4f9ed-â€¦` (app nueva) | 200 | 200 | 200 | **403** âŒ |

Es decir: los scopes y PKCE son correctos (el token de Microsoft se emite y Xbox lo canjea).
Mojang rechaza la **app**, no el token. Hasta que Microsoft apruebe la app nueva en
`aka.ms/mce-reviewappid`, el launcher usa por defecto el client pÃºblico `c36a9fb6-â€¦`; la app
propia se activa con `MicrosoftClientId` en `config.json` cuando estÃ© aprobada. El error en la UI
incluye ahora esa explicaciÃ³n y el enlace, en lugar del JSON crudo.

### Cifrado en disco de las cuentas

| Pieza | ImplementaciÃ³n |
|---|---|
| Algoritmo | AES-256-GCM nativo (`AesGcm`), no DPAPI: mismo cÃ³digo en Windows, Linux y macOS |
| Sobre | `{"version":1,"alg":"AES-256-GCM","nonce":"â€¦","tag":"â€¦","data":"â€¦"}` (Base64); la cabecera se autentica como datos asociados |
| Clave | 32 bytes de `RandomNumberGenerator` en `accounts.key`; `chmod 600` en Unix, ACL del perfil en Windows |
| Escritura | Temporal + rename atÃ³mico (`.part`), nunca deja el archivo a medias |
| MigraciÃ³n | Array JSON plano â†’ se cifra en el primer arranque; idempotente (el archivo no se reescribe si ya es un sobre) |
| Fallo seguro | Sin clave o con clave incorrecta se lanza un error explÃ­cito y se bloquea el guardado, para no sobrescribir las cuentas |

Verificado sobre una copia de un `accounts.json` real: 1 cuenta migrada sin pÃ©rdida de campos,
sin rastro del token en el archivo y segunda carga sin reescribir.

---

## 3. Interfaz y mÃ³dulos de UI (Avalonia)

`Program.cs` â†’ `App.axaml.cs` (`ServiceCollection` + `ConfigureServices`) â†’ `MainWindow` con
`MainWindowViewModel`. Servicios como singleton; ViewModels y ventanas como transient.

El **design system** vive en `App.axaml`: paleta oscura (fondo `#0F1115`, superficie `#1A1D24`,
borde `#2A2E3B`, acento `#6C5CE7` con hover `#7D6FF0` y pressed `#5B4DFF`) y estilos globales
para `Window`, `TextBlock` (`.h1/.h2/.caption/.label`), `Button` (`.primary/.ghost/.nav/.nav.active/.profile`),
`Border.card/.pill`, `TextBox`, `ComboBox`, `CheckBox`, `NumericUpDown`, `ListBox.cards`,
`ScrollViewer`/`ScrollBar`, `ProgressBar`, `TabItem` y `ToolTip`, con radios de 10â€“12 px y estados
hover/pressed. `app.manifest` declara DPI awareness `PerMonitorV2` para que Windows no escale la
ventana como mapa de bits.

| MÃ³dulo | Vista / VM | Estado | Detalle |
|---|---|---|---|
| **Shell / ventana principal** | `MainWindow` / `MainWindowViewModel` | âœ… | Sidebar fija (Instancias, Tienda Modrinth, CosmÃ©ticos, Ajustes), header con avatar real desde `mc-heads.net/avatar/{user}` (fallback a iniciales), tarjeta de perfil con gamertag y estado, dashboard de instancias en tarjetas con selecciÃ³n clara, panel de detalle, progreso y barra inferior con **JUGAR** y estado del juego. Redimensionable 1200Ã—720 (mÃ­nimo 1024Ã—640) con chrome nativo |
| NavegaciÃ³n interna | `AppPage` + comandos `Show*` | âœ… | Las cuatro secciones cambian de pÃ¡gina dentro del shell sin abrir ventanas; `SettingsView`/`ModrinthView` se envuelven en un `Grid` con `IsVisible` para no alterar el `x:DataType` de los bindings compilados |
| **Visor 3D de skins** | `Controls/SkinPreviewGLControl` (603 lÃ­neas) / `SkinsPreviewViewModel` | âœ… | OpenGL vÃ­a Silk.NET: cuerpo completo, **capa exterior/hat**, transparencias, geometrÃ­a cerrada sin huecos, encuadre centrado, rotaciÃ³n con puntero, auto-rotaciÃ³n, animaciÃ³n de caminar, classic/slim. Solo se retocÃ³ la paleta del contenedor |
| Skins y capas | `SkinService` | âœ… | `sessionserver.mojang.com` con fallback a Crafatar; cachÃ© de skin/capa y **cachÃ© de UUIDs en memoria** (24 h positivos, 5 min negativos) para no repetir llamadas a Mojang |
| **Armario / cosmÃ©ticos** | `Views/CosmeticsView` / `SkinsPreviewViewModel` + `CosmeticsService` | âœ… | Buscador global por usuario, preview 3D, **Mis Skins** (biblioteca local con equipar/quitar), capa y sombrero exclusivos TVLauncher generados con SkiaSharp, y persistencia de skin/capa/sombrero en `config.json`. El shell recarga el estado equipado y el avatar al volver. El mod Fabric (`tvl-cosmetics.jar`) va **embebido en el `.exe`** y se inyecta en la instancia; el backend global es Supabase (`cosmetics_profiles` + Edge Function `publish-cosmetics`). Ver `docs/SUPABASE_SETUP.md` |
| Gestor de instancias | `CreateInstanceWindow`, `InstanceEditWindow` | âœ… | Crear/editar/borrar; versiÃ³n, modloader + versiÃ³n de loader, RAM mÃ­n/mÃ¡x, resoluciÃ³n, `JVMArgs`, Java, directorio. Modales restilizados; el formulario va dentro de un `ScrollViewer` con los botones anclados, asÃ­ que se puede guardar en pantallas ajustadas |
| Wizard de primera ejecuciÃ³n | `SetupWizardWindow` / `SetupWizardViewModel` | âœ… | Gateado por `AppConfig.FirstRun`, `CompleteFirstRun()` |
| Cuentas | `AccountManagerWindow` / `AccountManagerViewModel` | âœ… | AÃ±adir/cambiar/eliminar cuentas Microsoft; modal restilizado |
| Ajustes | `Views/SettingsView` / `SettingsViewModel` | âœ… | PÃ¡gina embebida; Java, RAM, tema, Modrinth, cosmÃ©ticos, SMP por defecto, cerrar al lanzar / mantener abierto. `CloseRequested` devuelve el control al shell |
| **Modrinth** | `Views/ModrinthView` / `ModrinthBrowserViewModel` + `ModrinthService` | âš ï¸ | PÃ¡gina embebida; bÃºsqueda (query, versiÃ³n, loader) con el **catÃ¡logo real de versiones** de `/v2/tag/game_version`; detalle, versiones y descarga del archivo primario a `{instance}/mods` por **streaming con barra de progreso**, `.part` atÃ³mico, **cancelaciÃ³n desde la UI** y **verificaciÃ³n SHA-512** contra `files[].hashes`. **No resuelve dependencias** ni instala `.mrpack` |
| Servidor SMP por defecto | `InstanceManager.GenerateServersDat` | âš ï¸ | Escribe `servers.dat`; por defecto apunta a `147.15.17.142:25565` (verificado: puerto abierto) |
| Banner | `Controls/AdsBanner` | â„¹ï¸ | Banner superior reestilizado y ocultable desde la vista principal |
| i18n | â€” | âŒ | Texto hardcodeado en espaÃ±ol; `Language` existe en config pero no se usa |
| Accesibilidad / teclado | âš ï¸ | `AutomationProperties.Name` en navegaciÃ³n, tarjetas y avatares; navegaciÃ³n completa por teclado pendiente |

---

## 4. Integridad y testing

| Capacidad | Estado | Detalle |
|---|---|---|
| **SHA1 en descargas** | âœ… | `EnsureFileAsync(url, path, size, sha1, ct, fatal)` valida el `.part` **antes** de renombrarlo; si no coincide, borra el temporal y falla. Cubre `client.jar`, librerÃ­as, nativas, asset index, log config, metadata de versiÃ³n y assets |
| PolÃ­tica de reutilizaciÃ³n | âš ï¸ | Los archivos presentes se reutilizan **por tamaÃ±o** (revalidar todo cuesta ~12 s: 1.214 MB de assets + 536 MB de librerÃ­as + 187 MB de versiones) |
| Rechazo de contenido corrupto | âœ… | Probado contra servidor HTTP local real: hash incorrecto â†’ no se publica ni queda `.part` |
| **Suite xUnit** | âœ… | `tests/TVLauncher.Tests`, **203 casos**, sin red externa ni archivos reales del juego (`TempDirectory` aislado) |
| Aislamiento de tests | âœ… | `InternalsVisibleTo`, `tests/**` excluido del glob de compilaciÃ³n, `EnsureProfileJar` parametrizado, `ConfigService` con directorio inyectable, `ModrinthBrowserViewModel` con ctor interno sin carga remota |
| VerificaciÃ³n por commit | âœ… | Cada commit compilado y testeado en worktree aislado (7 tests en `dc1ae0a`, 51 en `7133b01`, 82 con cifrado, 95 tras los arreglos de UI y Modrinth, 104 con cancelaciÃ³n y SHA-512, 114 con scopes y PKCE, 169 con el rediseÃ±o de UI) |
| CI | âŒ | No hay pipeline |
### Composición de la suite (203 casos)
### ComposiciÃ³n de la suite (169 casos = 126 mÃ©todos, 9 `[Theory]` con 52 entradas en total)

| Archivo | Casos | QuÃ© garantiza |
|---|---|---|
| `RuleEvaluatorTests` | 10 | allow/disallow, `os`, `arch`, `features`, feature ausente = falsa, case-insensitive |
| `SecretMaskerTests` | 7 | tokens Microsoft/XBL/XSTS/uhs, JSON compacto e **indentado**, enmascarar antes de truncar, nulos |
| `FileIntegrityTests` | 10 | SHA1 conocido (`"abc"`), mismo tamaÃ±o + hash distinto, mayÃºsculas, tamaÃ±o desconocido, ausente |
| `DownloadIntegrityTests` | 3 | Descarga real contra `TcpListener` local: hash correcto instala, incorrecto no publica, vÃ¡lido no re-descarga |
| `VersionMergeTests` | 10 | log4j 2.8.1 â†’ 2.15.0 sin duplicar, librerÃ­as del hijo primero, variantes de arquitectura, `ClientSourceId`, `mainClass` |
| `LaunchArgumentsBuilderTests` | 7 | placeholders, `${version_name}` al perfil resuelto, reglas por features, legacy con `-cp`, `ToLogString` enmascarado |
| `ProfileJarTests` | 4 | copia atÃ³mica, respeta jar propio (OptiFine), no copia sin `${version_name}` |
| `AccountsCipherTests` | 14 | round-trip, sobre autocontenido, nonce distinto por sellado, sin fuga de texto plano, clave errÃ³nea, cifrado y tag manipulados, versiÃ³n/algoritmo no soportados, sobres malformados, clave de tamaÃ±o incorrecto |
| `AccountsStoreTests` | 12 | guardar/cargar, sobre en disco sin tokens, migraciÃ³n desde JSON plano, idempotencia (no reescribe), clave ausente o corrupta sin tocar los datos, archivo legacy corrupto, sin temporales, `chmod 600` en Unix |
| `ConfigMigrationTests` | 5 | `play.tvlauncher.net` â†’ IP real (tambiÃ©n en mayÃºsculas), respeta hosts personalizados, no reescribe si no hay nada que migrar, instalaciÃ³n nueva con la IP real |
| `ModrinthDownloadTests` | 10 | Streaming a disco contra servidor HTTP local: archivo Ã­ntegro en varios bloques, progreso monÃ³tono que termina en 100, error HTTP sin `.part`, cuerpo truncado que no se publica, sobrescritura atÃ³mica; SHA-512 correcto, incorrecto (rechazo + limpieza), en mayÃºsculas, ausente, y parseo de `hashes`/`size` |
| `ModrinthBrowserViewModelTests` | 4 | Circuito del ViewModel: el jar acaba en `{instancia}/mods`, sin hash tambiÃ©n descarga, hash errÃ³neo deja mensaje de error y ningÃºn archivo, y `CancelDownloadCommand` detiene la transferencia sin dejar `.part` |
| `GameVersionCatalogTests` | 5 | Parseo de `/v2/tag/game_version`: solo releases, orden descendente, sin duplicados, incluye versiones antiguas (1.8.9), JSON vacÃ­o o invÃ¡lido |
| `WindowLayoutTests` | 3 | Las ventanas de instancia tienen `ScrollViewer` y el botÃ³n de acciÃ³n queda fuera del Ã¡rea desplazable |
| `MicrosoftAuthRequestTests` | 10 | Scopes `openid profile offline_access XboxLive.signin` sin duplicados y URL-encoded, authorize con PKCE S256 y sin `client_secret`, challenge = Base64URL(SHA-256), formularios de token/device code, y explicaciÃ³n accionable del 403 `Invalid app registration` |
| `UiThemeTests` | 25 | `App.axaml` define la paleta oscura y los estilos globales: `Button.primary/ghost/nav`, `Border.card/pill`, `TextBox`, `ComboBox`, `ListBox.cards`, `ScrollViewer`, `ProgressBar`, radios 10/12 y estados hover/pressed |
| `MainWindowShellTests` | 18 | Shell: sidebar con Instancias/Modrinth/CosmÃ©ticos/Ajustes, navegaciÃ³n por `AppPage`, barra inferior con **JUGAR**, header de perfil y avatar con fallback a iniciales (`BuildInitials`) |
| `SecondaryViewsThemeTests` | 12 | `SettingsView`, `ModrinthView`, `CosmeticsView`, `InstanceEditWindow` y `AccountManagerWindow` usan la paleta y los estilos del design system, sin colores legacy |

### Correcciones reales cerradas

1. **`ResolutionException` en Forge 1.20.1**: classpath con `versions/{mc}/{mc}.jar` mientras el perfil esperaba `${version_name}.jar` en `-DignoreList` â†’ mÃ³dulo automÃ¡tico `_1._20._1` en conflicto con `minecraft`.
2. **`NoSuchMethodError: ThrowableProxy.formatExtendedStackTraceTo` en 1.16.5**: dos `log4j-core` en el classpath (2.8.1 padre + 2.15.0 perfil) â†’ dedupe por `group:artifact[:classifier]`.
3. **Tokens en logs**: una sesiÃ³n real dejÃ³ 4 entradas con `access_token`/`refresh_token` en `tvlauncher.log`; hoy se enmascaran y el log local fue limpiado retroactivamente.

### AuditorÃ­a de seguridad

- **0** secretos, API keys, contraseÃ±as o claves privadas en los archivos trackeados **ni en todo el historial** (`git log --all -p`).
- Ãšnico identificador: `DefaultClientId` (pÃºblico por diseÃ±o, app pÃºblica con PKCE). **NingÃºn `client_secret`**.
- Sin binarios versionados, sin `appsettings*.json`, sin credenciales en la URL del remoto.
- `accounts.json` cifrado con AES-256-GCM: una copia suelta del archivo no expone tokens. La clave vive en el mismo directorio, asÃ­ que esto protege ante lectores del archivo o backups, **no** ante cÃ³digo que se ejecute como el mismo usuario.

---

## 5. Endpoints por defecto

| Endpoint | Valor actual | VerificaciÃ³n |
|---|---|---|
| Servidor SMP por defecto | `147.15.17.142:25565` | TCP **abierto** (comprobado) |
| Mod in-game | `Resources/tvl-cosmetics.jar` embebido en el `.exe` | Se extrae a `%LOCALAPPDATA%\TVLauncher\cosmetics` y se copia a `{instance}/mods`; no se descarga nada |
| Backend de cosmÃ©ticos | Supabase `cosmetics_profiles` + Edge Function `publish-cosmetics` | URL y anon key se configuran en Ajustes â†’ SincronizaciÃ³n in-game; guÃ­a en `docs/SUPABASE_SETUP.md` |

Notas:

- El SMP por defecto es configurable en Ajustes y se escribe en `servers.dat` de cada instancia.
- El CDN `cdn.tvlauncher.net` queda **retirado**: ni el launcher ni el mod descargan el jar.
- La `service_role` de Supabase nunca se incluye en el launcher, en el mod ni en el repositorio;
  la Edge Function la usa solo en el servidor.
- Los cosmÃ©ticos degradan de forma controlada: sin Supabase el juego funciona y el jugador local
  sigue viendo lo que tenga equipado.

### CosmÃ©ticos in-game (beta)

- El launcher escribe `config/tvl-cosmetics.json` en cada instancia con `schema_version`, `enabled`,
  `supabase_url`, `supabase_anon_key`, `local_cape_id`, `local_hat_id` y `poll_seconds`.
- Al equipar, desequipar o lanzar, el launcher publica los IDs en Supabase con un debounce de 500 ms
  (el lanzamiento publica sin esperar). Un fallo de red no bloquea el juego.
- Las cuentas Microsoft se publican y se ven desde cualquier servidor. Las cuentas **offline no
  publican**: solo renderizan sus cosmÃ©ticos en local.
- El sombrero puede solaparse con un casco colocado en esta beta.
- Release candidate local: `publish/win-x64/TVLauncher.exe`, **47.519.006 bytes**,
  SHA-256 `AC7B26FDDBD53AA102F4B8612B53D7BD1927808C2F461771124A1445738FF1BE`.
- Paquete beta: `artifacts/TVLauncher-beta-win-x64.zip`, **54.614.207 bytes**,
  SHA-256 `E73630917589C1FFDE9EAFEB0B6F157046A4065C2FB99E43ACF07E034A47BCEC`.
  Incluye `TVLauncher.exe`, `beta-dotnet/` + `Iniciar-TVLauncher.cmd` (workaround de
  Smart App Control) y `LEEME-BETA.md`.

---

## 6. Lo que queda pendiente

### Seguridad / privacidad

| Pendiente | Notas |
|---|---|
| Refresh proactivo de token | Evitar reautenticaciÃ³n manual al caducar |

### Descargas y robustez

| Pendiente | Notas |
|---|---|
| Reintentos con backoff | Hoy un hash incorrecto aborta directamente |
| ReanudaciÃ³n con `Range` | El `.part` existe pero no se reutiliza |
| VerificaciÃ³n de archivos ya instalados | Hoy solo por tamaÃ±o (trade-off medido: ~12 s) |
| SHA1 del instalador de Forge | No se publica hash en el metadata |

### Testing / CI

| Pendiente | Notas |
|---|---|
| **Automatizar la matriz de arranque** | Hoy es manual (8 combinaciones); falta script reproducible que detecte "menÃº alcanzado" |
| Cubrir 1.16.1 y 1.17â€“1.19 | Mismas rutas que 1.16.5/1.20.1, pero no probadas |
| CI (build + test) | No existe pipeline |
| Tests de `MinecraftService` | La orquestaciÃ³n de proceso no estÃ¡ cubierta |

### Producto

| Pendiente | Notas |
|---|---|
| **Quilt real** | Solo etiqueta del modelo y filtro de Modrinth |
| **OptiFine** | Solo se respeta un jar ya instalado |
| **Modpacks `.mrpack` y dependencias de Modrinth** | Hoy descarga un mod suelto |
| Auto-descarga de JRE | Solo detecciÃ³n |
| Auto-actualizaciÃ³n del launcher | No existe |
| Empaquetado y firma | Portable/MSIX pendiente |
| i18n y accesibilidad | Pendientes |
| `README.md` desactualizado | Sigue diciendo que Forge/Fabric/NeoForge se lanzan vÃ­a CmlLib |
| Log de Minecraft acumulativo | Rota a `.old.log` a los 5 MB; una sesiÃ³n previa puede convivir |

---

## 7. CÃ³mo reproducir el estado exacto

```powershell
git clone https://github.com/tvboy07/TVLauncher.git
cd TVLauncher
git checkout <commit>

dotnet test tests\TVLauncher.Tests\TVLauncher.Tests.csproj  # 203/203
dotnet test tests\TVLauncher.Tests\TVLauncher.Tests.csproj  # 169/169

# Datos de usuario (fuera del repo):
#   %LOCALAPPDATA%\TVLauncher\{config.json,accounts.json,accounts.key,instances\,assets\,libraries\,versions\}
#   %LOCALAPPDATA%\TVLauncher\logs\{tvlauncher.log,minecraft-console.log}
```

## 8. Lectura para el consultor (resumen de riesgos)

1. **Funciona de verdad**: 8 combinaciones de versiÃ³n/modloader arrancan hasta el menÃº, incluidos Forge legacy (1.7.10) y NeoForge 1.21.1.
2. **El activo principal es el motor propio**: control total de classpath, nativos, argumentos y diagnÃ³stico; permite arreglar crashes que un launcher de librerÃ­a oculta.
3. **La deuda mÃ¡s cara es la operativa**: sin CI ni matriz automatizada, cada regresiÃ³n se detecta jugando.
4. **Riesgo de seguridad contenido**: repo limpio y `accounts.json` cifrado con AES-256-GCM (clave local, migraciÃ³n automÃ¡tica). La clave vive en el mismo directorio: protege el archivo, no a un atacante que ya ejecute cÃ³digo como el usuario.
5. **El CDN de cosmÃ©ticos ya no existe y se retirÃ³**: el jar va embebido en el `.exe`. El nuevo
   punto Ãºnico de fallo es Supabase; sin Ã©l se pierden los cosmÃ©ticos remotos, no el juego.

