using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TVLauncher.Models;

namespace TVLauncher.Services;

public class CosmeticsService
{
    private const string ModResourceName = "TVLauncher.Resources.tvl-cosmetics.jar";

    private readonly ConfigService _configService;
    private readonly ICosmeticsPublisher _publisher;
    private readonly SemaphoreSlim _modJarLock = new(1, 1);
    private readonly string _cosmeticsConfigPath;
    private readonly string _cosmeticsJarPath;
    private readonly string _cosmeticsSyncPath;
    
    public List<CosmeticItem> AvailableCosmetics { get; private set; }
    public List<string> EquippedCosmetics { get; private set; }
    
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<CosmeticItem>? CosmeticEquipped;

    public CosmeticsService(ConfigService configService, ICosmeticsPublisher publisher)
    {
        _configService = configService;
        _publisher = publisher;
        
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
            if (!_configService.Config.EnableCosmetics)
                return;

            if (!await EnsureModJarAsync())
                Console.WriteLine("No se pudo preparar el jar de cosméticos embebido");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al inicializar CosmeticsService: {ex.Message}");
        }
    }

    /// <summary>
    /// Extrae el jar embebido a la carpeta de cosméticos del launcher.
    /// </summary>
    /// <returns><c>true</c> si el jar quedó disponible en disco.</returns>
    internal async Task<bool> EnsureModJarAsync()
    {
        await _modJarLock.WaitAsync();
        try
        {
            using Stream? resource = typeof(CosmeticsService).Assembly
                .GetManifestResourceStream(ModResourceName);
            if (resource is null)
            {
                Console.WriteLine($"No se encontró el recurso embebido {ModResourceName}");
                return false;
            }

            using var buffer = new MemoryStream();
            await resource.CopyToAsync(buffer);
            byte[] bytes = buffer.ToArray();
            if (bytes.Length == 0)
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(_cosmeticsJarPath)!);

            if (FileMatches(_cosmeticsJarPath, bytes))
                return true;

            string tempPath = _cosmeticsJarPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(tempPath, bytes);
                File.Move(tempPath, _cosmeticsJarPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al extraer tvl-cosmetics.jar: {ex.Message}");
            return false;
        }
        finally
        {
            _modJarLock.Release();
        }
    }

    public async Task InjectCosmeticsToInstanceAsync(MinecraftInstance instance)
    {
        try
        {
            if (!_configService.Config.EnableCosmetics || !_configService.Config.AutoInjectCosmetics)
                return;

            if (!await EnsureModJarAsync())
            {
                StatusChanged?.Invoke(this, "No se pudo preparar el mod de cosméticos");
                return;
            }

            string modsPath = Path.Combine(instance.GameDirectory, "mods");
            Directory.CreateDirectory(modsPath);
            
            string targetPath = Path.Combine(modsPath, "tvl-cosmetics.jar");
            
            // Copiar el jar solo si el contenido cambió (no por fecha: el jar
            // embebido conserva la fecha de compilación y podría ser más viejo).
            if (!FilesMatch(_cosmeticsJarPath, targetPath))
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

    private static bool FileMatches(string path, byte[] bytes)
    {
        if (!File.Exists(path))
            return false;

        return string.Equals(
            ComputeHash(path),
            Convert.ToHexString(SHA256.HashData(bytes)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool FilesMatch(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath) || !File.Exists(targetPath))
            return false;

        return string.Equals(
            ComputeHash(sourcePath),
            ComputeHash(targetPath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeHash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

public partial class CosmeticItem : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "cape"; // cape, hat, wings, etc.
    public string TextureUrl { get; set; } = string.Empty;
    public bool IsOwned { get; set; } = false;
    public bool IsPremium { get; set; } = false;

    /// <summary>El cosmético está equipado actualmente (muestra el botón Quitar).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEquip))]
    private bool _isEquipped;

    /// <summary>Se puede equipar: es propiedad del usuario y no está ya equipado.</summary>
    public bool CanEquip => IsOwned && !IsEquipped;
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
