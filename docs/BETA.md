# TVLauncher — Guía de la beta

> Última actualización: 03/11/2026.

Esta beta está centrada en el **núcleo de lanzamiento propio** y en los **cosméticos
in-game** para Fabric 1.21.11. El objetivo es que cualquier instancia llegue al menú
principal y que, si falla, los logs digan exactamente por qué.

## Qué incluye

- Lanzamiento propio de Vanilla, Forge, NeoForge y Fabric.
- Cuentas offline y Microsoft (OAuth con PKCE y fallback a device code).
- Instancias aisladas, RAM configurable, Java detectado automáticamente.
- Visor 3D de skins, armario y biblioteca de skins.
- Capa y sombrero TVLauncher en Fabric 1.21.11.
- Skin local en primera y tercera persona; subida a Mojang para cuentas premium.
- Buscador de mods de Modrinth con verificación SHA-512 y cancelación.
- Servidor SMP por defecto configurable.

## Requisitos

- Windows 10/11 x64 para el paquete probado (Avalonia también compila en Linux/macOS).
- **.NET 8 Desktop Runtime** si usás el paquete portable `beta-dotnet`.
- Java compatible con la versión de Minecraft (el launcher lo detecta).
- Para cosméticos: Minecraft **1.21.11**, **Fabric Loader 0.20.1** y **Fabric API**.

## Cómo ejecutar la beta

### Opción A — ejecutable portable (recomendado si está firmado)

Ejecutar `TVLauncher.exe` del paquete.

> **Smart App Control:** Windows 11 puede bloquear el `.exe` porque todavía no está
> firmado. Si aparece “Una directiva de Control de aplicaciones bloqueó este archivo”,
> usá la opción B o pedí un build firmado.

### Opción B — paquete `beta-dotnet`

Requiere .NET 8 Desktop Runtime.

```text
Iniciar-TVLauncher.cmd
```

Ese script ejecuta `dotnet TVLauncher.dll`, que usa el host firmado de .NET y evita el
bloqueo del `.exe` sin firmar.

## Pruebas mínimas antes de reportar

1. Abrir el launcher y crear/editar una instancia.
2. Lanzar una instancia Vanilla/Forge/Fabric y llegar al menú principal.
3. En Fabric 1.21.11:
   - Equipar capa y sombrero; verificar en tercera persona y con F5.
   - Equipar una skin de “Mis Skins”; verificar primera y tercera persona.
   - Poner elytras y comprobar que no desaparecen.
   - Desmarcar **Cosméticos** en Ajustes y verificar que el mod deja de renderizar.
4. Revisar los logs si algo falla:
   - `%LOCALAPPDATA%\TVLauncher\logs\tvlauncher.log`
   - `%LOCALAPPDATA%\TVLauncher\logs\minecraft-console.log`
   - `%LOCALAPPDATA%\TVLauncher\instances\{instancia}\logs\latest.log`

## Limitaciones conocidas

- **Rendimiento de cosméticos:** el mod agrega ~1–2 ms/frame en algunos equipos. Se
  corregirá en una actualización; no bloquea la beta.
- **Skins legacy 64x32:** se remapean brazos y piernas, pero pueden quedar diferencias
  en diseños muy antiguos.
- **Cosméticos remotos:** requieren configurar Supabase (URL + anon key) en Ajustes.
  Sin backend, solo se ven los cosméticos locales del jugador.
- **Cuentas offline:** su skin se aplica solo en el cliente; no se publica a Mojang ni
  la ven otros jugadores.
- **Fabric 1.21.11** es la única versión soportada por el mod de cosméticos.
- **Firma:** el ejecutable todavía no está firmado; Smart App Control puede bloquearlo.
- **Matriz de arranque:** la cobertura de versiones se verifica a mano.
- **Supabase:** no incluir nunca la `service_role` en el launcher, el mod ni el repo.

## Reportar un problema

Incluir:

1. Versión de TVLauncher y sistema operativo.
2. Versión de Minecraft, modloader y Java.
3. Pasos para reproducir.
4. `tvlauncher.log`, `minecraft-console.log` y, si existe, el crash report.
5. Si es de cosméticos: `config/tvl-cosmetics.json`, `logs/latest.log` y si la cuenta es
   Microsoft u offline.

No adjuntar `accounts.json`, `accounts.key` ni tokens.
