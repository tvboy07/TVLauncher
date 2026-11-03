package net.tvlauncher.cosmetics;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertTrue;

import net.minecraft.entity.player.PlayerSkinType;
import net.minecraft.entity.player.SkinTextures;
import net.minecraft.util.AssetInfo;
import net.minecraft.util.Identifier;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;

class LocalSkinOverrideTest {
    private static final Identifier ORIGINAL =
        Identifier.of("minecraft", "textures/entity/player/wide/steve");
    private static final Identifier LOCAL_SKIN =
        Identifier.of("tvl-cosmetics", "textures/local_skin");
    private static final Identifier TVL_CAPE =
        Identifier.of("tvl-cosmetics", "textures/cape_tvlauncher");
    private static final Identifier MOJANG_CAPE =
        Identifier.of("minecraft", "textures/entity/cape/mojang");

    @AfterEach
    void tearDown() {
        LocalSkinOverride.clear();
    }

    @Test
    void replacesOnlyTheBodyAndKeepsTheRestOfTheProfile() {
        var original = new SkinTextures(
            new AssetInfo.TextureAssetInfo(ORIGINAL),
            null,
            null,
            PlayerSkinType.WIDE,
            true);

        LocalSkinOverride.set(LOCAL_SKIN, PlayerSkinType.SLIM);
        SkinTextures overridden = LocalSkinOverride.apply(original);

        assertEquals(LOCAL_SKIN, overridden.body().texturePath());
        assertEquals(PlayerSkinType.SLIM, overridden.model());
        assertEquals(original.cape(), overridden.cape());
        assertEquals(original.elytra(), overridden.elytra());
        assertEquals(original.secure(), overridden.secure());
    }

    @Test
    void cachesTheLocalOverrideForTheSameOriginal() {
        var original = new SkinTextures(
            new AssetInfo.TextureAssetInfo(ORIGINAL),
            null,
            null,
            PlayerSkinType.WIDE,
            false);
        LocalSkinOverride.set(LOCAL_SKIN, PlayerSkinType.WIDE);

        SkinTextures first = LocalSkinOverride.applyLocal(original);
        SkinTextures second = LocalSkinOverride.applyLocal(original);

        assertSame(first, second);
    }

    @Test
    void overridesTheCapeWithoutChangingTheBody() {
        var original = new SkinTextures(
            new AssetInfo.TextureAssetInfo(ORIGINAL),
            null,
            null,
            PlayerSkinType.WIDE,
            true);

        SkinTextures overridden = LocalSkinOverride.apply(original, TVL_CAPE);

        assertEquals(original.body(), overridden.body());
        assertEquals(TVL_CAPE, overridden.cape().texturePath());
        assertEquals(original.model(), overridden.model());
        assertEquals(original.secure(), overridden.secure());
    }

    @Test
    void keepsTheMojangCapeAsTheElytraFallback() {
        var mojangCape = new AssetInfo.TextureAssetInfo(MOJANG_CAPE);
        var original = new SkinTextures(
            new AssetInfo.TextureAssetInfo(ORIGINAL),
            mojangCape,
            null,
            PlayerSkinType.WIDE,
            true);

        SkinTextures overridden = LocalSkinOverride.apply(original, TVL_CAPE);

        assertEquals(mojangCape, overridden.elytra());
    }

    @Test
    void keepsAnExplicitElytraTexture() {
        var mojangCape = new AssetInfo.TextureAssetInfo(MOJANG_CAPE);
        var elytra = new AssetInfo.TextureAssetInfo(
            Identifier.of("minecraft", "textures/entity/elytra"));
        var original = new SkinTextures(
            new AssetInfo.TextureAssetInfo(ORIGINAL),
            mojangCape,
            elytra,
            PlayerSkinType.WIDE,
            true);

        SkinTextures overridden = LocalSkinOverride.apply(original, TVL_CAPE);

        assertEquals(elytra, overridden.elytra());
    }

    @Test
    void returnsTheOriginalWhenNoLocalSkinIsLoaded() {
        var original = new SkinTextures(null, null, null, PlayerSkinType.WIDE, false);

        assertSame(original, LocalSkinOverride.apply(original));
        assertTrue(!LocalSkinOverride.isActive());
    }
}
