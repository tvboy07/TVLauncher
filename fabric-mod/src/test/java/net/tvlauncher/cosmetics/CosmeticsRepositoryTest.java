package net.tvlauncher.cosmetics;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneId;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.Optional;
import java.util.UUID;
import org.junit.jupiter.api.Test;

class CosmeticsRepositoryTest {
    private static final UUID MINECRAFT_UUID =
        UUID.fromString("11111111-2222-3333-4444-555555555555");
    private static final UUID OFFLINE_UUID =
        UUID.fromString("b50ad385-829d-3141-a216-7e7d7539ba7f");

    private final MutableClock clock = new MutableClock();

    @Test
    void positiveResultIsCachedFor60Seconds() {
        var api = new FakeApi(List.of(profile(MINECRAFT_UUID, OFFLINE_UUID, "cape_tvlauncher", null)));
        var repository = new CosmeticsRepository(api, clock);

        repository.refresh(List.of(MINECRAFT_UUID));

        clock.advance(Duration.ofSeconds(59));
        assertTrue(repository.get(MINECRAFT_UUID).isPresent());

        clock.advance(Duration.ofSeconds(2));
        assertFalse(repository.get(MINECRAFT_UUID).isPresent());
    }

    @Test
    void negativeResultIsCachedFor30Seconds() {
        var api = new FakeApi(List.of());
        var repository = new CosmeticsRepository(api, clock);

        repository.refresh(List.of(MINECRAFT_UUID));
        repository.refresh(List.of(MINECRAFT_UUID));
        assertEquals(1, api.calls.size(), "el resultado negativo debe cachearse");

        clock.advance(Duration.ofSeconds(31));
        repository.refresh(List.of(MINECRAFT_UUID));
        assertEquals(2, api.calls.size());
    }

    @Test
    void apiFailureReturnsEmptyAndDoesNotThrow() {
        var api = new FakeApi(List.of());
        api.failure = new IllegalStateException("boom");
        var repository = new CosmeticsRepository(api, clock);

        repository.refresh(List.of(MINECRAFT_UUID));

        assertTrue(repository.get(MINECRAFT_UUID).isEmpty());
    }

    @Test
    void apiFailureBacksOffBeforeRetrying() {
        var api = new FakeApi(List.of());
        api.failure = new IllegalStateException("boom");
        var repository = new CosmeticsRepository(api, clock);

        repository.refresh(List.of(MINECRAFT_UUID));
        assertEquals(1, api.calls.size());

        clock.advance(Duration.ofSeconds(1));
        repository.refresh(List.of(MINECRAFT_UUID));
        assertEquals(1, api.calls.size(), "no debe reintentar durante el backoff");

        clock.advance(Duration.ofSeconds(2));
        repository.refresh(List.of(MINECRAFT_UUID));
        assertEquals(2, api.calls.size(), "debe reintentar cuando expira el backoff");
    }

    @Test
    void successfulRefreshResetsTheBackoff() {
        var api = new FakeApi(List.of());
        api.failure = new IllegalStateException("boom");
        var repository = new CosmeticsRepository(api, clock);

        // Fallo 1 -> backoff 2 s.
        repository.refresh(List.of(MINECRAFT_UUID));
        clock.advance(Duration.ofSeconds(2));

        // Un éxito reinicia el contador de fallos.
        api.failure = null;
        repository.refresh(List.of(OFFLINE_UUID));
        assertEquals(2, api.calls.size());

        // Fallo nuevo: si el contador se reinició, el backoff vuelve a 2 s.
        api.failure = new IllegalStateException("boom");
        repository.refresh(List.of(UUID.randomUUID()));
        clock.advance(Duration.ofSeconds(1));
        repository.refresh(List.of(UUID.randomUUID()));
        assertEquals(3, api.calls.size(), "no debe reintentar durante el backoff");

        clock.advance(Duration.ofSeconds(2));
        repository.refresh(List.of(UUID.randomUUID()));
        assertEquals(4, api.calls.size(), "el backoff debe haber vuelto a 2 s");
    }

    @Test
    void refreshBatchesAtMost50Uuids() {
        var api = new FakeApi(List.of());
        var repository = new CosmeticsRepository(api, clock);
        List<UUID> uuids = new ArrayList<>();
        for (int i = 0; i < 120; i++) {
            uuids.add(UUID.randomUUID());
        }

        repository.refresh(uuids);

        assertEquals(3, api.calls.size());
        assertEquals(50, api.calls.get(0).size());
        assertEquals(50, api.calls.get(1).size());
        assertEquals(20, api.calls.get(2).size());
    }

    @Test
    void matchesByMinecraftUuidAndOfflineUuid() {
        var api = new FakeApi(List.of(profile(MINECRAFT_UUID, OFFLINE_UUID, "cape_tvlauncher", "hat_tvlauncher")));
        var repository = new CosmeticsRepository(api, clock);

        repository.refresh(List.of(MINECRAFT_UUID));

        assertEquals(
            Optional.of("cape_tvlauncher"),
            repository.get(MINECRAFT_UUID).map(CosmeticProfile::capeId));
        assertEquals(
            Optional.of("cape_tvlauncher"),
            repository.get(OFFLINE_UUID).map(CosmeticProfile::capeId));
    }

    @Test
    void clearDropsEverything() {
        var api = new FakeApi(List.of(profile(MINECRAFT_UUID, OFFLINE_UUID, "cape_tvlauncher", null)));
        var repository = new CosmeticsRepository(api, clock);
        repository.refresh(List.of(MINECRAFT_UUID));

        repository.clear();

        assertTrue(repository.get(MINECRAFT_UUID).isEmpty());
    }

    private static CosmeticProfile profile(
        UUID minecraftUuid, UUID offlineUuid, String capeId, String hatId) {
        return new CosmeticProfile(
            minecraftUuid, offlineUuid, capeId, hatId, Instant.parse("2026-10-31T12:00:00Z"));
    }

    private static final class FakeApi implements CosmeticsApi {
        private final List<CosmeticProfile> profiles;
        final List<List<UUID>> calls = new ArrayList<>();
        Exception failure;

        FakeApi(List<CosmeticProfile> profiles) {
            this.profiles = profiles;
        }

        @Override
        public List<CosmeticProfile> fetch(Collection<UUID> uuids) throws Exception {
            calls.add(List.copyOf(uuids));
            if (failure != null) {
                throw failure;
            }
            return profiles;
        }
    }

    private static final class MutableClock extends Clock {
        private Instant now = Instant.parse("2026-10-31T12:00:00Z");

        void advance(Duration duration) {
            now = now.plus(duration);
        }

        @Override
        public ZoneId getZone() {
            return ZoneOffset.UTC;
        }

        @Override
        public Clock withZone(ZoneId zone) {
            return this;
        }

        @Override
        public Instant instant() {
            return now;
        }
    }
}
