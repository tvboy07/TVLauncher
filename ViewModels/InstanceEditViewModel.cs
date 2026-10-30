using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TVLauncher.Models;
using TVLauncher.Services;

namespace TVLauncher.ViewModels;

public partial class InstanceEditViewModel : ViewModelBase
{
    private readonly InstanceManager _instanceManager;

    [ObservableProperty]
    private string _instanceName = string.Empty;

    [ObservableProperty]
    private string _selectedVersion = string.Empty;

    [ObservableProperty]
    private string _selectedModLoader = "Vanilla";

    [ObservableProperty]
    private string _selectedModLoaderVersion = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _modLoaderVersions = new();

    [ObservableProperty]
    private int _maxRamGB = 4;

    [ObservableProperty]
    private int _minRamGB = 2;

    [ObservableProperty]
    private ObservableCollection<string> _versions = new();

    [ObservableProperty]
    private ObservableCollection<string> _modLoaders = new() { "Vanilla", "Fabric", "Forge", "NeoForge" };

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    private MinecraftInstance? _instance;

    private readonly GameFilesService _gameFilesService;
    private int _loaderVersionsRequest;

    // Estado instalado: permite distinguir si el usuario cambió versión/loader y,
    // por tanto, si hay que reinstalar el modloader en el próximo arranque.
    private string _installedVersion = string.Empty;
    private string _installedModLoader = "Vanilla";
    private string _installedLoaderVersion = string.Empty;
    private string _baseVersion = string.Empty;

    public event EventHandler? CloseRequested;

    public InstanceEditViewModel(InstanceManager instanceManager, MinecraftService minecraftService, GameFilesService gameFilesService)
    {
        _instanceManager = instanceManager;
        _gameFilesService = gameFilesService;
        _ = LoadVersionsAsync(gameFilesService);
    }

    /// <summary>Carga la instancia a editar y rellena los campos actuales.</summary>
    public void Load(MinecraftInstance instance)
    {
        _instance = instance;
        _installedVersion = instance.Version;
        _installedModLoader = string.IsNullOrEmpty(instance.ModLoader) ? "Vanilla" : instance.ModLoader;
        _installedLoaderVersion = instance.ModLoaderVersion;
        _baseVersion = ResolveBaseVersion(instance);

        InstanceName = instance.Name;

        // Orden importante: cada cambio de loader/versión recarga la lista de versiones
        // del loader, así que el valor guardado se asigna antes de la carga definitiva.
        SelectedModLoader = _installedModLoader;
        SelectedModLoaderVersion = instance.ModLoaderVersion;

        // El manifiesto de versiones llega por red de forma asíncrona. Sembrar la
        // versión base evita que el ComboBox aparezca vacío hasta que termine.
        if (!string.IsNullOrWhiteSpace(_baseVersion) && !Versions.Contains(_baseVersion))
            Versions.Insert(0, _baseVersion);
        SelectedVersion = null;
        SelectedVersion = _baseVersion;

        MaxRamGB = instance.MaxRamGB;
        MinRamGB = instance.MinRamGB;
    }

    /// <summary>
    /// Versión de Minecraft base de la instancia. Si ya hay un perfil de modloader
    /// instalado (p. ej. <c>1.20.1-forge-47.4.28</c>) se lee su <c>inheritsFrom</c>
    /// para que el editor muestre 1.20.1 y no el id interno del perfil.
    /// </summary>
    private string ResolveBaseVersion(MinecraftInstance instance)
    {
        try
        {
            string profilePath = System.IO.Path.Combine(
                _gameFilesService.VersionsDirectory, instance.Version, instance.Version + ".json");

            if (System.IO.File.Exists(profilePath))
            {
                using var document = System.Text.Json.JsonDocument.Parse(
                    System.IO.File.ReadAllText(profilePath));

                if (document.RootElement.TryGetProperty("inheritsFrom", out var parent))
                {
                    string? parentId = parent.GetString();
                    if (!string.IsNullOrWhiteSpace(parentId))
                        return parentId;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[EditVM] No se pudo leer el perfil {instance.Version}: {ex.Message}");
        }

        // Respaldo: en Forge/NeoForge legacy el loader empieza por "{mcVersion}-".
        if (!instance.ModLoader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
        {
            int dash = instance.ModLoaderVersion.IndexOf('-');
            if (dash > 0)
            {
                string candidate = instance.ModLoaderVersion[..dash];
                if (candidate.Count(c => c == '.') >= 1 && candidate[0] is >= '0' and <= '9')
                    return candidate;
            }
        }

        return instance.Version;
    }

    partial void OnSelectedModLoaderChanged(string value)
    {
        _ = LoadModLoaderVersionsAsync();
    }

    partial void OnSelectedVersionChanged(string value)
    {
        if (SelectedModLoader is "Forge" or "NeoForge")
            _ = LoadModLoaderVersionsAsync();
    }

    private async Task LoadModLoaderVersionsAsync()
    {
        int request = ++_loaderVersionsRequest;
        string? current = SelectedModLoaderVersion;

        ModLoaderVersions.Clear();

        if (SelectedModLoader is not ("Forge" or "NeoForge") || string.IsNullOrWhiteSpace(SelectedVersion))
            return;

        try
        {
            var versions = await _gameFilesService.GetForgeLoaderVersionsAsync(
                SelectedVersion, SelectedModLoader == "NeoForge");

            if (request != _loaderVersionsRequest)
                return;

            foreach (var version in versions)
                ModLoaderVersions.Add(version);

            if (!string.IsNullOrWhiteSpace(current) && ModLoaderVersions.Contains(current))
                SelectedModLoaderVersion = current;
            else if (ModLoaderVersions.Count > 0)
                SelectedModLoaderVersion = ModLoaderVersions[0];
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cargar {SelectedModLoader}: {ex.Message}";
        }
    }

    private async System.Threading.Tasks.Task LoadVersionsAsync(GameFilesService gameFilesService)
    {
        try
        {
            var manifest = await gameFilesService.GetManifestAsync();
            string targetVersion = !string.IsNullOrWhiteSpace(_baseVersion) ? _baseVersion : SelectedVersion;

            // Manifiesto completo, sin filtrar por tipo ni recortar: las versiones
            // legacy (1.12.2, 1.8.9, alpha/beta) también deben poder seleccionarse.
            Versions.Clear();
            foreach (var v in manifest.Versions)
                Versions.Add(v.Id);

            // La instancia puede apuntar a un id de modloader que no está en el manifiesto.
            if (!string.IsNullOrWhiteSpace(targetVersion) && !Versions.Contains(targetVersion))
                Versions.Insert(0, targetVersion);

            if (!string.IsNullOrWhiteSpace(targetVersion))
                SelectedVersion = targetVersion;
        }
        catch (Exception ex)
        {
            Log.Warn($"[EditVM] No se pudo cargar el listado de versiones: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (_instance == null)
        {
            StatusMessage = "No hay instancia cargada";
            return;
        }

        if (string.IsNullOrWhiteSpace(InstanceName))
        {
            StatusMessage = "El nombre no puede estar vacío";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedVersion))
        {
            StatusMessage = "Selecciona una versión";
            return;
        }

        // Si el usuario cambió la versión o el loader, la instancia vuelve a apuntar a la
        // versión base: en el siguiente arranque se instala el loader elegido. Si no
        // cambió nada, se conserva el perfil ya instalado (sin reinstalar).
        bool targetChanged =
            !string.Equals(SelectedVersion.Trim(), _baseVersion, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(SelectedModLoader, _installedModLoader, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(SelectedModLoaderVersion, _installedLoaderVersion, StringComparison.OrdinalIgnoreCase);

        _instance.Name = InstanceName.Trim();
        _instance.Version = targetChanged ? SelectedVersion.Trim() : _installedVersion;
        _instance.ModLoader = SelectedModLoader;
        _instance.ModLoaderVersion = SelectedModLoaderVersion;
        _instance.MinRamGB = Math.Clamp(Math.Min(MinRamGB, MaxRamGB), 1, 32);
        _instance.MaxRamGB = Math.Clamp(MaxRamGB, 1, 32);

        _instanceManager.UpdateInstance(_instance);
        StatusMessage = "Cambios guardados";
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
