using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using TVLauncher.Models;
using TVLauncher.Services;

namespace TVLauncher.ViewModels;

public partial class ModrinthBrowserViewModel : ViewModelBase
{
    private readonly ModrinthService _modrinthService;
    private readonly InstanceManager _instanceManager;

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private string _selectedGameVersion = "1.20.1";

    [ObservableProperty]
    private string _selectedModLoader = "fabric";

    [ObservableProperty]
    private ObservableCollection<ModrinthProject> _searchResults = new();

    [ObservableProperty]
    private ModrinthProject? _selectedProject;

    [ObservableProperty]
    private ObservableCollection<ModrinthVersion> _projectVersions = new();

    [ObservableProperty]
    private ModrinthVersion? _selectedVersion;

    [ObservableProperty]
    private string _statusMessage = "Busca mods en Modrinth";

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _isDownloading;

    public ObservableCollection<string> GameVersions { get; } = new() { "1.20.1", "1.20.4", "1.19.4", "1.19.2", "1.18.2" };
    public ObservableCollection<string> ModLoaders { get; } = new() { "fabric", "forge", "neoforge", "quilt" };

    public ModrinthBrowserViewModel(ModrinthService modrinthService, InstanceManager instanceManager)
    {
        _modrinthService = modrinthService;
        _instanceManager = instanceManager;

        _modrinthService.StatusChanged += (s, msg) => StatusMessage = msg;

        // El desplegable arranca con una lista de respaldo y se sustituye por el
        // catálogo real de Modrinth en cuanto llega la respuesta.
        _ = LoadGameVersionsAsync();
    }

    partial void OnSelectedProjectChanged(ModrinthProject? value)
    {
        if (value != null)
        {
            _ = LoadProjectVersionsAsync(value.ProjectId);
        }
    }

    partial void OnSelectedGameVersionChanged(string value) => ReloadVersionsIfProjectSelected();

    partial void OnSelectedModLoaderChanged(string value) => ReloadVersionsIfProjectSelected();

    private void ReloadVersionsIfProjectSelected()
    {
        if (SelectedProject != null)
            _ = LoadProjectVersionsAsync(SelectedProject.ProjectId);
    }

    private async Task LoadGameVersionsAsync()
    {
        try
        {
            var versions = await _modrinthService.GetGameVersionsAsync();
            if (versions.Count == 0)
                return; // se mantiene la lista de respaldo

            string previous = SelectedGameVersion;
            GameVersions.Clear();
            foreach (var version in versions)
                GameVersions.Add(version);

            SelectedGameVersion = GameVersions.Contains(previous)
                ? previous
                : GameVersions[0];
        }
        catch (Exception ex)
        {
            Log.Warn($"[Modrinth] No se pudo cargar el catálogo de versiones: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task Search()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            StatusMessage = "Introduce un término de búsqueda";
            return;
        }

        IsLoading = true;
        StatusMessage = "Buscando...";
        SearchResults.Clear();

        try
        {
            var results = await _modrinthService.SearchModsAsync(SearchQuery, SelectedGameVersion, SelectedModLoader, 30);
            foreach (var mod in results)
            {
                SearchResults.Add(mod);
            }
            StatusMessage = $"Se encontraron {SearchResults.Count} resultados";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadProjectVersionsAsync(string projectId)
    {
        StatusMessage = "Cargando versiones...";
        ProjectVersions.Clear();

        try
        {
            var versions = await _modrinthService.GetProjectVersionsAsync(projectId, SelectedGameVersion, SelectedModLoader);
            foreach (var version in versions)
            {
                ProjectVersions.Add(version);
            }
            StatusMessage = $"{ProjectVersions.Count} versiones compatibles encontradas";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DownloadMod()
    {
        if (SelectedVersion == null || SelectedProject == null)
        {
            StatusMessage = "Selecciona una versión para descargar";
            return;
        }

        var instance = _instanceManager.GetSelectedInstance();
        if (instance == null)
        {
            StatusMessage = "No hay instancia seleccionada";
            return;
        }

        try
        {
            IsLoading = true;
            IsDownloading = true;
            DownloadProgress = 0;
            StatusMessage = $"Descargando {SelectedProject.Title}...";

            // Buscar archivo primario
            var primaryFile = SelectedVersion.Files.FirstOrDefault(f => f.Primary) ?? SelectedVersion.Files.FirstOrDefault();
            if (primaryFile == null)
            {
                StatusMessage = "No se encontró archivo para descargar";
                return;
            }

            string modsPath = Path.Combine(instance.GameDirectory, "mods");
            string filePath = Path.Combine(modsPath, primaryFile.Filename);

            // Progress<T> se crea en el hilo de UI, así que los informes del
            // descargador llegan marshalados al hilo correcto.
            var progress = new Progress<double>(value =>
            {
                if (!IsDownloading)
                    return;

                DownloadProgress = value;
                StatusMessage = $"Descargando {SelectedProject.Title}... {value:0}%";
            });

            await _modrinthService.DownloadFileAsync(primaryFile.Url, filePath, progress);

            DownloadProgress = 100;
            StatusMessage = $"{SelectedProject.Title} descargado correctamente a {instance.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al descargar: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            IsLoading = false;
        }
    }
}
