using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;
using TVLauncher.Models;

namespace TVLauncher.Services;

public class ConfigService
{
    private readonly string _configDirectory;
    private readonly string _configFilePath;
    public AppConfig Config { get; private set; }

    public ConfigService()
    {
        // Crear directorio de datos en AppData/Local/TVLauncher
        _configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TVLauncher"
        );
        
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
