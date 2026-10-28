using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TVLauncher.Models;

namespace TVLauncher.Services;

public class InstanceManager
{
    private readonly ConfigService _configService;
    private readonly string _instancesDirectory;
    private readonly string _instancesFilePath;
    private List<MinecraftInstance> _instances;

    public List<MinecraftInstance> Instances => _instances;
    public event EventHandler<MinecraftInstance>? InstanceCreated;
    public event EventHandler<MinecraftInstance>? InstanceDeleted;
    public event EventHandler<MinecraftInstance>? InstanceUpdated;

    public InstanceManager(ConfigService configService)
    {
        _configService = configService;
        _instancesDirectory = _configService.GetInstancesPath();
        _instancesFilePath = Path.Combine(_configService.Config.DataPath, "instances.json");
        _instances = new List<MinecraftInstance>();
        
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            Directory.CreateDirectory(_instancesDirectory);
            LoadInstances();
            
            // Crear instancia por defecto si no hay ninguna
            if (_instances.Count == 0)
            {
                CreateDefaultInstance();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al inicializar InstanceManager: {ex.Message}");
        }
    }

    private void CreateDefaultInstance()
    {
        var defaultInstance = new MinecraftInstance
        {
            Name = "Minecraft 1.20.1",
            Version = "1.20.1",
            VersionType = "release",
            ModLoader = "Vanilla",
            MaxRamGB = 4,
            MinRamGB = 2,
            GameDirectory = Path.Combine(_instancesDirectory, "minecraft-1.20.1"),
            CreatedAt = DateTime.Now,
            LastPlayed = DateTime.Now,
            AutoConnectServer = true
        };
        
        _instances.Add(defaultInstance);
        Directory.CreateDirectory(defaultInstance.GameDirectory);
        GenerateServersDat(defaultInstance);
        SaveInstances();
        
        InstanceCreated?.Invoke(this, defaultInstance);
    }

    public void LoadInstances()
    {
        try
        {
            if (File.Exists(_instancesFilePath))
            {
                string json = File.ReadAllText(_instancesFilePath);
                var loadedInstances = JsonConvert.DeserializeObject<List<MinecraftInstance>>(json);
                
                if (loadedInstances != null)
                {
                    _instances = loadedInstances;
                    
                    // Verificar que los directorios existan
                    foreach (var instance in _instances)
                    {
                        if (string.IsNullOrEmpty(instance.GameDirectory))
                        {
                            instance.GameDirectory = Path.Combine(_instancesDirectory, instance.Id);
                        }
                        Directory.CreateDirectory(instance.GameDirectory);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al cargar instancias: {ex.Message}");
            _instances = new List<MinecraftInstance>();
        }
    }

    public void SaveInstances()
    {
        try
        {
            string json = JsonConvert.SerializeObject(_instances, Formatting.Indented);
            File.WriteAllText(_instancesFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al guardar instancias: {ex.Message}");
        }
    }

    public async Task SaveInstancesAsync()
    {
        try
        {
            string json = JsonConvert.SerializeObject(_instances, Formatting.Indented);
            await File.WriteAllTextAsync(_instancesFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al guardar instancias: {ex.Message}");
        }
    }

    public MinecraftInstance CreateInstance(string name, string version, string modLoader = "Vanilla", string modLoaderVersion = "")
    {
        var instance = new MinecraftInstance
        {
            Name = name,
            Version = version,
            ModLoader = modLoader,
            ModLoaderVersion = modLoaderVersion,
            GameDirectory = Path.Combine(_instancesDirectory, SanitizeFileName(name)),
            MaxRamGB = _configService.Config.MaxRamGB,
            MinRamGB = _configService.Config.MinRamGB,
            CreatedAt = DateTime.Now,
            LastPlayed = DateTime.Now,
            AutoConnectServer = true
        };
        
        _instances.Add(instance);
        Directory.CreateDirectory(instance.GameDirectory);
        
        // Crear estructura de carpetas
        Directory.CreateDirectory(Path.Combine(instance.GameDirectory, "mods"));
        Directory.CreateDirectory(Path.Combine(instance.GameDirectory, "resourcepacks"));
        Directory.CreateDirectory(Path.Combine(instance.GameDirectory, "shaderpacks"));
        Directory.CreateDirectory(Path.Combine(instance.GameDirectory, "saves"));
        Directory.CreateDirectory(Path.Combine(instance.GameDirectory, "config"));
        
        // Generar servers.dat con SMP por defecto
        GenerateServersDat(instance);
        
        SaveInstances();
        InstanceCreated?.Invoke(this, instance);
        
        return instance;
    }

    public void DeleteInstance(string instanceId)
    {
        var instance = _instances.FirstOrDefault(i => i.Id == instanceId);
        if (instance != null)
        {
            _instances.Remove(instance);
            
            // Eliminar carpeta de la instancia (opcional, pero recomendado)
            try
            {
                if (Directory.Exists(instance.GameDirectory))
                {
                    Directory.Delete(instance.GameDirectory, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al eliminar carpeta de instancia: {ex.Message}");
            }
            
            SaveInstances();
            InstanceDeleted?.Invoke(this, instance);
        }
    }

    public void UpdateInstance(MinecraftInstance instance)
    {
        var existingInstance = _instances.FirstOrDefault(i => i.Id == instance.Id);
        if (existingInstance != null)
        {
            var index = _instances.IndexOf(existingInstance);
            _instances[index] = instance;
            SaveInstances();
            InstanceUpdated?.Invoke(this, instance);
        }
    }

    public MinecraftInstance? GetInstance(string instanceId)
    {
        return _instances.FirstOrDefault(i => i.Id == instanceId);
    }

    public MinecraftInstance? GetSelectedInstance()
    {
        if (string.IsNullOrEmpty(_configService.Config.SelectedInstance))
            return _instances.FirstOrDefault();
        
        return _instances.FirstOrDefault(i => i.Id == _configService.Config.SelectedInstance) 
               ?? _instances.FirstOrDefault();
    }

    public void SetSelectedInstance(string instanceId)
    {
        _configService.Config.SelectedInstance = instanceId;
        _configService.SaveConfig();
    }

    public void GenerateServersDat(MinecraftInstance instance)
    {
        if (!_configService.Config.DefaultSmpServer.Name.Equals(string.Empty))
        {
            try
            {
                string serversDatPath = Path.Combine(instance.GameDirectory, "servers.dat");
                
                // Si ya existe servers.dat, no sobrescribir para no perder servidores del usuario
                if (File.Exists(serversDatPath))
                {
                    // Aquí se podría fusionar con servidores existentes
                    // Por simplicidad, solo lo creamos si no existe
                    return;
                }
                
                // Crear servers.dat básico con NBT
                // Nota: Para crear NBT correctamente se necesitaría una librería como fNbt
                // Por ahora creamos una estructura placeholder
                Directory.CreateDirectory(instance.GameDirectory);
                
                // Crear archivo marcador para indicar que se debe inyectar
                string markerPath = Path.Combine(instance.GameDirectory, ".tvl_servers_injected");
                if (!File.Exists(markerPath))
                {
                    File.WriteAllText(markerPath, JsonConvert.SerializeObject(new
                    {
                        ServerName = _configService.Config.DefaultSmpServer.Name,
                        ServerIp = _configService.Config.DefaultSmpServer.Ip,
                        ServerPort = _configService.Config.DefaultSmpServer.Port,
                        HiddenAddress = _configService.Config.DefaultSmpServer.HiddenAddress,
                        InjectedAt = DateTime.Now
                    }));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al generar servers.dat: {ex.Message}");
            }
        }
    }

    private string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Replace(" ", "_").ToLower();
    }
}
