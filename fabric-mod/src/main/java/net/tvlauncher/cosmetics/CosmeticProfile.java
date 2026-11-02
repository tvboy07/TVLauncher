package net.tvlauncher.cosmetics;

import java.time.Instant;
import java.util.UUID;

/**
 * Una fila de {@code public.cosmetics_profiles}.
 *
 * <p>Los campos de UUID y los IDs de cosmético son nulables: una cuenta offline
 * no tiene {@code minecraft_uuid} y un jugador puede llevar solo capa o solo
 * sombrero.
 */
public record CosmeticProfile(
    UUID minecraftUuid,
    UUID offlineUuid,
    String capeId,
    String hatId,
    Instant updatedAt) {
}
