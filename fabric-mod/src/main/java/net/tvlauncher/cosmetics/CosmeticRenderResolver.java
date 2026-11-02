package net.tvlauncher.cosmetics;

import java.util.Optional;
import java.util.UUID;

/**
 * Decide qué cosméticos mostrar para un jugador concreto.
 *
 * <p>El perfil remoto siempre gana. El fallback local (los IDs que el jugador
 * tiene equipados en el launcher) solo se aplica al jugador propio, porque es
 * lo único que se puede conocer sin backend.
 */
public final class CosmeticRenderResolver {
    public static final String CAPE_TVLAUNCHER = "cape_tvlauncher";
    public static final String HAT_TVLAUNCHER = "hat_tvlauncher";

    private CosmeticRenderResolver() {
    }

    public static Optional<ResolvedCosmetics> resolve(
        UUID uuid,
        boolean localPlayer,
        CosmeticsConfig config,
        CosmeticsRepository repository) {
        if (config == null || !config.enabled() || repository == null) {
            return Optional.empty();
        }

        Optional<CosmeticProfile> remote = repository.get(uuid);
        if (remote.isPresent()) {
            CosmeticProfile profile = remote.get();
            String cape = sanitize(profile.capeId(), CAPE_TVLAUNCHER);
            String hat = sanitize(profile.hatId(), HAT_TVLAUNCHER);
            return cape == null && hat == null
                ? Optional.empty()
                : Optional.of(new ResolvedCosmetics(cape, hat));
        }

        if (!localPlayer) {
            return Optional.empty();
        }

        String cape = sanitize(config.localCapeId(), CAPE_TVLAUNCHER);
        String hat = sanitize(config.localHatId(), HAT_TVLAUNCHER);
        return cape == null && hat == null
            ? Optional.empty()
            : Optional.of(new ResolvedCosmetics(cape, hat));
    }

    private static String sanitize(String id, String expected) {
        return expected.equals(id) ? id : null;
    }
}
