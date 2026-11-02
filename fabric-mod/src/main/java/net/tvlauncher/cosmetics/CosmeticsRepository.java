package net.tvlauncher.cosmetics;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Collection;
import java.util.HashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Caché de perfiles de cosméticos compartida entre el hilo de red y el de
 * render.
 *
 * <p>Un resultado positivo vive 60 s y uno negativo 30 s. Cuando la API falla
 * se aplica un backoff exponencial (1, 2, 4... hasta 300 s) para no martillear
 * Supabase desde el cliente del jugador.
 */
public final class CosmeticsRepository {
    static final Duration POSITIVE_TTL = Duration.ofSeconds(60);
    static final Duration NEGATIVE_TTL = Duration.ofSeconds(30);
    static final int BATCH_SIZE = 50;
    static final long MAX_BACKOFF_SECONDS = 300;
    private static final int MAX_BACKOFF_EXPONENT = 8;

    private final CosmeticsApi api;
    private final Clock clock;
    private final ConcurrentHashMap<UUID, Entry> cache = new ConcurrentHashMap<>();
    private final AtomicInteger failures = new AtomicInteger();
    private volatile Instant nextRetryAt;

    public CosmeticsRepository(CosmeticsApi api, Clock clock) {
        this.api = api;
        this.clock = clock;
    }

    public Optional<CosmeticProfile> get(UUID uuid) {
        if (uuid == null) {
            return Optional.empty();
        }

        Entry entry = cache.get(uuid);
        if (entry == null || !clock.instant().isBefore(entry.expiresAt())) {
            return Optional.empty();
        }

        return Optional.ofNullable(entry.profile());
    }

    /**
     * Actualiza la caché de los UUID indicados. Solo consulta los que no tienen
     * un resultado fresco y respeta el backoff tras un fallo.
     */
    public void refresh(Collection<UUID> uuids) {
        if (uuids == null || uuids.isEmpty()) {
            return;
        }

        Instant now = clock.instant();
        Instant retryAt = nextRetryAt;
        if (retryAt != null && now.isBefore(retryAt)) {
            return;
        }

        List<UUID> stale = collectStale(uuids, now);
        if (stale.isEmpty()) {
            return;
        }

        for (int start = 0; start < stale.size(); start += BATCH_SIZE) {
            List<UUID> batch = stale.subList(start, Math.min(start + BATCH_SIZE, stale.size()));
            try {
                applySuccess(batch, api.fetch(batch), now);
            } catch (Exception e) {
                applyFailure(batch, now);
            }
        }
    }

    public void clear() {
        cache.clear();
        failures.set(0);
        nextRetryAt = null;
    }

    private List<UUID> collectStale(Collection<UUID> uuids, Instant now) {
        Set<UUID> stale = new LinkedHashSet<>();
        for (UUID uuid : uuids) {
            if (uuid == null) {
                continue;
            }
            Entry entry = cache.get(uuid);
            if (entry == null || !now.isBefore(entry.expiresAt())) {
                stale.add(uuid);
            }
        }
        return new ArrayList<>(stale);
    }

    private void applySuccess(List<UUID> batch, List<CosmeticProfile> profiles, Instant now) {
        failures.set(0);
        nextRetryAt = null;

        Map<UUID, CosmeticProfile> byUuid = new HashMap<>();
        if (profiles != null) {
            for (CosmeticProfile profile : profiles) {
                if (profile == null) {
                    continue;
                }
                if (profile.minecraftUuid() != null) {
                    byUuid.put(profile.minecraftUuid(), profile);
                }
                if (profile.offlineUuid() != null) {
                    byUuid.put(profile.offlineUuid(), profile);
                }
            }
        }

        for (UUID uuid : batch) {
            CosmeticProfile profile = byUuid.get(uuid);
            cache.put(uuid, new Entry(profile, now.plus(
                profile == null ? NEGATIVE_TTL : POSITIVE_TTL)));
        }

        // Un perfil puede traer dos UUIDs distintos de los consultados: se
        // cachea también por el otro para que el jugador se resuelva en
        // servidores online y offline.
        for (Map.Entry<UUID, CosmeticProfile> entry : byUuid.entrySet()) {
            cache.put(entry.getKey(), new Entry(entry.getValue(), now.plus(POSITIVE_TTL)));
        }
    }

    private void applyFailure(List<UUID> batch, Instant now) {
        int count = failures.incrementAndGet();
        long seconds = Math.min(
            MAX_BACKOFF_SECONDS, 1L << Math.min(count, MAX_BACKOFF_EXPONENT));
        Instant retryAt = now.plusSeconds(seconds);
        nextRetryAt = retryAt;

        // El resultado negativo caduca justo cuando termina el backoff para que
        // el siguiente intento no quede bloqueado por la propia caché.
        for (UUID uuid : batch) {
            cache.put(uuid, new Entry(null, retryAt));
        }
    }

    private record Entry(CosmeticProfile profile, Instant expiresAt) {
    }
}
