# Configuración de Supabase para cosméticos in-game

Este documento explica cómo preparar el backend que permite que los jugadores
con TVLauncher se vean la capa y el sombrero en Minecraft.

## 1. Crear el proyecto

1. Entra en <https://supabase.com/dashboard>.
2. Crea un proyecto nuevo.
3. Guarda la contraseña de la base de datos en un lugar seguro. El launcher no
   la necesita.
4. Espera a que el proyecto esté `ACTIVE_HEALTHY`.

## 2. Crear la tabla

Abre **SQL Editor** y ejecuta el contenido de:

```text
supabase/migrations/20261031_cosmetics_profiles.sql
```

La tabla resultante es `public.cosmetics_profiles`:

| Columna | Tipo | Uso |
|---|---|---|
| `id` | bigint identity | Clave interna |
| `minecraft_uuid` | uuid unique | UUID oficial de la cuenta Microsoft |
| `offline_uuid` | uuid unique | UUID estándar de servidor offline |
| `cape_id` | text | `cape_tvlauncher` o `null` |
| `hat_id` | text | `hat_tvlauncher` o `null` |
| `updated_at` | timestamptz | Última publicación |

La tabla tiene RLS activado y una única política de lectura pública para
`anon` y `authenticated`. No hay políticas de escritura para clientes.

## 3. Desplegar la Edge Function

La función está en:

```text
supabase/functions/publish-cosmetics/
```

Se despliega con `verify_jwt = false` porque el `Authorization` que recibe es un
token de Minecraft, no un JWT de Supabase. La función valida ese token contra
`https://api.minecraftservices.com/minecraft/profile` antes de escribir.

Con Supabase CLI:

```bash
supabase functions deploy publish-cosmetics --project-ref <project-ref> --no-verify-jwt
```

La función usa automáticamente los secretos `SUPABASE_URL` y
`SUPABASE_SERVICE_ROLE_KEY` que Supabase inyecta en el entorno de las Edge
Functions. La `service_role` nunca debe copiarse al launcher, al mod ni al
repositorio.

## 4. Copiar la URL y la anon key

En **Project Settings → API**:

1. Copia **Project URL**.
2. Copia **anon public key**.

Pégalas en TVLauncher:

**Ajustes → Sincronización in-game**

La anon key es pública por diseño y solo permite `SELECT`. No pegues la
`service_role` en el launcher.

## 5. Probar la publicación

Necesitas un token de Minecraft válido. El launcher lo obtiene al iniciar sesión
con Microsoft; para una prueba manual puedes usar el token de una sesión de
prueba.

```bash
curl -X POST "$SUPABASE_URL/functions/v1/publish-cosmetics" \
  -H "apikey: $SUPABASE_ANON_KEY" \
  -H "Authorization: Bearer $MINECRAFT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"cape_id":"cape_tvlauncher","hat_id":"hat_tvlauncher"}'
```

Respuesta esperada:

```json
{ "ok": true }
```

Códigos de error:

| Código | Significado |
|---|---|
| `400` | Body o IDs inválidos |
| `401` | Token de Minecraft ausente, inválido o caducado |
| `405` | Método distinto de `POST` |
| `500` | Error de configuración o de base de datos |
| `502` | No se pudo consultar el perfil de Minecraft |

## 6. Comprobar la seguridad

Lectura pública:

```bash
curl "$SUPABASE_URL/rest/v1/cosmetics_profiles?select=*" \
  -H "apikey: $SUPABASE_ANON_KEY"
```

Debe devolver `200` y las filas publicadas.

Escritura anónima:

```bash
curl -X POST "$SUPABASE_URL/rest/v1/cosmetics_profiles" \
  -H "apikey: $SUPABASE_ANON_KEY" \
  -H "Authorization: Bearer $SUPABASE_ANON_KEY" \
  -H "Content-Type: application/json" \
  -d '{"cape_id":"cape_tvlauncher","hat_id":null}'
```

Debe fallar con `401` o `403`. Si la escritura funciona, revisa que RLS esté
activado y que no exista una política de escritura para `anon`.

## 7. Verificar la sincronización in-game

1. Configura URL y anon key en Ajustes.
2. Inicia sesión con una cuenta Microsoft.
3. Equipa `cape_tvlauncher` y `hat_tvlauncher`.
4. Lanza una instancia Fabric 1.21.11.
5. Comprueba que la fila aparece en `cosmetics_profiles`.
6. Entra con un segundo jugador que también use el launcher.
7. Verifica que ambos se ven la capa y el sombrero.

Para servidores offline-mode, el mod busca la fila por `offline_uuid`; para
servidores online-mode, por `minecraft_uuid`. Por eso la Edge Function guarda
ambos.

## 8. Límites de la beta

- Solo Minecraft Java 1.21.11 con Fabric.
- Solo los cosméticos `cape_tvlauncher` y `hat_tvlauncher`.
- Las cuentas offline no publican; solo ven sus cosméticos en local.
- Sin conexión a Supabase, el juego funciona pero no se ven cosméticos remotos.
- El sombrero puede solaparse con un casco en esta versión.
