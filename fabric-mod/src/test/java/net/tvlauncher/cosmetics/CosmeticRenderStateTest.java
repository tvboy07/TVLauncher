package net.tvlauncher.cosmetics;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.time.Clock;
import java.time.Instant;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;

class CosmeticRenderStateTest {
    private static final UUID CAPE_PLAYER =
        UUID.fromString("11111111-2222-3333-4444-555555555555");
    private static final UUID UNKNOWN_CAPE_PLAYER =
        UUID.fromString("22222222-3333-4444-5555-666666666666");
    private static final UUID HAT_PLAYER =
        UUID.fromString("33333333-4444-5555-6666-777777777777");

    @AfterEach
    void tearDown() {
        CosmeticRenderState.clearForTests();
        PlayerIdentityMap.clear();
    }

    @Test
    void hasCustomCapeIsTrueOnlyForCapeTvlauncher() {
        CosmeticRenderState.initialize(
            repositoryWith(
                profile(CAPE_PLAYER, "cape_tvlauncher", null),
                profile(UNKNOWN_CAPE_PLAYER, "cape_1", null)),
            config());

        PlayerIdentityMap.put(1, CAPE_PLAYER);
        PlayerIdentityMap.put(2, UNKNOWN_CAPE_PLAYER);

        assertTrue(CosmeticRenderState.hasCustomCape(1));
        assertFalse(CosmeticRenderState.hasCustomCape(2));
    }

    @Test
    void hasCustomCapeIsFalseForHatOnly() {
        CosmeticRenderState.initialize(
            repositoryWith(profile(HAT_PLAYER, null, "hat_tvlauncher")),
            config());

        PlayerIdentityMap.put(3, HAT_PLAYER);

        assertFalse(CosmeticRenderState.hasCustomCape(3));
        assertEquals(
            Optional.of(new ResolvedCosmetics(null, "hat_tvlauncher")),
            CosmeticRenderState.resolve(3));
    }

    @Test
    void renderFlagsAvoidResolvingProfiles() {
        CosmeticRenderState.initialize(repositoryWith(), config());
        CosmeticRenderState.setRenderFlags(Map.of(
            5, new CosmeticRenderState.RenderFlags(true, true, true)));

        assertTrue(CosmeticRenderState.hasCustomCape(5));
        assertTrue(CosmeticRenderState.hasHat(5));
        assertTrue(CosmeticRenderState.isLocalPlayer(5));
        assertFalse(CosmeticRenderState.hasCustomCape(5 + 1));
    }

    @Test
    void isLocalPlayerMatchesTheLocalUuid() {
        CosmeticRenderState.initialize(repositoryWith(), config());
        CosmeticRenderState.setLocalUuid(CAPE_PLAYER);
        PlayerIdentityMap.put(7, CAPE_PLAYER);
        PlayerIdentityMap.put(8, HAT_PLAYER);

        assertTrue(CosmeticRenderState.isLocalPlayer(7));
        assertFalse(CosmeticRenderState.isLocalPlayer(8));
        assertFalse(CosmeticRenderState.isLocalPlayer(999));
    }

    @Test
    void resolveReturnsEmptyForUnknownEntityId() {
        CosmeticRenderState.initialize(repositoryWith(), config());

        assertTrue(CosmeticRenderState.resolve(999).isEmpty());
        assertFalse(CosmeticRenderState.hasCustomCape(999));
    }

    @Test
    void localFallbackRendersTheLocalPlayerWithoutBackendData() {
        CosmeticRenderState.initialize(
            repositoryWith(),
            new CosmeticsConfig(
                1, true, "https://demo.supabase.co", "anon-key",
                "cape_tvlauncher", "hat_tvlauncher", 30));
        CosmeticRenderState.setLocalUuid(CAPE_PLAYER);
        PlayerIdentityMap.put(7, CAPE_PLAYER);

        assertTrue(CosmeticRenderState.hasCustomCape(7));
        assertEquals(
            Optional.of(new ResolvedCosmetics("cape_tvlauncher", "hat_tvlauncher")),
            CosmeticRenderState.resolve(7));
    }

    private static CosmeticsRepository repositoryWith(CosmeticProfile... profiles) {
        List<CosmeticProfile> list = List.of(profiles);
        var repository = new CosmeticsRepository(uuids -> list, Clock.systemUTC());
        if (profiles.length > 0) {
            repository.refresh(List.of(profiles[0].minecraftUuid()));
        }
        return repository;
    }

    private static CosmeticProfile profile(UUID uuid, String capeId, String hatId) {
        return new CosmeticProfile(uuid, null, capeId, hatId, Instant.now());
    }

    private static CosmeticsConfig config() {
        return new CosmeticsConfig(1, true, "https://demo.supabase.co", "anon-key", null, null, 30);
    }
}
