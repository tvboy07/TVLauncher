package net.tvlauncher.cosmetics;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;

import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class CosmeticsConfigTest {
    @TempDir
    Path dir;

    @Test
    void missingFileReturnsDisabledDefaults() {
        CosmeticsConfig config = CosmeticsConfig.load(dir.resolve("absent.json"));

        assertFalse(config.enabled());
        assertEquals("", config.supabaseUrl());
        assertEquals("", config.supabaseAnonKey());
        assertEquals(30, config.pollSeconds());
        assertEquals(1, config.schemaVersion());
    }

    @Test
    void malformedJsonReturnsDisabledDefaults() throws Exception {
        Path file = dir.resolve("broken.json");
        Files.writeString(file, "{ not json at all");

        CosmeticsConfig config = CosmeticsConfig.load(file);

        assertFalse(config.enabled());
        assertEquals(30, config.pollSeconds());
    }

    @Test
    void pollSecondsIsClampedBetween5And300() throws Exception {
        Path low = dir.resolve("low.json");
        Files.writeString(low, "{\"enabled\": true, \"poll_seconds\": 1}");

        Path high = dir.resolve("high.json");
        Files.writeString(high, "{\"enabled\": true, \"poll_seconds\": 900}");

        assertEquals(5, CosmeticsConfig.load(low).pollSeconds());
        assertEquals(300, CosmeticsConfig.load(high).pollSeconds());
    }

    @Test
    void readsTheFullContract() throws Exception {
        Path file = dir.resolve("full.json");
        Files.writeString(file, """
            {
              "schema_version": 1,
              "enabled": true,
              "supabase_url": "https://demo.supabase.co",
              "supabase_anon_key": "anon-key",
              "local_cape_id": "cape_tvlauncher",
              "local_hat_id": "hat_tvlauncher",
              "poll_seconds": 45
            }
            """);

        CosmeticsConfig config = CosmeticsConfig.load(file);

        assertEquals(true, config.enabled());
        assertEquals("https://demo.supabase.co", config.supabaseUrl());
        assertEquals("anon-key", config.supabaseAnonKey());
        assertEquals("cape_tvlauncher", config.localCapeId());
        assertEquals("hat_tvlauncher", config.localHatId());
        assertEquals(45, config.pollSeconds());
    }

    @Test
    void readsTheLocalSkinContract() throws Exception {
        Path file = dir.resolve("skin.json");
        Files.writeString(file, """
            {
              "schema_version": 1,
              "enabled": true,
              "local_skin_file": "tvl-cosmetics-skin.png",
              "local_skin_model": "classic"
            }
            """);

        CosmeticsConfig config = CosmeticsConfig.load(file);

        assertEquals("tvl-cosmetics-skin.png", config.localSkinFile());
        assertEquals("classic", config.localSkinModel());
    }
}
