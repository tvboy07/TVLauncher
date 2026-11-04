# Changelog

## 1.0.0-beta.1 — 03/11/2026

Primera beta pública del launcher con núcleo de lanzamiento propio.

### Añadido

- Lanzamiento propio de Vanilla, Forge, NeoForge y Fabric sin CmlLib como motor.
- Cuentas offline y Microsoft con OAuth 2.0, PKCE y fallback a device code.
- Cosméticos in-game para Fabric 1.21.11:
  - Capa y sombrero TVLauncher.
  - Skin local en primera y tercera persona.
  - Subida de skin a Mojang para cuentas premium.
  - Backend opcional Supabase para cosméticos remotos.
- Visor 3D de skins y armario con biblioteca local.
- Buscador y descarga de mods de Modrinth con SHA-512 y cancelación.
- Normalización y remapeo de skins legacy 64x32.
- Interruptor real de cosméticos en Ajustes.

### Seguridad

- El token de Minecraft ya no queda en `DefaultRequestHeaders` de un `HttpClient`
  reutilizado: cada petición usa su propio header.
- Enmascarado global de logs (`Bearer`, JWT, `--accessToken`, `access_token=...`).
- La clave de `accounts.json` se protege con DPAPI en Windows, con migración
  automática desde la clave plana anterior.
- Al eliminar una cuenta se borran sus tokens en memoria.
- Documento `docs/SECURITY.md` con modelo de amenazas y advertencia de Supabase.

### Corregido

- La capa del launcher se dibuja con la textura de 64x32 que espera el modelo vanilla.
- Elytras visibles al reemplazar la capa por la TVLauncher.
- La skin se aplica también en primera persona.
- El checkbox de cosméticos desactiva el mod que ya está en la instancia.
- Documentación de README, roadmap e inventario actualizada.

### Limitaciones conocidas

- Regresión de rendimiento de cosméticos (~1–2 ms/frame en algunos equipos).
- El `.exe` no está firmado; Smart App Control puede bloquearlo.
- Los cosméticos remotos requieren configurar Supabase.
- Solo Fabric 1.21.11 para cosméticos in-game.
