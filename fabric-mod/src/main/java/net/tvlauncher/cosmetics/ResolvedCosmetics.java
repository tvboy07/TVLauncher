package net.tvlauncher.cosmetics;

/**
 * Cosméticos ya validados que deben renderizarse para un jugador.
 *
 * @param capeId {@code cape_tvlauncher} o {@code null}
 * @param hatId  {@code hat_tvlauncher} o {@code null}
 */
public record ResolvedCosmetics(String capeId, String hatId) {
    public boolean hasCape() {
        return capeId != null;
    }

    public boolean hasHat() {
        return hatId != null;
    }
}
