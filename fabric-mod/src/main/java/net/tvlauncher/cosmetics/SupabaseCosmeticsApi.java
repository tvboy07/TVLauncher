package net.tvlauncher.cosmetics;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.UUID;

/**
 * Cliente REST de Supabase para leer {@code cosmetics_profiles}.
 *
 * <p>Solo usa la anon key pública: la tabla tiene una política de lectura
 * pública y ninguna de escritura, así que el mod jamás puede publicar.
 */
public final class SupabaseCosmeticsApi implements CosmeticsApi {
    private static final String TABLE = "cosmetics_profiles";
    private static final String COLUMNS = "minecraft_uuid,offline_uuid,cape_id,hat_id,updated_at";
    private static final Duration TIMEOUT = Duration.ofSeconds(10);

    private final String supabaseUrl;
    private final String anonKey;
    private final HttpClient httpClient;

    public SupabaseCosmeticsApi(String supabaseUrl, String anonKey) {
        this(supabaseUrl, anonKey, HttpClient.newBuilder()
            .connectTimeout(TIMEOUT)
            .build());
    }

    SupabaseCosmeticsApi(String supabaseUrl, String anonKey, HttpClient httpClient) {
        this.supabaseUrl = supabaseUrl == null ? "" : supabaseUrl.trim();
        this.anonKey = anonKey == null ? "" : anonKey.trim();
        this.httpClient = httpClient;
    }

    @Override
    public List<CosmeticProfile> fetch(Collection<UUID> uuids) throws Exception {
        if (uuids == null || uuids.isEmpty()) {
            return List.of();
        }

        HttpRequest request = HttpRequest.newBuilder(URI.create(buildUrl(supabaseUrl, uuids)))
            .timeout(TIMEOUT)
            .header("apikey", anonKey)
            .header("Authorization", "Bearer " + anonKey)
            .header("Accept", "application/json")
            .GET()
            .build();

        HttpResponse<String> response =
            httpClient.send(request, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));

        if (response.statusCode() < 200 || response.statusCode() >= 300) {
            throw new IllegalStateException(
                "Supabase respondió " + response.statusCode() + " al leer cosméticos");
        }

        return parse(response.body());
    }

    static String buildUrl(String baseUrl, Collection<UUID> uuids) {
        String base = baseUrl == null ? "" : baseUrl.trim();
        while (base.endsWith("/")) {
            base = base.substring(0, base.length() - 1);
        }
        return base + "/rest/v1/" + TABLE + "?select=" + COLUMNS + "&" + buildQuery(uuids);
    }

    static String buildQuery(Collection<UUID> uuids) {
        String list = joinUuids(uuids);
        return "or=(minecraft_uuid.in.(" + list + "),offline_uuid.in.(" + list + "))";
    }

    static List<CosmeticProfile> parse(String json) {
        List<CosmeticProfile> profiles = new ArrayList<>();
        if (json == null || json.isBlank()) {
            return profiles;
        }

        JsonElement root = JsonParser.parseString(json);
        if (!root.isJsonArray()) {
            return profiles;
        }

        JsonArray array = root.getAsJsonArray();
        for (JsonElement element : array) {
            if (!element.isJsonObject()) {
                continue;
            }
            JsonObject row = element.getAsJsonObject();
            profiles.add(new CosmeticProfile(
                readUuid(row, "minecraft_uuid"),
                readUuid(row, "offline_uuid"),
                readNullableString(row, "cape_id"),
                readNullableString(row, "hat_id"),
                readInstant(row, "updated_at")));
        }

        return profiles;
    }

    private static String joinUuids(Collection<UUID> uuids) {
        StringBuilder builder = new StringBuilder();
        for (UUID uuid : uuids) {
            if (uuid == null) {
                continue;
            }
            if (builder.length() > 0) {
                builder.append(',');
            }
            builder.append(uuid);
        }
        return builder.toString();
    }

    private static UUID readUuid(JsonObject row, String name) {
        String value = readNullableString(row, name);
        if (value == null) {
            return null;
        }
        try {
            return UUID.fromString(value);
        } catch (IllegalArgumentException e) {
            return null;
        }
    }

    private static Instant readInstant(JsonObject row, String name) {
        String value = readNullableString(row, name);
        if (value == null) {
            return null;
        }
        try {
            return Instant.parse(value);
        } catch (RuntimeException e) {
            return null;
        }
    }

    private static String readNullableString(JsonObject row, String name) {
        JsonElement element = row.get(name);
        if (element == null || element.isJsonNull() || !element.isJsonPrimitive()) {
            return null;
        }
        String value = element.getAsString();
        return value.isBlank() ? null : value;
    }
}
