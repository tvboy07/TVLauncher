import SparkMD5 from "npm:spark-md5@3.0.2";

/**
 * UUID offline estándar de Minecraft: MD5("OfflinePlayer:" + nombre),
 * con versión 3 y variante RFC 4122, formateado en big-endian.
 */
export function offlineUuid(name: string): string {
  const hex = SparkMD5.hash("OfflinePlayer:" + name);
  const bytes = new Uint8Array(16);

  for (let i = 0; i < 16; i++) {
    bytes[i] = Number.parseInt(hex.slice(i * 2, i * 2 + 2), 16);
  }

  bytes[6] = (bytes[6] & 0x0f) | 0x30;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;

  const out = Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
  return `${out.slice(0, 8)}-${out.slice(8, 12)}-${out.slice(12, 16)}-${out.slice(16, 20)}-${out.slice(20)}`;
}
