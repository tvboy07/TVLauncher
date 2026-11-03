package net.tvlauncher.cosmetics;

import java.util.Optional;
import java.util.UUID;

/**
 * Estado global de renderizado de cosméticos.
 *
 * <p>Lo escribe el hilo del cliente al refrescar la caché y lo lee el hilo de
 * render al dibujar cada jugador. Ambos son el mismo hilo en Minecraft, pero
 * los campos son {@code volatile} para que el refresco asíncrono no deje
 * valores a medias.
 */
public final class CosmeticRenderState {
    private static volatile CosmeticsRepository repository;
    private static volatile CosmeticsConfig config = CosmeticsConfig.disabled();
    private static volatile UUID localUuid;

    private CosmeticRenderState() {
    }

    public static void initialize(CosmeticsRepository newRepository, CosmeticsConfig newConfig) {
        repository = newRepository;
        config = newConfig == null ? CosmeticsConfig.disabled() : newConfig;
    }

    public static void setLocalUuid(UUID uuid) {
        localUuid = uuid;
    }

    public static Optional<ResolvedCosmetics> resolve(int entityId) {
        UUID uuid = PlayerIdentityMap.get(entityId);
        if (uuid == null) {
            return Optional.empty();
        }

        CosmeticsRepository currentRepository = repository;
        CosmeticsConfig currentConfig = config;
        boolean localPlayer = uuid.equals(localUuid);

        return CosmeticRenderResolver.resolve(uuid, localPlayer, currentConfig, currentRepository);
    }

    public static boolean hasCustomCape(int entityId) {
        return resolve(entityId).map(ResolvedCosmetics::hasCape).orElse(false);
    }

    public static void clearForTests() {
        repository = null;
        config = CosmeticsConfig.disabled();
        localUuid = null;
        PlayerIdentityMap.clear();
    }
}
