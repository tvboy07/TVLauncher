package net.tvlauncher.cosmetics;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;

import java.util.List;
import java.util.UUID;
import org.junit.jupiter.api.Test;

class SupabaseCosmeticsApiTest {
    @Test
    void parsesUuidFieldsAndNullIds() {
        String json = """
            [
              {
                "minecraft_uuid": "11111111-2222-3333-4444-555555555555",
                "offline_uuid": "b50ad385-829d-3141-a216-7e7d7539ba7f",
                "cape_id": "cape_tvlauncher",
                "hat_id": null,
                "updated_at": "2026-10-31T12:00:00+00:00"
              }
            ]
            """;

        List<CosmeticProfile> profiles = SupabaseCosmeticsApi.parse(json);

        CosmeticProfile profile = profiles.getFirst();
        assertEquals(UUID.fromString("11111111-2222-3333-4444-555555555555"), profile.minecraftUuid());
        assertEquals(UUID.fromString("b50ad385-829d-3141-a216-7e7d7539ba7f"), profile.offlineUuid());
        assertEquals("cape_tvlauncher", profile.capeId());
        assertNull(profile.hatId());
    }

    @Test
    void parsesRowsWithMissingUuids() {
        String json = """
            [{ "offline_uuid": "b50ad385-829d-3141-a216-7e7d7539ba7f", "hat_id": "hat_tvlauncher" }]
            """;

        CosmeticProfile profile = SupabaseCosmeticsApi.parse(json).getFirst();

        assertNull(profile.minecraftUuid());
        assertEquals("hat_tvlauncher", profile.hatId());
        assertNull(profile.capeId());
    }

    @Test
    void buildsAQueryWithBothUuidColumns() {
        UUID a = UUID.fromString("11111111-2222-3333-4444-555555555555");
        UUID b = UUID.fromString("b50ad385-829d-3141-a216-7e7d7539ba7f");

        String query = SupabaseCosmeticsApi.buildQuery(List.of(a, b));

        assertEquals(
            "or=(minecraft_uuid.in.(11111111-2222-3333-4444-555555555555,b50ad385-829d-3141-a216-7e7d7539ba7f),"
                + "offline_uuid.in.(11111111-2222-3333-4444-555555555555,b50ad385-829d-3141-a216-7e7d7539ba7f))",
            query);
    }

    @Test
    void buildsTheFullRestUrl() {
        String url = SupabaseCosmeticsApi.buildUrl(
            "https://demo.supabase.co/",
            List.of(UUID.fromString("11111111-2222-3333-4444-555555555555")));

        assertEquals(
            "https://demo.supabase.co/rest/v1/cosmetics_profiles"
                + "?select=minecraft_uuid,offline_uuid,cape_id,hat_id,updated_at"
                + "&or=(minecraft_uuid.in.(11111111-2222-3333-4444-555555555555),"
                + "offline_uuid.in.(11111111-2222-3333-4444-555555555555))",
            url);
    }
}
