using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private readonly object _publishLock = new();
    private readonly string _cosmeticsConfigPath;
    private readonly string _cosmeticsJarPath;
    private readonly string _cosmeticsSyncPath;
    private CancellationTokenSource? _publishCts;

    /// <summary>
    /// Espera antes de publicar un cambio de cosméticos. Evita un POST por
    /// cada clic cuando el jugador equipa y desequipa varias piezas seguidas.
    /// </summary>
    internal TimeSpan PublishDebounce { get; set; } = TimeSpan.FromMilliseconds(500);
    
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
        _cosmeticsSyncPath = Path.Combine(cosmeticsPath, "tvl-cosmetics.json");
        
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

            // El mod debe encontrar su configuración ya escrita cuando arranque:
            // publicamos el estado actual y lo sincronizamos antes de copiar el jar.
            await PublishBeforeLaunchAsync();
            await SyncCosmeticsToInstanceAsync(instance);

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
            string configPath = Path.Combine(instance.GameDirectory, "config");
            Directory.CreateDirectory(configPath);

            var modConfig = new CosmeticModConfig
            {
                SchemaVersion = 1,
                Enabled = _configService.Config.EnableCosmetics,
                SupabaseUrl = _configService.Config.CosmeticsSupabaseUrl,
                SupabaseAnonKey = _configService.Config.CosmeticsSupabaseAnonKey,
                LocalCapeId = _configService.Config.EquippedCapeId,
                LocalHatId = _configService.Config.EquippedHatId,
                PollSeconds = 30
            };

            string json = JsonConvert.SerializeObject(modConfig, Formatting.Indented);
            await File.WriteAllTextAsync(
                Path.Combine(configPath, "tvl-cosmetics.json"),
                json);

            // También guardar una copia en la cache global del launcher para
            // diagnóstico y para reutilizarla si la instancia se recrea.
            Directory.CreateDirectory(Path.GetDirectoryName(_cosmeticsSyncPath)!);
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
            SchedulePublish();
        }
    }

    public void UnequipCosmetic(string cosmeticId)
    {
        if (EquippedCosmetics.Contains(cosmeticId))
        {
            EquippedCosmetics.Remove(cosmeticId);
            SaveCosmeticsConfig();
            SchedulePublish();
        }
    }

    public void LoadCosmeticsConfig()
    {
        var loaded = new List<string>();
        try
        {
            if (File.Exists(_cosmeticsConfigPath))
            {
                string json = File.ReadAllText(_cosmeticsConfigPath);
                var config = JsonConvert.DeserializeObject<CosmeticsConfig>(json);

                if (config != null)
                {
                    loaded = config.EquippedCosmetics ?? new List<string>();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al cargar config de cosméticos: {ex.Message}");
            loaded = new List<string>();
        }

        // Limpiar IDs de una versión anterior del catálogo y añadir lo que el
        // jugador tenga equipado en AppConfig. Así el armario, el JSON del mod
        // y la publicación a Supabase parten siempre de la misma lista.
        var cleaned = new List<string>();
        foreach (string id in loaded)
        {
            if (string.IsNullOrWhiteSpace(id) || IsObsoleteCosmeticId(id) || cleaned.Contains(id))
                continue;

            cleaned.Add(id);
        }

        AddIfMissing(cleaned, _configService.Config.EquippedCapeId);
        AddIfMissing(cleaned, _configService.Config.EquippedHatId);

        bool changed = !loaded.SequenceEqual(cleaned);
        EquippedCosmetics = cleaned;

        if (changed)
            SaveCosmeticsConfig();
    }

    private static bool IsObsoleteCosmeticId(string id)
    {
        return id == "cape_1" || id == "hat_1";
    }

    private static void AddIfMissing(List<string> ids, string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id))
            ids.Add(id);
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

    private void SchedulePublish()
    {
        var cts = new CancellationTokenSource();
        lock (_publishLock)
        {
            _publishCts?.Cancel();
            _publishCts?.Dispose();
            _publishCts = cts;
        }

        _ = PublishAfterDebounceAsync(cts);
    }

    private async Task PublishAfterDebounceAsync(CancellationTokenSource cts)
    {
        try
        {
            if (PublishDebounce > TimeSpan.Zero)
                await Task.Delay(PublishDebounce, cts.Token);
            else
                await Task.Yield();

            CancellationToken token = cts.Token;
            PublishCosmeticsResult result = await _publisher.PublishCurrentAsync(token);

            // El publisher convierte la cancelación en Failed; si un cambio
            // posterior ya reemplazó esta publicación, no la reportamos como error.
            if (token.IsCancellationRequested)
                return;

            ReportPublishResult(result);
        }
        catch (OperationCanceledException)
        {
            // Un cambio posterior reemplazó esta publicación; no es un error.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al publicar cosméticos: {ex.Message}");
        }
        finally
        {
            lock (_publishLock)
            {
                if (ReferenceEquals(_publishCts, cts))
                    _publishCts = null;
            }

            cts.Dispose();
        }
    }

    private async Task PublishBeforeLaunchAsync()
    {
        try
        {
            PublishCosmeticsResult result = await _publisher.PublishCurrentAsync();
            ReportPublishResult(result);
        }
        catch (Exception ex)
        {
            // Un fallo de red nunca debe impedir que el juego arranque.
            Console.WriteLine($"Error al publicar cosméticos antes de lanzar: {ex.Message}");
        }
    }

    private void ReportPublishResult(PublishCosmeticsResult result)
    {
        switch (result.Status)
        {
            case PublishCosmeticsStatus.Published:
                Console.WriteLine("Cosméticos publicados");
                break;

            case PublishCosmeticsStatus.Failed:
                StatusChanged?.Invoke(
                    this,
                    "No se pudieron publicar los cosméticos; el juego continuará");
                Console.WriteLine($"No se pudieron publicar los cosméticos: {result.Error}");
                break;

            default:
                Console.WriteLine("Publicación de cosméticos omitida");
                break;
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
