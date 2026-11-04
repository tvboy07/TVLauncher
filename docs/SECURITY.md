# Seguridad de cuentas y tokens

> Documento para la beta. Si encontrás un problema de seguridad, no lo publiques
> en un issue con tokens o logs crudos: contactá al mantenedor del repositorio.

## Qué guarda el launcher

`accounts.json` contiene las cuentas y sus tokens de Minecraft/Microsoft. Se guarda
cifrado con **AES-256-GCM** en `%LOCALAPPDATA%\TVLauncher` (o el directorio de datos
configurado). La clave es un bloque aleatorio de 32 bytes en `accounts.key`.

- En Windows la clave se protege con **DPAPI** (`CurrentUser`): el archivo no se
  puede descifrar copiándolo a otro usuario u otro equipo.
- En Unix se aplica `chmod 600` a la clave y se guarda en un sobre JSON.
- La escritura es atómica (`.part` + rename) para no dejar un archivo a medias.
- Si el archivo no se puede descifrar, el launcher **bloquea el guardado** para no
  pisar las cuentas existentes.
- Las claves antiguas de 32 bytes se migran automáticamente a DPAPI en Windows.

La clave vive junto a `accounts.json`: protege el archivo **en reposo y si se copia
a otro usuario/equipo**, pero no a un proceso que ya se ejecute como el mismo
usuario (malware con la sesión iniciada). Para ese caso hacen falta otras defensas
del sistema operativo.

## Tokens y logs

- Los tokens nunca deben escribirse en `tvlauncher.log` ni en `minecraft-console.log`.
- `SecretMasker` enmascara campos JSON (`access_token`, `refresh_token`, `id_token`,
  `Token`, `uhs`), `Bearer …`, JWT, `access_token=…` y `--accessToken …`.
- `Log.Write` aplica el enmascarado **globalmente** como defensa en profundidad.
- El token de Minecraft se envía en un `HttpRequestMessage` por petición; **nunca**
  se guarda en `DefaultRequestHeaders` de un `HttpClient` reutilizado.
- Aun así, **no compartas logs sin revisarlos**: pueden contener rutas, nombres de
  usuario, IDs de instancia o mensajes de error de terceros.

### Exposición residual: línea de comandos del proceso

Minecraft exige `--accessToken` en los argumentos del proceso Java. El launcher lo
enmascara en sus logs (`--accessToken ***`), pero el sistema operativo puede exponer
la línea de comandos a otros procesos del mismo usuario. Es una limitación del
protocolo de Minecraft, no del launcher. No ejecutes el juego en equipos compartidos
con cuentas premium si esto es un riesgo para vos.

## OAuth de Microsoft

- Authorization Code + PKCE (S256), redirect a `http://127.0.0.1` (loopback).
- `state` aleatorio y verificación del callback.
- Sin `client_secret` en la aplicación: el Client ID es público.
- Fallback a Device Code Flow si el proveedor rechaza el loopback.
- Los tokens de Microsoft/Xbox/Minecraft se enmascaran en los logs.
- No se envían tokens a endpoints distintos de los oficiales de Microsoft/Mojang,
  salvo que actives Supabase (ver abajo).

## Supabase (cosméticos remotos)

Si configurás una URL y una anon key, el launcher envía el **token de Minecraft** a
tu Edge Function `publish-cosmetics` para verificar que sos el dueño del perfil.

- Usá solo un backend que controles.
- La `service_role` debe vivir **solo** en el servidor (Edge Function). Nunca en el
  launcher, el mod, el repositorio ni una captura.
- La anon key es pública; la tabla debe tener RLS y la anon key solo `SELECT`.
- La función no guarda el token; lo usa para consultar el perfil de Mojang y luego
  lo descarta. Aun así, el token transita el backend elegido.
- Si no configurás Supabase, los cosméticos remotos quedan desactivados y el juego
  sigue funcionando con los cosméticos locales.

## Cuentas offline

Las cuentas offline usan un UUID derivado del nombre y no publican datos a Mojang.
Su skin/capa se renderiza solo en el cliente; no la ven otros jugadores.

## Qué no hacer

- No subir `accounts.json`, `accounts.key`, `launcher_profiles.json` ni logs crudos.
- No pegar tokens en issues, Discord, capturas o commits.
- No usar la `service_role` de Supabase en el launcher ni en el mod.
- No ejecutar builds de terceros que pidan tu token.

## Pendientes de hardening

- Keychain (macOS) o libsecret (Linux) para la clave; hoy Unix usa `chmod 600`.
- Refresh proactivo y revocación de tokens.
- Revisar periódicamente que ningún `Console.WriteLine` nuevo evite `Log`.
- Firmar el ejecutable para distribución.
