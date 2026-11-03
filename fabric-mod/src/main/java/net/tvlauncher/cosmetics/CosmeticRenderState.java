package net.tvlauncher.cosmetics;

import java.util.Map;
import java.util.Optional;
import java.util.UUID;

/**
 * Estado global de renderizado de cosméticos.
 *
 * <p>El cliente recalcula una vez por tick las banderas de cada jugador
 * (local/capa/sombrero) y el render las lee sin volver a resolver perfiles ni
 * crear {@link Optional}s en cada frame. Los campos son {@code volatile}
 * porque el refresco asíncrono puede escribir mientras el render lee.
 */
public final class CosmeticRenderState {
    public record RenderFlags(boolean local, boolean cape, boolean hat) {
    }

    private static volatile CosmeticsRepository repository;
    private static volatile CosmeticsConfig config = CosmeticsConfig.disabled();
    private static volatile UUID localUuid;
    private static volatile Map<Integer, RenderFlags> renderFlags = Map.of();

    private CosmeticRenderState() {
    }

    public static void initialize(CosmeticsRepository newRepository, CosmeticsConfig newConfig) {
        repository = newRepository;
        config = newConfig == null ? CosmeticsConfig.disabled() : newConfig;
    }

    public static void setLocalUuid(UUID uuid) {
        localUuid = uuid;
    }

    /** Reemplaza las banderas cacheadas para este tick. */
    public static void setRenderFlags(Map<Integer, RenderFlags> flags) {
        renderFlags = flags == null || flags.isEmpty() ? Map.of() : Map.copyOf(flags);
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
        RenderFlags flags = renderFlags.get(entityId);
        if (flags != null) {
            return flags.cape();
        }
        return resolve(entityId).map(ResolvedCosmetics::hasCape).orElse(false);
    }

    public static boolean hasHat(int entityId) {
        RenderFlags flags = renderFlags.get(entityId);
        if (flags != null) {
            return flags.hat();
        }
        return resolve(entityId).map(ResolvedCosmetics::hasHat).orElse(false);
    }

    /** Indica si el id de entidad pertenece al jugador local. */
    public static boolean isLocalPlayer(int entityId) {
        RenderFlags flags = renderFlags.get(entityId);
        if (flags != null) {
            return flags.local();
        }

        UUID uuid = PlayerIdentityMap.get(entityId);
        return uuid != null && uuid.equals(localUuid);
    }

    public static void clearForTests() {
        repository = null;
        config = CosmeticsConfig.disabled();
        localUuid = null;
        renderFlags = Map.of();
        PlayerIdentityMap.clear();
    }
}
