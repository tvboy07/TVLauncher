package net.tvlauncher.cosmetics;

import java.util.Collection;
import java.util.List;
import java.util.UUID;

/** Lectura de perfiles de cosméticos desde el backend global. */
@FunctionalInterface
public interface CosmeticsApi {
    List<CosmeticProfile> fetch(Collection<UUID> uuids) throws Exception;
}
