using Newtonsoft.Json;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace TVLauncher.Services;

public class SkinService
{
    private readonly HttpClient _httpClient;
    private readonly string _skinCachePath;
    
    // Mojang API endpoints
    private const string MojangProfileApi = "https://api.mojang.com/users/profiles/minecraft";
    private const string MojangSessionApi = "https://sessionserver.mojang.com/session/minecraft/profile";
    
    // Fallback endpoints
    private const string CrafatarSkinApi = "https://crafatar.com/skins";
    private const string CrafatarCapeApi = "https://crafatar.com/capes";
    
    public event EventHandler<string>? StatusChanged;

    public SkinService(ConfigService configService)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TVLauncher/1.0.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        
        _skinCachePath = Path.Combine(configService.GetCachePath(), "skins");
        Directory.CreateDirectory(_skinCachePath);
    }

    public async Task<SkinData?> GetSkinAsync(string username, bool preferMojang = true)
    {
        try
        {
            StatusChanged?.Invoke(this, $"Resolviendo skin para {username}...");
            
            // Intentar obtener UUID
            string? uuid = await GetUuidFromUsernameAsync(username);
            if (string.IsNullOrEmpty(uuid))
            {
                Log.Warn($"[Skin] No se pudo resolver UUID para '{username}'");
                StatusChanged?.Invoke(this, "No se pudo resolver el UUID del usuario");
                return null;
            }
            
            // Estrategia híbrida: intentar SessionServer de Mojang primero si está habilitado
            if (preferMojang)
            {
                var skinData = await TryGetSkinFromMojangSessionAsync(uuid);
                if (skinData != null && skinData.SkinTexture != null)
                {
                    Log.Info($"[Skin] Mojang SessionServer OK ({skinData.SkinTexture.Length} bytes)");
                    StatusChanged?.Invoke(this, "Skin obtenido desde Mojang SessionServer");
                    return skinData;
                }
                Log.Info("[Skin] Mojang SessionServer sin skin, probando Crafatar");
            }
            
            // Fallback a Crafatar
            StatusChanged?.Invoke(this, "Usando Crafatar como fallback...");
            var crafatarSkin = await TryGetSkinFromCrafatarAsync(uuid);
            if (crafatarSkin != null && crafatarSkin.SkinTexture != null)
            {
                Log.Info($"[Skin] Crafatar OK ({crafatarSkin.SkinTexture.Length} bytes)");
                StatusChanged?.Invoke(this, "Skin obtenido desde Crafatar");
                return crafatarSkin;
            }
            
            StatusChanged?.Invoke(this, "No se pudo obtener el skin");
            Log.Warn($"[Skin] Ninguna fuente devolvió skin para '{username}'");
            return null;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error al obtener skin: {ex.Message}");
            Log.Error($"[Skin] Error en GetSkinAsync: {ex}");
            return null;
        }
    }

    private async Task<string?> GetUuidFromUsernameAsync(string username)
    {
        try
        {
            // Verificar cache
            string cacheFile = Path.Combine(_skinCachePath, $"{username.ToLower()}_uuid.txt");
            if (File.Exists(cacheFile))
            {
                string cachedUuid = await File.ReadAllTextAsync(cacheFile);
                if (!string.IsNullOrEmpty(cachedUuid) && (DateTime.Now - File.GetLastWriteTime(cacheFile)).TotalHours < 24)
                {
                    return cachedUuid.Trim();
                }
            }
            
            // Consultar Mojang API
            string response = await _httpClient.GetStringAsync($"{MojangProfileApi}/{username}");
            var profile = JsonConvert.DeserializeObject<MojangProfile>(response);
            
            if (profile != null && !string.IsNullOrEmpty(profile.Id))
            {
                // Guardar en cache
                await File.WriteAllTextAsync(cacheFile, profile.Id);
                return profile.Id;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al obtener UUID: {ex.Message}");
            return null;
        }
    }

    private async Task<SkinData?> TryGetSkinFromMojangSessionAsync(string uuid)
    {
        try
        {
            string response = await _httpClient.GetStringAsync($"{MojangSessionApi}/{uuid}");
            var sessionProfile = JsonConvert.DeserializeObject<MojangSessionProfile>(response);
            
            if (sessionProfile?.Properties != null)
            {
                foreach (var prop in sessionProfile.Properties)
                {
                    if (prop.Name == "textures")
                    {
                        // Decodificar base64
                        byte[] data = Convert.FromBase64String(prop.Value);
                        string decoded = Encoding.UTF8.GetString(data);
                        var textures = JsonConvert.DeserializeObject<TexturesResponse>(decoded);
                        
                        if (textures?.Textures?.Skin?.Url != null)
                        {
                            // Descargar textura
                            byte[] skinBytes = await _httpClient.GetByteArrayAsync(textures.Textures.Skin.Url);
                            string model = textures.Textures.Skin.Metadata?.Model ?? "classic";
                            
                            // Guardar en cache
                            string skinCacheFile = Path.Combine(_skinCachePath, $"{uuid}.png");
                            await File.WriteAllBytesAsync(skinCacheFile, skinBytes);

                            // Capa (cape) del jugador: textures.CAPE.url
                            string? capeUrl = textures.Textures.Cape?.Url;
                            byte[]? capeBytes = null;
                            if (!string.IsNullOrWhiteSpace(capeUrl))
                            {
                                capeBytes = await TryDownloadCapeAsync(uuid, capeUrl);
                            }
                            
                            return new SkinData
                            {
                                Uuid = uuid,
                                SkinTexture = skinBytes,
                                CapeTexture = capeBytes,
                                CapeUrl = capeUrl,
                                Model = model,
                                Source = "MojangSessionServer"
                            };
                        }
                    }
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en Mojang SessionServer: {ex.Message}");
            return null;
        }
    }

    private async Task<SkinData?> TryGetSkinFromCrafatarAsync(string uuid)
    {
        try
        {
            // Verificar cache primero
            string skinCacheFile = Path.Combine(_skinCachePath, $"{uuid}_crafatar.png");
            if (File.Exists(skinCacheFile) && (DateTime.Now - File.GetLastWriteTime(skinCacheFile)).TotalHours < 1)
            {
                byte[] cachedSkin = await File.ReadAllBytesAsync(skinCacheFile);
                return new SkinData
                {
                    Uuid = uuid,
                    SkinTexture = cachedSkin,
                    Model = "classic",
                    Source = "Crafatar"
                };
            }
            
            // Descargar desde Crafatar
            string url = $"{CrafatarSkinApi}/{uuid}.png?size=64";
            byte[] skinBytes = await _httpClient.GetByteArrayAsync(url);
            
            // Guardar en cache
            await File.WriteAllBytesAsync(skinCacheFile, skinBytes);

            // Crafatar también expone la capa del jugador (404 si no tiene)
            byte[]? capeBytes = await TryDownloadCapeAsync(uuid, $"{CrafatarCapeApi}/{uuid}.png?size=64");
            
            return new SkinData
            {
                Uuid = uuid,
                SkinTexture = skinBytes,
                CapeTexture = capeBytes,
                Model = "classic",
                Source = "Crafatar"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en Crafatar: {ex.Message}");
            return null;
        }
    }

    public byte[]? GetCachedSkin(string uuid)
    {
        try
        {
            string[] possibleFiles = {
                Path.Combine(_skinCachePath, $"{uuid}.png"),
                Path.Combine(_skinCachePath, $"{uuid}_crafatar.png")
            };
            
            foreach (var file in possibleFiles)
            {
                if (File.Exists(file))
                {
                    return File.ReadAllBytes(file);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al obtener skin cacheado: {ex.Message}");
        }
        
        return null;
    }

    /// <summary>Descarga (o reutiliza la caché de) la textura de la capa del jugador.</summary>
    private async Task<byte[]?> TryDownloadCapeAsync(string uuid, string capeUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(capeUrl))
                return GetCachedCape(uuid);

            byte[] capeBytes = await _httpClient.GetByteArrayAsync(capeUrl);
            if (capeBytes.Length == 0)
                return GetCachedCape(uuid);

            await File.WriteAllBytesAsync(Path.Combine(_skinCachePath, $"{uuid}_cape.png"), capeBytes);
            Log.Info($"[Skin] Capa descargada ({capeBytes.Length} bytes)");
            return capeBytes;
        }
        catch (Exception ex)
        {
            // Un 404 significa simplemente que el jugador no tiene capa
            Log.Warn($"[Skin] No se pudo descargar la capa: {ex.Message}");
            return GetCachedCape(uuid);
        }
    }

    /// <summary>Capa cacheada en disco para el UUID indicado (o null si no existe).</summary>
    public byte[]? GetCachedCape(string uuid)
    {
        try
        {
            string capeFile = Path.Combine(_skinCachePath, $"{uuid}_cape.png");
            if (File.Exists(capeFile))
            {
                byte[] bytes = File.ReadAllBytes(capeFile);
                return bytes.Length > 0 ? bytes : null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al obtener capa cacheada: {ex.Message}");
        }

        return null;
    }

    /// <summary>Descarga una textura arbitraria (por ejemplo una capa de TVLauncher).</summary>
    public async Task<byte[]?> DownloadTextureAsync(string url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            byte[] bytes = await _httpClient.GetByteArrayAsync(url);
            return bytes.Length > 0 ? bytes : null;
        }
        catch (Exception ex)
        {
            Log.Warn($"[Skin] No se pudo descargar la textura '{url}': {ex.Message}");
            return null;
        }
    }
}

public class SkinData
{
    public string Uuid { get; set; } = string.Empty;
    public byte[]? SkinTexture { get; set; }
    public byte[]? CapeTexture { get; set; }
    public string? CapeUrl { get; set; }
    public string Model { get; set; } = "classic"; // classic o slim
    public string Source { get; set; } = string.Empty;
}

public class MojangProfile
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
}

public class MojangSessionProfile
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonProperty("properties")]
    public MojangProperty[] Properties { get; set; } = Array.Empty<MojangProperty>();
}

public class MojangProperty
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonProperty("value")]
    public string Value { get; set; } = string.Empty;
}

public class TexturesResponse
{
    [JsonProperty("textures")]
    public TexturesContainer Textures { get; set; } = new();
}

public class TexturesContainer
{
    [JsonProperty("SKIN")]
    public SkinTexture Skin { get; set; } = new();
    
    [JsonProperty("CAPE")]
    public CapeTexture? Cape { get; set; }
}

public class SkinTexture
{
    [JsonProperty("url")]
    public string Url { get; set; } = string.Empty;
    
    [JsonProperty("metadata")]
    public SkinMetadata? Metadata { get; set; }
}

public class CapeTexture
{
    [JsonProperty("url")]
    public string Url { get; set; } = string.Empty;
}

public class SkinMetadata
{
    [JsonProperty("model")]
    public string Model { get; set; } = "classic";
}
