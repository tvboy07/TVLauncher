package net.tvlauncher.cosmetics;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Clock;
import java.util.Collection;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientLifecycleEvents;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.rendering.v1.LivingEntityFeatureRendererRegistrationCallback;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.network.AbstractClientPlayerEntity;
import net.minecraft.client.render.entity.PlayerEntityRenderer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Punto de entrada del mod de cosméticos.
 *
 * <p>Cada tick reconstruye el mapa {@code entityId → UUID}, recarga la config si
 * cambió en disco y pide a Supabase los perfiles que falten. La descarga corre
 * en un hilo daemon para no bloquear el render, y el resultado se publica en
 * {@link CosmeticRenderState}.
 */
public final class TvCosmeticsClient implements ClientModInitializer {
    public static final String MOD_ID = "tvl-cosmetics";
    public static final Logger LOGGER = LoggerFactory.getLogger(MOD_ID);

    private static final Path CONFIG_PATH =
        FabricLoader.getInstance().getConfigDir().resolve("tvl-cosmetics.json");
    private static final String CONFIG_FILE = "tvl-cosmetics.json";
    private static final long CONFIG_CHECK_INTERVAL_TICKS = 20L;

    private static final AtomicBoolean REFRESH_IN_FLIGHT = new AtomicBoolean();
    private static final ExecutorService REFRESH_EXECUTOR = Executors.newSingleThreadExecutor(runnable -> {
        Thread thread = new Thread(runnable, "TVLauncher cosmetics refresh");
        thread.setDaemon(true);
        return thread;
    });

    private static volatile CosmeticsConfig config = CosmeticsConfig.disabled();
    private static volatile CosmeticsRepository repository = noopRepository();
    private static long configModifiedAt = Long.MIN_VALUE;
    private static long tickCounter;
    private static long lastRefreshTick = Long.MIN_VALUE;
    private static Set<UUID> lastUuids = Set.of();
    private static boolean hadWorld;

    @Override
    public void onInitializeClient() {
        loadConfig();
        registerFeatureRenderer();
        registerTextures();
        registerTickHandler();
        LOGGER.info("TVLauncher Cosmetics initialized (enabled={})", config.enabled());
    }

    private static void registerFeatureRenderer() {
        LivingEntityFeatureRendererRegistrationCallback.EVENT.register(
            (entityType, entityRenderer, registrationHelper, context) -> {
                if (entityRenderer instanceof PlayerEntityRenderer<?> playerRenderer) {
                    registrationHelper.register(new TvCosmeticsFeatureRenderer(playerRenderer));
                }
            });
    }

    private static void registerTextures() {
        ClientLifecycleEvents.CLIENT_STARTED.register(client -> {
            client.getTextureManager().registerTexture(CosmeticRenderLayers.CAPE_TEXTURE);
            client.getTextureManager().registerTexture(CosmeticRenderLayers.HAT_TEXTURE);
        });
        ClientLifecycleEvents.CLIENT_STOPPING.register(client -> REFRESH_EXECUTOR.shutdownNow());
    }

    private static void registerTickHandler() {
        ClientTickEvents.END_CLIENT_TICK.register(TvCosmeticsClient::onEndTick);
    }

    private static void onEndTick(MinecraftClient client) {
        tickCounter++;

        if (client.world == null) {
            if (hadWorld) {
                hadWorld = false;
                PlayerIdentityMap.clear();
                CosmeticRenderState.setLocalUuid(null);
                repository.clear();
                lastUuids = Set.of();
            }
            return;
        }
        hadWorld = true;

        if (tickCounter % CONFIG_CHECK_INTERVAL_TICKS == 0L) {
            reloadConfigIfChanged();
        }

        PlayerIdentityMap.clear();
        Set<UUID> uuids = new HashSet<>();
        for (AbstractClientPlayerEntity player : client.world.getPlayers()) {
            PlayerIdentityMap.put(player.getId(), player.getUuid());
            uuids.add(player.getUuid());
        }
        CosmeticRenderState.setLocalUuid(client.player == null ? null : client.player.getUuid());

        CosmeticsConfig currentConfig = config;
        boolean newPlayers = !uuids.equals(lastUuids);
        lastUuids = uuids;

        long pollTicks = Math.max(1, currentConfig.pollSeconds()) * 20L;
        if (newPlayers || tickCounter - lastRefreshTick >= pollTicks) {
            lastRefreshTick = tickCounter;
            refresh(currentConfig, uuids);
        }
    }

    private static void reloadConfigIfChanged() {
        long modifiedAt = modifiedTime();
        if (modifiedAt == configModifiedAt) {
            return;
        }

        CosmeticsConfig loaded = loadConfigFromDisk(modifiedAt);
        if (loaded.equals(config)) {
            configModifiedAt = modifiedAt;
            return;
        }

        config = loaded;
        configModifiedAt = modifiedAt;
        repository = createRepository(loaded);
        CosmeticRenderState.initialize(repository, loaded);
        lastUuids = Set.of();
        LOGGER.info(
            "Loaded {} (enabled={}, backend={})",
            CONFIG_FILE,
            loaded.enabled(),
            loaded.hasRemoteBackend());
    }

    private static void loadConfig() {
        long modifiedAt = modifiedTime();
        config = loadConfigFromDisk(modifiedAt);
        configModifiedAt = modifiedAt;
        repository = createRepository(config);
        CosmeticRenderState.initialize(repository, config);
    }

    private static CosmeticsConfig loadConfigFromDisk(long modifiedAt) {
        if (modifiedAt < 0L) {
            return CosmeticsConfig.disabled();
        }
        return CosmeticsConfig.load(CONFIG_PATH);
    }

    private static long modifiedTime() {
        try {
            return Files.isRegularFile(CONFIG_PATH)
                ? Files.getLastModifiedTime(CONFIG_PATH).toMillis()
                : -1L;
        } catch (IOException e) {
            return -1L;
        }
    }

    private static CosmeticsRepository createRepository(CosmeticsConfig currentConfig) {
        CosmeticsApi api = currentConfig.hasRemoteBackend()
            ? new SupabaseCosmeticsApi(currentConfig.supabaseUrl(), currentConfig.supabaseAnonKey())
            : uuids -> List.of();
        return new CosmeticsRepository(api, Clock.systemUTC());
    }

    private static CosmeticsRepository noopRepository() {
        return new CosmeticsRepository(uuids -> List.of(), Clock.systemUTC());
    }

    private static void refresh(CosmeticsConfig currentConfig, Collection<UUID> uuids) {
        if (!currentConfig.enabled() || !currentConfig.hasRemoteBackend() || uuids.isEmpty()) {
            return;
        }
        if (!REFRESH_IN_FLIGHT.compareAndSet(false, true)) {
            return;
        }

        CosmeticsRepository currentRepository = repository;
        List<UUID> snapshot = List.copyOf(uuids);
        REFRESH_EXECUTOR.execute(() -> {
            try {
                currentRepository.refresh(snapshot);
            } catch (RuntimeException e) {
                LOGGER.debug("Cosmetics refresh failed", e);
            } finally {
                REFRESH_IN_FLIGHT.set(false);
            }
        });
    }
}
