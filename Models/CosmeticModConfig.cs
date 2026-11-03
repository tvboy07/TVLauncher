using Newtonsoft.Json;

namespace TVLauncher.Models;

/// <summary>
/// Configuración que el launcher escribe en <c>config/tvl-cosmetics.json</c>
/// para que el mod Fabric sepa qué backend consultar y qué cosméticos mostrar
/// al jugador local cuando la cuenta es offline.
///
/// Los nombres de las propiedades JSON son el contrato con el mod; no
/// cambiarlos sin actualizar <c>CosmeticsConfig</c> en el proyecto Fabric.
/// </summary>
public sealed class CosmeticModConfig
{
    [JsonProperty("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("supabase_url")]
    public string SupabaseUrl { get; set; } = string.Empty;

    [JsonProperty("supabase_anon_key")]
    public string SupabaseAnonKey { get; set; } = string.Empty;

    [JsonProperty("local_cape_id")]
    public string? LocalCapeId { get; set; }

    [JsonProperty("local_hat_id")]
    public string? LocalHatId { get; set; }

    /// <summary>PNG de 64x64 copiado a config/; el mod lo usa para el jugador local.</summary>
    [JsonProperty("local_skin_file")]
    public string? LocalSkinFile { get; set; }

    [JsonProperty("local_skin_model")]
    public string? LocalSkinModel { get; set; }

    [JsonProperty("poll_seconds")]
    public int PollSeconds { get; set; } = 30;
}
