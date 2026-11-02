import { createClient } from "npm:@supabase/supabase-js@2";
import { offlineUuid } from "./offline_uuid.ts";

const ID_PATTERN = /^[a-z0-9_]{1,64}$/;

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

/** Devuelve null para un ID ausente, undefined para un ID inválido. */
function readId(value: unknown): string | null | undefined {
  if (value === null || value === undefined) return null;
  if (typeof value !== "string" || !ID_PATTERN.test(value)) return undefined;
  return value;
}

Deno.serve(async (req) => {
  if (req.method !== "POST") {
    return json({ error: "method_not_allowed" }, 405);
  }

  const authorization = req.headers.get("Authorization");
  if (!authorization?.startsWith("Bearer ")) {
    return json({ error: "unauthorized" }, 401);
  }

  const minecraftToken = authorization.slice("Bearer ".length).trim();
  if (!minecraftToken) {
    return json({ error: "unauthorized" }, 401);
  }

  let body: unknown;
  try {
    body = await req.json();
  } catch {
    return json({ error: "invalid_body" }, 400);
  }

  if (body === null || typeof body !== "object" || Array.isArray(body)) {
    return json({ error: "invalid_body" }, 400);
  }

  const payload = body as Record<string, unknown>;
  const capeId = readId(payload.cape_id);
  const hatId = readId(payload.hat_id);

  if (capeId === undefined || hatId === undefined) {
    return json({ error: "invalid_cosmetics" }, 400);
  }

  let profileResponse: Response;
  try {
    profileResponse = await fetch(
      "https://api.minecraftservices.com/minecraft/profile",
      { headers: { Authorization: `Bearer ${minecraftToken}` } },
    );
  } catch {
    return json({ error: "minecraft_unreachable" }, 502);
  }

  if (profileResponse.status === 401 || profileResponse.status === 403) {
    return json({ error: "invalid_minecraft_token" }, 401);
  }

  if (!profileResponse.ok) {
    return json({ error: "minecraft_unreachable" }, 502);
  }

  const profile = await profileResponse.json() as { id?: string; name?: string };
  if (!profile.id || !profile.name) {
    return json({ error: "invalid_minecraft_profile" }, 502);
  }

  const supabaseUrl = Deno.env.get("SUPABASE_URL");
  const serviceRoleKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
  if (!supabaseUrl || !serviceRoleKey) {
    return json({ error: "server_misconfigured" }, 500);
  }

  const supabase = createClient(supabaseUrl, serviceRoleKey, {
    auth: { persistSession: false, autoRefreshToken: false },
  });

  const { error } = await supabase
    .from("cosmetics_profiles")
    .upsert(
      {
        minecraft_uuid: profile.id,
        offline_uuid: offlineUuid(profile.name),
        cape_id: capeId,
        hat_id: hatId,
        updated_at: new Date().toISOString(),
      },
      { onConflict: "minecraft_uuid" },
    );

  if (error) {
    console.error("[publish-cosmetics] upsert failed:", error.message);
    return json({ error: "database_error" }, 500);
  }

  return json({ ok: true });
});
