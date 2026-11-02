package net.tvlauncher.cosmetics;

import net.fabricmc.api.ClientModInitializer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public final class TvCosmeticsClient implements ClientModInitializer {
    public static final String MOD_ID = "tvl-cosmetics";
    public static final Logger LOGGER = LoggerFactory.getLogger(MOD_ID);

    @Override
    public void onInitializeClient() {
        LOGGER.info("TVLauncher Cosmetics initialized");
    }
}
