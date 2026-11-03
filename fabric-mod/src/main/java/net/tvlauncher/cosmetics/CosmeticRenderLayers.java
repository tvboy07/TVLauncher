package net.tvlauncher.cosmetics;

import net.minecraft.client.render.RenderLayer;
import net.minecraft.client.render.RenderLayers;
import net.minecraft.util.Identifier;

/**
 * Capas de render de los cosméticos TVLauncher.
 *
 * <p>Usa el pipeline de entidades con recorte alfa y la textura propia del mod.
 * Las capas se piden cada frame porque {@link RenderLayers#entityCutout} las
 * memoiza por textura, así que no hace falta cachearlas a mano.
 */
public final class CosmeticRenderLayers {
    public static final Identifier CAPE_TEXTURE =
        Identifier.of(TvCosmeticsClient.MOD_ID, "textures/cape_tvlauncher.png");
    public static final Identifier HAT_TEXTURE =
        Identifier.of(TvCosmeticsClient.MOD_ID, "textures/hat_tvlauncher.png");

    private CosmeticRenderLayers() {
    }

    public static RenderLayer cape() {
        return RenderLayers.entityCutout(CAPE_TEXTURE);
    }

    public static RenderLayer hat() {
        return RenderLayers.entityCutout(HAT_TEXTURE);
    }
}
