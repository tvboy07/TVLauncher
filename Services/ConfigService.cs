using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;
using TVLauncher.Models;

namespace TVLauncher.Services;

public class ConfigService
{
    /// <summary>
    /// Hostname placeholder que se distribuyó en versiones anteriores como
    /// servidor SMP por defecto. Ya no resuelve por DNS.
    /// </summary>
    internal const string LegacySmpHost = "play.tvlauncher.net";

    private readonly string _configDirectory;
    private readonly string _configFilePath;
    public AppConfig Config { get; private set; }

    public ConfigService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TVLauncher"))
    {
    }

    /// <summary>
    /// Variante con directorio explícito. Solo para tests (InternalsVisibleTo):
    /// permite ejercitar la carga y la migración de <c>config.json</c> en una
    /// carpeta temporal sin tocar <c>%LOCALAPPDATA%</c>.
    /// </summary>
    internal ConfigService(string configDirectory)
    {
        // Crear directorio de datos en AppData/Local/TVLauncher
        _configDirectory = configDirectory;

        _configFilePath = Path.Combine(_configDirectory, "config.json");
        Config = new AppConfig();

        Initialize();
    }

    private void Initialize()
    {
        try
        {
            // Crear directorios necesarios
            Directory.CreateDirectory(_configDirectory);
            Directory.CreateDirectory(Path.Combine(_configDirectory, "instances"));
            Directory.CreateDirectory(Path.Combine(_configDirectory, "assets"));
            Directory.CreateDirectory(Path.Combine(_configDirectory, "cache"));
            Directory.CreateDirectory(Path.Combine(_configDirectory, "cosmetics"));
            
            // Establecer ruta de datos por defecto
            if (string.IsNullOrEmpty(Config.DataPath))
            {
                Config.DataPath = _configDirectory;
            }
            
            // Cargar configuración si existe
            if (File.Exists(_configFilePath))
            {
                LoadConfig();
            }
            else
            {
                // Primera ejecución - guardar configuración por defecto
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al inicializar ConfigService: {ex.Message}");
        }
    }

    public void LoadConfig()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                string json = File.ReadAllText(_configFilePath);
                var loadedConfig = JsonConvert.DeserializeObject<AppConfig>(json);
                
                if (loadedConfig != null)
                {
                    Config = loadedConfig;
                    
                    // Asegurar que las rutas existan
                    if (string.IsNullOrEmpty(Config.DataPath))
                    {
                        Config.DataPath = _configDirectory;
                    }

                    // Endpoints placeholder de versiones anteriores.
                    if (MigrateLegacyEndpoints())
                    {
                        SaveConfig();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al cargar configuración: {ex.Message}");
            // Usar configuración por defecto en caso de error
            Config = new AppConfig { DataPath = _configDirectory };
        }
    }

    /// <summary>
    /// Sustituye endpoints placeholder de versiones anteriores. Hoy: el hostname
    /// del SMP por defecto (<c>play.tvlauncher.net</c>, que ya no resuelve) por la
    /// IP real. Los hosts personalizados del usuario no se tocan.
    /// </summary>
    /// <returns><c>true</c> si hubo cambios que deban persistirse.</returns>
    private bool MigrateLegacyEndpoints()
    {
        if (Config.DefaultSmpServer is null)
            Config.DefaultSmpServer = new DefaultSmpServer();

        if (string.Equals(Config.DefaultSmpServer.Ip?.Trim(), LegacySmpHost, StringComparison.OrdinalIgnoreCase))
        {
            Config.DefaultSmpServer.Ip = DefaultSmpServer.DefaultIp;
            Log.Info($"[Config] Endpoint SMP migrado: {LegacySmpHost} -> {DefaultSmpServer.DefaultIp}");
            return true;
        }

        return false;
    }

    public void SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(_configDirectory);
            string json = JsonConvert.SerializeObject(Config, Formatting.Indented);
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al guardar configuración: {ex.Message}");
        }
    }

    public async Task SaveConfigAsync()
    {
        try
        {
            Directory.CreateDirectory(_configDirectory);
            string json = JsonConvert.SerializeObject(Config, Formatting.Indented);
            await File.WriteAllTextAsync(_configFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al guardar configuración: {ex.Message}");
        }
    }

    public string GetInstancesPath()
    {
        return Path.Combine(Config.DataPath, "instances");
    }

    public string GetAssetsPath()
    {
        return Path.Combine(Config.DataPath, "assets");
    }

    public string GetCachePath()
    {
        return Path.Combine(Config.DataPath, "cache");
    }

    public string GetCosmeticsPath()
    {
        return Path.Combine(Config.DataPath, "cosmetics");
    }

    public void CompleteFirstRun()
    {
        Config.FirstRun = false;
        SaveConfig();
    }

    public void OptimizeMemory()
    {
        try
        {
            // Forzar recolección de basura para liberar memoria
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al optimizar memoria: {ex.Message}");
        }
    }
}
