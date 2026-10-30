using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using TVLauncher.Models;
using TVLauncher.Services;
using TVLauncher.Views;

namespace TVLauncher.ViewModels;

public partial class CreateInstanceViewModel : ViewModelBase
{
    private readonly InstanceManager _instanceManager;
    private readonly MinecraftService _minecraftService;
    private readonly GameFilesService _gameFilesService;

    [ObservableProperty]
    private string _instanceName = "Nueva Instancia";

    [ObservableProperty]
    private string _selectedVersion = string.Empty;

    [ObservableProperty]
    private string _selectedModLoader = "Vanilla";

    [ObservableProperty]
    private string _selectedModLoaderVersion = string.Empty;

    [ObservableProperty]
    private int _maxRamGB = 4;

    [ObservableProperty]
    private int _minRamGB = 2;

    [ObservableProperty]
    private ObservableCollection<string> _versions = new();

    [ObservableProperty]
    private ObservableCollection<string> _modLoaders = new() { "Vanilla", "Fabric", "Forge", "NeoForge" };

    [ObservableProperty]
    private ObservableCollection<string> _modLoaderVersions = new();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public event EventHandler? CloseRequested;

    private int _loaderVersionsRequest;

    public CreateInstanceViewModel(InstanceManager instanceManager, MinecraftService minecraftService, GameFilesService gameFilesService)
    {
        _instanceManager = instanceManager;
        _minecraftService = minecraftService;
        _gameFilesService = gameFilesService;
        _ = LoadVersionsAsync();
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Cargando versiones...";
            var manifest = await _gameFilesService.GetManifestAsync();

            // Mostrar el manifiesto completo (release, snapshot, old_beta y old_alpha),
            // sin recortes: así están disponibles 1.12.2, 1.8.9 y el resto de versiones
            // históricas. Mojang ya las entrega ordenadas de más reciente a más antigua.
            Versions.Clear();
            foreach (var v in manifest.Versions)
                Versions.Add(v.Id);

            if (Versions.Count > 0 && string.IsNullOrEmpty(SelectedVersion))
                SelectedVersion = Versions.Contains(manifest.Latest.Release)
                    ? manifest.Latest.Release
                    : Versions[0];

            StatusMessage = $"{Versions.Count} versiones disponibles";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cargar versiones: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedModLoaderChanged(string value)
    {
        _ = LoadModLoaderVersionsAsync();
    }

    partial void OnSelectedVersionChanged(string value)
    {
        // Las versiones del loader dependen de la versión de Minecraft elegida.
        if (SelectedModLoader is "Forge" or "NeoForge")
            _ = LoadModLoaderVersionsAsync();
    }

    private async Task LoadModLoaderVersionsAsync()
    {
        int request = ++_loaderVersionsRequest;

        ModLoaderVersions.Clear();
        SelectedModLoaderVersion = string.Empty;

        if (SelectedModLoader is not ("Forge" or "NeoForge"))
        {
            StatusMessage = string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedVersion))
            return;

        try
        {
            StatusMessage = $"Cargando versiones de {SelectedModLoader}...";
            bool neoForge = SelectedModLoader == "NeoForge";
            var versions = await _gameFilesService.GetForgeLoaderVersionsAsync(SelectedVersion, neoForge);

            // Otra selección reemplazó esta petición: descartar el resultado.
            if (request != _loaderVersionsRequest)
                return;

            foreach (var version in versions)
                ModLoaderVersions.Add(version);

            if (ModLoaderVersions.Count > 0)
                SelectedModLoaderVersion = ModLoaderVersions[0];

            StatusMessage = ModLoaderVersions.Count == 0
                ? $"No hay versiones de {SelectedModLoader} para {SelectedVersion}"
                : $"{ModLoaderVersions.Count} versiones de {SelectedModLoader} disponibles";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cargar {SelectedModLoader}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Create()
    {
        if (string.IsNullOrWhiteSpace(InstanceName))
        {
            StatusMessage = "Introduce un nombre para la instancia";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedVersion))
        {
            StatusMessage = "Selecciona una versión";
            return;
        }

        if (SelectedModLoader is "Forge" or "NeoForge" &&
            string.IsNullOrWhiteSpace(SelectedModLoaderVersion))
        {
            StatusMessage = $"Selecciona una versión de {SelectedModLoader}";
            return;
        }

        try
        {
            var instance = new MinecraftInstance
            {
                Name = InstanceName.Trim(),
                Version = SelectedVersion,
                ModLoader = SelectedModLoader,
                ModLoaderVersion = SelectedModLoaderVersion,
                MinRamGB = MinRamGB,
                MaxRamGB = MaxRamGB,
                GameDirectory = string.Empty // Se genera automáticamente en InstanceManager
            };

            _instanceManager.CreateInstance(instance.Name, instance.Version, instance.ModLoader, instance.ModLoaderVersion,
                MinRamGB, MaxRamGB);
            StatusMessage = "Instancia creada correctamente";
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
