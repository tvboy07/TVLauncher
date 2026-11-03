package net.tvlauncher.cosmetics;

import net.minecraft.entity.player.PlayerSkinType;
import net.minecraft.entity.player.SkinTextures;
import net.minecraft.util.AssetInfo;
import net.minecraft.util.Identifier;

/**
 * Sustituye la textura del cuerpo del jugador local por la skin equipada en
 * el launcher. El resto del perfil (capa, elytra, modelo) se conserva para no
 * pisar los cosméticos ni la información de Mojang.
 *
 * <p>El resultado local se cachea por identidad del perfil original:
 * {@code AbstractClientPlayerEntity.getSkin()} se llama varias veces por frame
 * y no podemos crear un {@link SkinTextures} nuevo en cada llamada.
 */
public final class LocalSkinOverride {
    private static volatile Identifier texture;
    private static volatile PlayerSkinType model;

    private static volatile SkinTextures cachedLocalOriginal;
    private static volatile SkinTextures cachedLocalResult;
    private static volatile SkinTextures cachedCapeOriginal;
    private static volatile SkinTextures cachedCapeResult;
    private static volatile Identifier cachedCapeTexture;

    private LocalSkinOverride() {
    }

    public static void set(Identifier skinTexture, PlayerSkinType skinModel) {
        texture = skinTexture;
        model = skinModel;
        cachedLocalOriginal = null;
        cachedLocalResult = null;
        cachedCapeOriginal = null;
        cachedCapeResult = null;
        cachedCapeTexture = null;
    }

    public static void clear() {
        texture = null;
        model = null;
        cachedLocalOriginal = null;
        cachedLocalResult = null;
        cachedCapeOriginal = null;
        cachedCapeResult = null;
        cachedCapeTexture = null;
    }

    public static boolean isActive() {
        return texture != null;
    }

    /** Aplica solo la skin del cuerpo local, reutilizando el resultado cacheado. */
    public static SkinTextures applyLocal(SkinTextures original) {
        Identifier currentTexture = texture;
        if (currentTexture == null || original == null) {
            return original;
        }

        if (original == cachedLocalOriginal) {
            return cachedLocalResult;
        }

        PlayerSkinType currentModel = model;
        SkinTextures result = new SkinTextures(
            new AssetInfo.TextureAssetInfo(currentTexture, currentTexture),
            original.cape(),
            original.elytra(),
            currentModel == null ? original.model() : currentModel,
            original.secure());
        cachedLocalOriginal = original;
        cachedLocalResult = result;
        return result;
    }

    /**
     * Aplica solo la capa TVL sobre un perfil ya resuelto (la skin del cuerpo
     * ya puede venir del launcher). Conserva la capa original como fallback de
     * elytra cuando el jugador no tiene textura de elytra propia.
     */
    public static SkinTextures applyCape(SkinTextures original, Identifier capeTexture) {
        if (original == null || capeTexture == null) {
            return original;
        }

        if (original == cachedCapeOriginal && capeTexture == cachedCapeTexture) {
            return cachedCapeResult;
        }

        var elytra = original.elytra() != null ? original.elytra() : original.cape();
        SkinTextures result = new SkinTextures(
            original.body(),
            new AssetInfo.TextureAssetInfo(capeTexture, capeTexture),
            elytra,
            original.model(),
            original.secure());
        cachedCapeOriginal = original;
        cachedCapeTexture = capeTexture;
        cachedCapeResult = result;
        return result;
    }

    /**
     * Aplica la skin local (si hay) y, opcionalmente, una capa TVLauncher.
     * Se conserva para los tests y para código que necesite ambas a la vez.
     */
    public static SkinTextures apply(SkinTextures original, Identifier capeTexture) {
        return applyCape(applyLocal(original), capeTexture);
    }

    public static SkinTextures apply(SkinTextures original) {
        return applyLocal(original);
    }
}
