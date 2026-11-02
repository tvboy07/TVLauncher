package net.tvlauncher.cosmetics;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.time.Clock;
import java.time.Instant;
import java.util.Collection;
import java.util.List;
import java.util.Optional;
import java.util.UUID;
import org.junit.jupiter.api.Test;

class CosmeticRenderResolverTest {
    private static final UUID PLAYER = UUID.fromString("11111111-2222-3333-4444-555555555555");

    private final CosmeticsRepository emptyRepository =
        new CosmeticsRepository(uuids -> List.of(), Clock.systemUTC());

    @Test
    void remoteProfileWinsOverLocalFallback() {
        var repository = repositoryWith(
            new CosmeticProfile(PLAYER, null, "cape_tvlauncher", null, Instant.now()));

        var resolved = CosmeticRenderResolver.resolve(
            PLAYER, true, config("hat_tvlauncher", null), repository);

        assertEquals(Optional.of(new ResolvedCosmetics("cape_tvlauncher", null)), resolved);
    }

    @Test
    void localFallbackAppliesOnlyToTheLocalPlayer() {
        var config = config("cape_tvlauncher", "hat_tvlauncher");

        assertEquals(
            Optional.of(new ResolvedCosmetics("cape_tvlauncher", "hat_tvlauncher")),
            CosmeticRenderResolver.resolve(PLAYER, true, config, emptyRepository));
        assertTrue(
            CosmeticRenderResolver.resolve(PLAYER, false, config, emptyRepository).isEmpty());
    }

    @Test
    void unknownIdsAreIgnored() {
        var config = config("cape_1", "hat_1");

        assertTrue(
            CosmeticRenderResolver.resolve(PLAYER, true, config, emptyRepository).isEmpty());
    }

    @Test
    void disabledConfigReturnsEmpty() {
        var config = new CosmeticsConfig(
            1, false, "https://demo.supabase.co", "anon-key",
            "cape_tvlauncher", "hat_tvlauncher", 30);
        var repository = repositoryWith(
            new CosmeticProfile(PLAYER, null, "cape_tvlauncher", "hat_tvlauncher", Instant.now()));

        assertTrue(CosmeticRenderResolver.resolve(PLAYER, true, config, repository).isEmpty());
    }

    @Test
    void remoteProfileWithOnlyUnknownIdsDoesNotFallBackToLocal() {
        var repository = repositoryWith(
            new CosmeticProfile(PLAYER, null, "cape_1", "hat_1", Instant.now()));

        assertTrue(
            CosmeticRenderResolver.resolve(
                PLAYER, true, config("cape_tvlauncher", null), repository).isEmpty());
    }

    private static CosmeticsRepository repositoryWith(CosmeticProfile profile) {
        var api = (CosmeticsApi) uuids -> List.of(profile);
        var repository = new CosmeticsRepository(api, Clock.systemUTC());
        repository.refresh(List.of(PLAYER));
        return repository;
    }

    private static CosmeticsConfig config(String capeId, String hatId) {
        return new CosmeticsConfig(
            1, true, "https://demo.supabase.co", "anon-key", capeId, hatId, 30);
    }
}
