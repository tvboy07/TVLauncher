# Diseño: cosméticos in-game para todos los jugadores

- **Fecha:** 2026-10-31
- **Estado:** Aprobado para implementación
- **Alcance:** beta de TVLauncher, Minecraft Java 1.21.11 + Fabric
- **Objetivo:** que un jugador con TVLauncher vea la capa y el sombrero de **cualquier otro jugador que también use TVLauncher**, sin depender del servidor donde estén jugando.

## 1. Contexto

El armario 3D del launcher ya renderiza la capa y el sombrero, pero ese render no se traslada al juego. La implementación anterior dependía de un `tvl-cosmetics.jar` externo y de un CDN que ya no resuelve:

- `https://cdn.tvlauncher.net/cosmetics/tvl-cosmetics.jar` no existe.
- El launcher solo escribía IDs en `config/tvl-cosmetics-client.json`.
- No había mod cliente ni assets in-game.

Además, un cliente de Minecraft no puede hablar directamente con otro cliente: solo habla con el servidor. Para que dos jugadores se vean los cosméticos en **cualquier** servidor, hace falta una fuente de datos global.

## 2. Decisiones aprobadas

| Tema | Decisión |
|---|---|
| Plataforma del mod | Solo Fabric 1.21.11, Java 21 |
| Loader / API | Fabric Loader 0.20.1, Fabric API 0.141.3+1.21.11 |
| Backend | Supabase (Postgres + REST + Edge Function) |
| Visibilidad | Todos los jugadores visibles que tengan el launcher |
| Dependencia del servidor | Ninguna; no hay plugin Paper |
| Cuentas offline | Solo render local; no se publican |
| Credenciales Supabase | Scaffold con placeholders + documento de configuración |
| Distribución del mod | Jar embebido en el `.exe` del launcher |
| Cosméticos de la beta | `cape_tvlauncher` y `hat_tvlauncher` |

## 3. Objetivos y no objetivos

### Objetivos

1. Publicar `UUID → capa/sombrero` desde el launcher para cuentas Microsoft verificadas.
2. Leer esos datos desde el mod para todos los jugadores visibles.
3. Renderizar capa y sombrero con la misma geometría, UV y textura del visor.
4. Funcionar en servidores online-mode y offline-mode.
5. Degradar sin romper el juego si Supabase no está disponible.
6. Mantener la suite .NET existente en verde y añadir pruebas del nuevo flujo.

### No objetivos

- Sincronizar cosméticos de cuentas offline con otros jugadores.
- Soportar Forge, NeoForge, versiones antiguas o Bedrock.
- Añadir un plugin de servidor.
- Implementar tienda, permisos premium o cosméticos personalizados por URL.
- Cambiar el sistema de skins del launcher.
- Renovar tokens de Microsoft caducados como parte de esta beta.

## 4. Arquitectura

```text
┌──────────────────────────┐
│ TVLauncher               │
│  - equipa cosméticos     │
│  - publica en Supabase   │
│  - inyecta el mod        │
└────────────┬─────────────┘
             │ HTTPS + token Minecraft
             ▼
┌──────────────────────────┐
│ Supabase                 │
│  - Edge Function         │
│  - tabla de perfiles     │
│  - RLS: lectura pública  │
└────────────┬─────────────┘
             │ HTTPS + anon key
             ▼
┌──────────────────────────┐
│ Mod Fabric 1.21.11       │
│  - consulta UUIDs        │
│  - cachea perfiles       │
│  - renderiza a todos     │
└──────────────────────────┘
```

Flujo:

1. El usuario equipa capa/sombrero en el armario.
2. El launcher publica los IDs en Supabase.
3. El launcher inyecta el mod y escribe su configuración en la instancia.
4. El mod arranca, lee la configuración y consulta los UUID de los jugadores visibles.
5. El mod renderiza la capa/sombrero de cada jugador con perfil remoto.
6. Para el jugador local sin perfil remoto, el mod usa los IDs locales como fallback.

## 5. Modelo de datos en Supabase

### 5.1 Tabla `cosmetics_profiles`

```sql
create table public.cosmetics_profiles (
  id             bigint generated always as identity primary key,
  minecraft_uuid uuid unique,
  offline_uuid   uuid unique,
  cape_id        text,
  hat_id         text,
  updated_at     timestamptz not null default now(),
  constraint at_least_one_uuid
    check (minecraft_uuid is not null or offline_uuid is not null)
);

alter table public.cosmetics_profiles enable row level security;

create policy "cosmetics are publicly readable"
  on public.cosmetics_profiles
  for select
  to anon, authenticated
  using (true);
```

Notas:

- `minecraft_uuid`: UUID oficial de la cuenta Microsoft.
- `offline_uuid`: UUID estándar de servidor offline.
- Se guardan ambos para que el mod encuentre al jugador tanto en servidores online-mode como offline-mode.
- No se guarda el nombre del jugador.
- `cape_id` y `hat_id` aceptan `null`.
- No hay políticas de escritura para `anon`; solo la Edge Function escribe con `service_role`.
- Los IDs desconocidos se ignoran en el mod, no rompen el render.

### 5.2 Consulta del mod

```http
GET /rest/v1/cosmetics_profiles
  ?select=minecraft_uuid,offline_uuid,cape_id,hat_id,updated_at
  &or=(minecraft_uuid.eq.<uuid1>,offline_uuid.eq.<uuid1>,
       minecraft_uuid.eq.<uuid2>,offline_uuid.eq.<uuid2>)
```

Cabeceras:

```http
apikey: <anon-key>
Authorization: Bearer <anon-key>
```

La consulta se hace por lotes de hasta 50 UUIDs. Si hay dos filas para el mismo jugador, se prefiere:

1. la fila cuyo `minecraft_uuid` coincide con el UUID del jugador;
2. la fila cuyo `offline_uuid` coincide;
3. la de `updated_at` más reciente.

## 6. Edge Function `publish-cosmetics`

Ruta:

```text
POST /functions/v1/publish-cosmetics
```

### 6.1 Entrada

```json
{
  "cape_id": "cape_tvlauncher",
  "hat_id": "hat_tvlauncher"
}
```

- `cape_id` y `hat_id` pueden ser `null`.
- Validación: `null` o `^[a-z0-9_]{1,64}$`.
- El body no contiene UUID ni nombre.

### 6.2 Autenticación

```http
Authorization: Bearer <token de Minecraft>
apikey: <anon-key>
```

La función:

1. Llama a `https://api.minecraftservices.com/minecraft/profile` con el token.
2. Si la respuesta no es 200, devuelve `401` y no escribe.
3. Obtiene `id` (UUID oficial) y `name` del perfil verificado.
4. Calcula el UUID offline estándar a partir de `name`.
5. Hace upsert por `minecraft_uuid` con `service_role`.
6. Actualiza `updated_at`.

La función se despliega con `verify_jwt = false` porque el token es de Minecraft, no un JWT de Supabase. La validación real la hace la propia función.

### 6.3 Respuestas

| Código | Significado |
|---|---|
| `200` | Publicado |
| `400` | Body inválido |
| `401` | Token de Minecraft inválido o caducado |
| `502` | No se pudo consultar el perfil de Minecraft |
| `500` | Error de base de datos |

No se registran tokens en logs.

### 6.4 UUID offline

Algoritmo estándar de Minecraft:

```text
MD5("OfflinePlayer:" + nombre)
```

Luego:

- versión 3 en el byte 6;
- variante RFC 4122 en el byte 8;
- formato `8-4-4-4-12` en big-endian.

Vector de prueba:

```text
Notch -> b50ad385-829d-3141-a216-7e7d7539ba7f
```

## 7. Cambios en el launcher

### 7.1 Configuración

Añadir a `AppConfig`:

```csharp
public string CosmeticsSupabaseUrl { get; set; } = string.Empty;
public string CosmeticsSupabaseAnonKey { get; set; } = string.Empty;
```

- La anon key es pública por diseño; la `service_role` nunca se guarda en el launcher.
- Si la URL o la key están vacías:
  - no se publica;
  - el mod puede seguir mostrando los cosméticos locales del jugador propio;
  - no se rompe el lanzamiento.
- Eliminar el uso de `CosmeticsJarUrl` y del CDN muerto. La propiedad puede quedar como obsoleta para no romper configuraciones antiguas, pero no se usa.
- `AutoInjectCosmetics` y `EnableCosmetics` se mantienen.

Añadir en Ajustes una sección “Sincronización in-game” con:

- URL de Supabase;
- anon key;
- texto de ayuda que indique que la sincronización entre jugadores requiere esos datos.

`SettingsViewModel` carga y guarda ambos campos.

### 7.2 Publicación

Nuevo servicio `CosmeticsPublisher`:

```csharp
Task<PublishResult> PublishAsync(
    string? capeId,
    string? hatId,
    string minecraftAccessToken,
    CancellationToken cancellationToken);
```

- `POST` a `{CosmeticsSupabaseUrl}/functions/v1/publish-cosmetics`.
- Cabeceras:
  - `apikey: <anon-key>`;
  - `Authorization: Bearer <token Minecraft>`;
  - `Content-Type: application/json`.
- Timeout de 8 s.
- No lanza excepciones hacia el flujo de lanzamiento; devuelve un resultado con error.
- Solo se usa con `AccountType.Microsoft`.
- Para cuentas offline no se llama.

Disparadores:

1. Al equipar/desequipar, con debounce de 500 ms.
2. Al lanzar el juego, antes de inyectar el mod.
3. Si falla, se registra y se muestra un estado discreto; el juego continúa.

### 7.3 Jar embebido

- El proyecto del mod genera `tvl-cosmetics.jar`.
- El jar se copia a `Resources/tvl-cosmetics.jar`.
- `TVLauncher.csproj` lo incluye como `EmbeddedResource` con nombre lógico estable.
- `CosmeticsService` lo extrae a:

```text
%LOCALAPPDATA%\TVLauncher\cosmetics\tvl-cosmetics.jar
```

- Compara SHA-256 entre el recurso embebido y el archivo local; sobrescribe si difiere.
- Copia a `mods/tvl-cosmetics.jar` si falta o si el hash difiere.
- No hay descarga HTTP del jar.
- Si la copia falla, se registra el error, pero **no se aborta** la escritura de la configuración del mod.

### 7.4 Configuración que recibe el mod

`SyncCosmeticsToInstanceAsync` escribirá en la instancia:

```text
config/tvl-cosmetics.json
```

```json
{
  "schema_version": 1,
  "enabled": true,
  "supabase_url": "https://<proyecto>.supabase.co",
  "supabase_anon_key": "<anon-key>",
  "local_cape_id": "cape_tvlauncher",
  "local_hat_id": "hat_tvlauncher",
  "poll_seconds": 30
}
```

- `enabled` es `false` si `EnableCosmetics` está desactivado.
- `local_*` se usan solo para el jugador local.
- El archivo se escribe antes de intentar copiar el jar.
- El mod vuelve a leerlo si cambia el `mtime` o cada 30 s.

### 7.5 IDs equipados

La fuente de verdad para publicar y para el fallback local será:

- `AppConfig.EquippedCapeId`;
- `AppConfig.EquippedHatId`.

`EquippedCosmetics` se mantiene por compatibilidad con el armario, pero se limpian IDs obsoletos (`cape_1`, `hat_1`) y se sincroniza con los dos campos anteriores.

## 8. Mod Fabric

### 8.1 Proyecto

```text
fabric-mod/
  build.gradle
  gradle.properties
  settings.gradle
  gradlew / gradlew.bat
  gradle/wrapper/
  src/main/java/net/tvlauncher/cosmetics/
  src/main/resources/
    fabric.mod.json
    tvl-cosmetics.mixins.json
    assets/tvl-cosmetics/textures/cape_tvlauncher.png
    assets/tvl-cosmetics/textures/hat_tvlauncher.png
```

- Fabric Loom.
- Java 21 toolchain.
- Yarn mappings para 1.21.11.
- Loader 0.20.1.
- Fabric API 0.141.3+1.21.11.
- Dependencias declaradas en `fabric.mod.json`:
  - `fabricloader >=0.16.0`;
  - `minecraft ~1.21.11`;
  - `java >=21`;
  - `fabric-api *`.

### 8.2 Configuración

`CosmeticsConfig` lee `config/tvl-cosmetics.json` con `FabricLoader.getInstance().getConfigDir()`.

Campos:

- `schema_version`;
- `enabled`;
- `supabase_url`;
- `supabase_anon_key`;
- `local_cape_id`;
- `local_hat_id`;
- `poll_seconds`.

Si el archivo falta, el mod no renderiza nada. Si `enabled` es `false`, no renderiza nada.

### 8.3 Repositorio de perfiles

`CosmeticsRepository`:

- `HttpClient` de Java 21.
- Executor daemon de un solo hilo; nunca hace I/O en el hilo de render.
- Timeout de conexión 3 s y de petición 5 s.
- Caché concurrente `UUID → CosmeticProfile`.
- TTL positivo: 60 s.
- TTL negativo: 30 s.
- Refresco:
  - cada `poll_seconds` (30 s por defecto);
  - al entrar o cambiar de mundo;
  - cuando aparece un UUID que no está en caché.
- Backoff exponencial ante 429/5xx, con máximo de 5 minutos.
- No lanza excepciones al bucle del cliente.

`CosmeticProfile`:

```java
record CosmeticProfile(
    UUID minecraftUuid,
    UUID offlineUuid,
    String capeId,
    String hatId,
    Instant updatedAt
) {}
```

### 8.4 Renderizado

`TvCosmeticsFeatureRenderer`:

- Se registra con `LivingEntityFeatureRendererRegistrationCallback` para `PlayerEntityRenderer`.
- Se ejecuta después del modelo del jugador.
- Consulta el perfil por el UUID del jugador.
- Si no hay perfil remoto y el jugador es el local, usa los IDs locales.
- Si no hay nada, no dibuja.

#### Capa

- Caja de 10×16×1 unidades de modelo.
- Anclada al torso, detrás de la espalda.
- Textura `tvl-cosmetics:textures/cape_tvlauncher.png` (64×32).
- UV equivalentes al visor.
- Inclinación suave; sin física compleja.
- Se oculta si el jugador lleva elytra.
- Se cancela la capa vanilla del jugador con un mixin puntual para evitar solapamiento.

#### Sombrero

- Ala, copa y cinta.
- Anclado a la cabeza; hereda rotación y pitch.
- Inclinación de 2°.
- Textura `tvl-cosmetics:textures/hat_tvlauncher.png` (64×64).
- UV equivalentes al visor.

#### Limitaciones de la beta

- El sombrero puede solaparse con un casco; no se oculta automáticamente en esta versión.
- No hay física de tela.
- Solo se renderizan los dos IDs de la beta.

### 8.5 Mixin de la capa vanilla

`CapeFeatureRendererMixin`:

- Cancela el render de la capa vanilla si el jugador tiene `cape_tvlauncher` en el perfil remoto o local.
- Si el mapeo de 1.21.11 cambia, se ajusta el target durante la implementación.
- El mixin solo afecta al cliente.

## 9. UUID offline: corrección obligatoria

El código actual de `AuthService.GetOfflineUuid` usa:

```csharp
return new Guid(hashBytes).ToString();
```

Eso interpreta los primeros bytes en little-endian. Minecraft/Java los interpreta en big-endian. Ejemplo:

```text
Launcher actual: 85d30ab5-9d82-4131-a216-7e7d7539ba7f
Minecraft real:  b50ad385-829d-3141-a216-7e7d7539ba7f
```

Corrección:

- Formatear el hash MD5 manualmente como UUID big-endian.
- Establecer versión 3 y variante RFC 4122.
- Test con `Notch`.
- Migrar cuentas offline existentes al cargarlas: recalcular `Uuid` y guardar si cambió.

Sin esta corrección, una cuenta Microsoft en un servidor offline-mode no coincidiría con su fila de Supabase.

## 10. Texturas compartidas

La generación actual de texturas vive en `SkinsPreviewViewModel`:

- `GenerateTvLauncherCape()`;
- `GenerateTvLauncherHat()`.

Se extraerá a `Services/CosmeticTextureFactory.cs`:

```csharp
internal static byte[] CreateCapePng();
internal static byte[] CreateHatPng();
```

- El visor usará la fábrica.
- Los PNG del mod se generarán desde la misma fábrica.
- Un test comparará los PNG comprometidos con la salida de la fábrica para evitar que el visor y el mod se desincronicen.

## 11. Seguridad y privacidad

- La anon key es pública y solo permite `SELECT`.
- La `service_role` solo existe como secreto de la Edge Function.
- El UUID y el nombre se obtienen del perfil verificado de Minecraft, no del body.
- Un usuario solo puede modificar su propia fila.
- No se guardan nombres de jugador en la tabla.
- La tabla es legible por cualquiera que tenga la anon key; los cosméticos no son datos sensibles.
- No se envían tokens de Minecraft al mod ni se escriben en logs.
- Las cuentas offline no publican nada; solo ven sus cosméticos en su propio cliente.

## 12. Manejo de errores

| Situación | Comportamiento |
|---|---|
| URL/key de Supabase vacías | No se publica; el mod usa fallback local para el jugador propio |
| Token caducado o inválido | La publicación falla; el juego sigue |
| Supabase caído | El mod no renderiza cosméticos remotos; no crashea |
| Respuesta 429/5xx | Backoff en el mod; reintento posterior |
| Jugador sin fila | Sin cosméticos remotos; fallback local solo si es el jugador propio |
| ID desconocido | Se ignora |
| Jar no se puede copiar | Se registra; la configuración del mod se escribe igualmente |
| Sin conexión a Internet | El juego funciona; no hay cosméticos remotos |

## 13. Pruebas

### .NET

- `GetOfflineUuid` con vector `Notch`.
- Migración de cuentas offline con UUID antiguo.
- `CosmeticsPublisher`:
  - body correcto;
  - cabeceras correctas;
  - 401/403;
  - timeout;
  - cuenta offline no publica;
  - URL/key vacías no publican.
- `CosmeticsService`:
  - extrae el jar embebido;
  - copia a `mods/`;
  - sobrescribe si cambia el hash;
  - escribe `tvl-cosmetics.json` aunque falle la copia.
- `CosmeticTextureFactory`:
  - dimensiones 64×32 y 64×64;
  - hash de los PNG del mod coincide.
- `ConfigService`:
  - configuraciones antiguas con `CosmeticsJarUrl` siguen cargando;
  - IDs obsoletos se limpian.
- Mantener los 169 tests existentes en verde.

### Java

- Parseo de la respuesta de Supabase.
- Coincidencia por `minecraft_uuid` y `offline_uuid`.
- Preferencia de fila cuando hay duplicados.
- Caché positiva y negativa.
- Backoff.
- IDs desconocidos.
- Configuración ausente o inválida.

### Manual

1. Compilar el mod y el launcher.
2. Configurar Supabase con el documento de setup.
3. Lanzar Fabric 1.21.11.
4. Equipar capa/sombrero y verificar el render propio.
5. Verificar en un servidor online-mode.
6. Verificar en un servidor offline-mode.
7. Ver a un segundo jugador con launcher.
8. Detener Supabase o desconectar la red y comprobar que el juego no crashea.
9. Comprobar que una cuenta offline solo se ve a sí misma.

## 14. Configuración de Supabase

Como todavía no hay proyecto, se incluye:

```text
supabase/
  migrations/20261031_cosmetics_profiles.sql
  functions/publish-cosmetics/index.ts
  config.toml
docs/SUPABASE_SETUP.md
```

`docs/SUPABASE_SETUP.md` explicará:

1. Crear el proyecto en Supabase.
2. Ejecutar la migración.
3. Desplegar la Edge Function con `verify_jwt = false`.
4. Copiar la URL y la anon key.
5. Pegarlas en Ajustes → Sincronización in-game.
6. Probar la publicación con `curl`.
7. Comprobar que la tabla solo permite lectura pública.

Mientras no se configuren URL/key:

- el launcher compila y los tests pasan;
- el mod se inyecta y muestra los cosméticos locales del jugador;
- los cosméticos de otros jugadores no aparecen.

## 15. Criterios de aceptación

- [ ] El launcher publica capa/sombrero de una cuenta Microsoft verificada.
- [ ] La Edge Function rechaza tokens inválidos y no permite escrituras anónimas.
- [ ] El mod renderiza la capa/sombrero de todos los jugadores visibles con perfil remoto.
- [ ] El render coincide con el visor en geometría, UV y textura.
- [ ] Funciona en servidor online-mode y offline-mode.
- [ ] Una cuenta offline solo ve sus cosméticos en local.
- [ ] Sin Supabase el juego no crashea y el fallback local funciona.
- [ ] El jar del mod va embebido; no se usa el CDN muerto.
- [ ] La suite .NET pasa completa.
- [ ] La build Release se genera y se documenta el hash.
- [ ] `docs/INVENTARIO.md` y `docs/SUPABASE_SETUP.md` reflejan el estado real.

## 16. Riesgos

| Riesgo | Mitigación |
|---|---|
| Los mappings de 1.21.11 cambian nombres de render | Compilar contra Yarn y ajustar el feature renderer/mixin |
| El token de Minecraft caduca | La publicación falla sin bloquear; se reintenta en el siguiente lanzamiento |
| Supabase no está configurado | Fallback local y documento de setup |
| El sombrero se solapa con cascos | Limitación documentada de la beta |
| La capa vanilla se sigue viendo | Mixin de cancelación y prueba visual |
| El jar embebido y el fuente se desincronizan | Script de build + test de hash de texturas/artefacto |

## 17. Trabajo futuro

- Publicación verificada para cuentas offline mediante algún mecanismo de confianza.
- Cosméticos personalizados con texturas subidas por el usuario.
- Realtime en lugar de polling.
- Física de capa.
- Soporte de más versiones/loaders.
- Panel de moderación y limpieza de perfiles inactivos.
