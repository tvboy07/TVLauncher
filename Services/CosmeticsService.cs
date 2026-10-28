using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using TVLauncher.Models;

namespace TVLauncher.Services;

public class CosmeticsService
{
    private readonly ConfigService _configService;
    private readonly HttpClient _httpClient;
    private readonly string _cosmeticsConfigPath;
    private readonly string _cosmeticsJarPath;
    private readonly string _cosmeticsSyncPath;
    
    public List<CosmeticItem> AvailableCosmetics { get; private set; }
    public List<string> EquippedCosmetics { get; private set; }
    
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<CosmeticItem>? CosmeticEquipped;

    public CosmeticsService(ConfigService configService)
    {
        _configService = configService;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TVLauncher/1.0.0");
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        
        string cosmeticsPath = _configService.GetCosmeticsPath();
        _cosmeticsConfigPath = Path.Combine(cosmeticsPath, "cosmetics_config.json");
        _cosmeticsJarPath = Path.Combine(cosmeticsPath, "tvl-cosmetics.jar");
        _cosmeticsSyncPath = Path.Combine(cosmeticsPath, "tvl-cosmetics-client.json");
        
        AvailableCosmetics = new List<CosmeticItem>();
        EquippedCosmetics = new List<string>();
        
        LoadCosmeticsConfig();
        Initialize();
    }

    private async void Initialize()
    {
        try
        {
            // Descargar cosmetics jar si no existe y está habilitado
            if (_configService.Config.EnableCosmetics && !File.Exists(_cosmeticsJarPath))
            {
                await DownloadCosmeticsJarAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al inicializar CosmeticsService: {ex.Message}");
        }
    }

    public async Task DownloadCosmeticsJarAsync()
    {
        try
        {
            StatusChanged?.Invoke(this, "Descargando tvl-cosmetics.jar...");
            
            Directory.CreateDirectory(Path.GetDirectoryName(_cosmeticsJarPath)!);
            
            string jarUrl = _configService.Config.CosmeticsJarUrl;
            using var response = await _httpClient.GetAsync(jarUrl);
            response.EnsureSuccessStatusCode();
            
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(_cosmeticsJarPath);
            await stream.CopyToAsync(fileStream);
            
            StatusChanged?.Invoke(this, "tvl-cosmetics.jar descargado correctamente");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error al descargar cosmetics jar: {ex.Message}");
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    public async Task InjectCosmeticsToInstanceAsync(MinecraftInstance instance)
    {
        try
        {
            if (!_configService.Config.EnableCosmetics || !_configService.Config.AutoInjectCosmetics)
                return;

            if (!File.Exists(_cosmeticsJarPath))
            {
                await DownloadCosmeticsJarAsync();
            }

            string modsPath = Path.Combine(instance.GameDirectory, "mods");
            Directory.CreateDirectory(modsPath);
            
            string targetPath = Path.Combine(modsPath, "tvl-cosmetics.jar");
            
            // Copiar jar si no existe o si es más reciente
            if (!File.Exists(targetPath) || 
                File.GetLastWriteTimeUtc(_cosmeticsJarPath) > File.GetLastWriteTimeUtc(targetPath))
            {
                File.Copy(_cosmeticsJarPath, targetPath, true);
                StatusChanged?.Invoke(this, "Cosméticos inyectados en la instancia");
            }
            
            // Sincronización JSON con el mod cliente (Opción C)
            await SyncCosmeticsToInstanceAsync(instance);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error al inyectar cosméticos: {ex.Message}");
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    public async Task SyncCosmeticsToInstanceAsync(MinecraftInstance instance)
    {
        try
        {
            if (!_configService.Config.EnableCosmetics)
                return;

            string configPath = Path.Combine(instance.GameDirectory, "config");
            Directory.CreateDirectory(configPath);
            
            string syncFilePath = Path.Combine(configPath, "tvl-cosmetics-client.json");
            
            var syncData = new CosmeticsSyncData
            {
                Version = "1.0.0",
                EquippedCosmetics = new List<string>(EquippedCosmetics),
                Timestamp = DateTime.UtcNow
            };
            
            string json = JsonConvert.SerializeObject(syncData, Formatting.Indented);
            await File.WriteAllTextAsync(syncFilePath, json);
            
            // También guardar en cache global
            await File.WriteAllTextAsync(_cosmeticsSyncPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al sincronizar cosméticos: {ex.Message}");
        }
    }

    public void EquipCosmetic(string cosmeticId)
    {
        if (!EquippedCosmetics.Contains(cosmeticId))
        {
            EquippedCosmetics.Add(cosmeticId);
            var cosmetic = AvailableCosmetics.Find(c => c.Id == cosmeticId);
            if (cosmetic != null)
            {
                CosmeticEquipped?.Invoke(this, cosmetic);
            }
            SaveCosmeticsConfig();
        }
    }

    public void UnequipCosmetic(string cosmeticId)
    {
        if (EquippedCosmetics.Contains(cosmeticId))
        {
            EquippedCosmetics.Remove(cosmeticId);
            SaveCosmeticsConfig();
        }
    }

    public void LoadCosmeticsConfig()
    {
        try
        {
            if (File.Exists(_cosmeticsConfigPath))
            {
                string json = File.ReadAllText(_cosmeticsConfigPath);
                var config = JsonConvert.DeserializeObject<CosmeticsConfig>(json);
                
                if (config != null)
                {
                    EquippedCosmetics = config.EquippedCosmetics ?? new List<string>();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al cargar config de cosméticos: {ex.Message}");
            EquippedCosmetics = new List<string>();
        }
    }

    public void SaveCosmeticsConfig()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cosmeticsConfigPath)!);
            var config = new CosmeticsConfig
            {
                EquippedCosmetics = EquippedCosmetics
            };
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(_cosmeticsConfigPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al guardar config de cosméticos: {ex.Message}");
        }
    }
}

public class CosmeticItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "cape"; // cape, hat, wings, etc.
    public string TextureUrl { get; set; } = string.Empty;
    public bool IsOwned { get; set; } = false;
    public bool IsPremium { get; set; } = false;
}

public class CosmeticsConfig
{
    public List<string> EquippedCosmetics { get; set; } = new();
}

public class CosmeticsSyncData
{
    public string Version { get; set; } = "1.0.0";
    public List<string> EquippedCosmetics { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
