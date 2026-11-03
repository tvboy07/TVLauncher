package net.tvlauncher.cosmetics;

import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Relaciona el id de entidad de Minecraft con el UUID del jugador.
 *
 * <p>El render state de 1.21.11 solo expone el id numérico de la entidad, no su
 * UUID, así que el cliente reconstruye este mapa cada tick a partir de
 * {@code client.world.getPlayers()}. Es la única forma de saber de quién es
 * cada cosmético sin tocar el servidor.
 */
public final class PlayerIdentityMap {
    private static final ConcurrentHashMap<Integer, UUID> UUID_BY_ENTITY_ID =
        new ConcurrentHashMap<>();

    private PlayerIdentityMap() {
    }

    public static void put(int entityId, UUID uuid) {
        if (uuid == null) {
            UUID_BY_ENTITY_ID.remove(entityId);
        } else {
            UUID_BY_ENTITY_ID.put(entityId, uuid);
        }
    }

    /** UUID del jugador, o {@code null} si la entidad no es un jugador conocido. */
    public static UUID get(int entityId) {
        return UUID_BY_ENTITY_ID.get(entityId);
    }

    /** Copia de los UUID de los jugadores visibles en este tick. */
    public static Set<UUID> uuids() {
        return Set.copyOf(UUID_BY_ENTITY_ID.values());
    }

    public static void clear() {
        UUID_BY_ENTITY_ID.clear();
    }
}
