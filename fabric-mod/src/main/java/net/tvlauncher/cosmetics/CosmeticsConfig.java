package net.tvlauncher.cosmetics;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

/**
 * Configuración que escribe el launcher en {@code config/tvl-cosmetics.json}.
 *
 * <p>Cualquier problema al leer el archivo deja el mod desactivado: es
 * preferible no mostrar cosméticos antes que romper el cliente por un JSON
 * inválido.
 */
public record CosmeticsConfig(
    int schemaVersion,
    boolean enabled,
    String supabaseUrl,
    String supabaseAnonKey,
    String localCapeId,
    String localHatId,
    String localSkinFile,
    String localSkinModel,
    int pollSeconds) {

    public static final int SCHEMA_VERSION = 1;
    public static final int DEFAULT_POLL_SECONDS = 30;
    public static final int MIN_POLL_SECONDS = 5;
    public static final int MAX_POLL_SECONDS = 300;

    /** Constructor de compatibilidad para el contrato anterior (sin skin). */
    public CosmeticsConfig(
            int schemaVersion,
            boolean enabled,
            String supabaseUrl,
            String supabaseAnonKey,
            String localCapeId,
            String localHatId,
            int pollSeconds) {
        this(schemaVersion, enabled, supabaseUrl, supabaseAnonKey,
            localCapeId, localHatId, null, null, pollSeconds);
    }

    public static CosmeticsConfig disabled() {
        return new CosmeticsConfig(
            SCHEMA_VERSION, false, "", "", null, null, null, null,
            DEFAULT_POLL_SECONDS);
    }

    public static CosmeticsConfig load(Path path) {
        if (path == null || !Files.isRegularFile(path)) {
            return disabled();
        }

        try {
            String raw = Files.readString(path, StandardCharsets.UTF_8);
            JsonElement root = JsonParser.parseString(raw);
            if (!root.isJsonObject()) {
                return disabled();
            }

            JsonObject json = root.getAsJsonObject();
            return new CosmeticsConfig(
                readInt(json, "schema_version", SCHEMA_VERSION),
                readBoolean(json, "enabled", false),
                readString(json, "supabase_url", ""),
                readString(json, "supabase_anon_key", ""),
                readNullableString(json, "local_cape_id"),
                readNullableString(json, "local_hat_id"),
                readNullableString(json, "local_skin_file"),
                readNullableString(json, "local_skin_model"),
                clampPollSeconds(readInt(json, "poll_seconds", DEFAULT_POLL_SECONDS)));
        } catch (IOException | RuntimeException e) {
            return disabled();
        }
    }

    public boolean hasRemoteBackend() {
        return !supabaseUrl.isBlank() && !supabaseAnonKey.isBlank();
    }

    static int clampPollSeconds(int value) {
        return Math.max(MIN_POLL_SECONDS, Math.min(MAX_POLL_SECONDS, value));
    }

    private static String readString(JsonObject json, String name, String fallback) {
        JsonElement element = json.get(name);
        if (element == null || !element.isJsonPrimitive()) {
            return fallback;
        }
        return element.getAsString();
    }

    private static String readNullableString(JsonObject json, String name) {
        String value = readString(json, name, "");
        return value.isBlank() ? null : value;
    }

    private static boolean readBoolean(JsonObject json, String name, boolean fallback) {
        JsonElement element = json.get(name);
        if (element == null || !element.isJsonPrimitive()
            || !element.getAsJsonPrimitive().isBoolean()) {
            return fallback;
        }
        return element.getAsBoolean();
    }

    private static int readInt(JsonObject json, String name, int fallback) {
        JsonElement element = json.get(name);
        if (element == null || !element.isJsonPrimitive()
            || !element.getAsJsonPrimitive().isNumber()) {
            return fallback;
        }
        return element.getAsInt();
    }
}
