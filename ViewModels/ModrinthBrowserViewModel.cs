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
    private readonly HttpClient _httpClient;

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

    public ObservableCollection<string> GameVersions { get; } = new() { "1.20.1", "1.20.4", "1.19.4", "1.19.2", "1.18.2" };
    public ObservableCollection<string> ModLoaders { get; } = new() { "fabric", "forge", "neoforge", "quilt" };

    public ModrinthBrowserViewModel(ModrinthService modrinthService, InstanceManager instanceManager)
    {
        _modrinthService = modrinthService;
        _instanceManager = instanceManager;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TVLauncher/1.0.0");
        
        _modrinthService.StatusChanged += (s, msg) => StatusMessage = msg;
    }

    partial void OnSelectedProjectChanged(ModrinthProject? value)
    {
        if (value != null)
        {
            _ = LoadProjectVersionsAsync(value.ProjectId);
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
            StatusMessage = $"Descargando {SelectedProject.Title}...";

            // Buscar archivo primario
            var primaryFile = SelectedVersion.Files.FirstOrDefault(f => f.Primary) ?? SelectedVersion.Files.FirstOrDefault();
            if (primaryFile == null)
            {
                StatusMessage = "No se encontró archivo para descargar";
                return;
            }

            string modsPath = Path.Combine(instance.GameDirectory, "mods");
            Directory.CreateDirectory(modsPath);

            string filePath = Path.Combine(modsPath, primaryFile.Filename);
            
            // Descargar archivo
            byte[] fileBytes = await _httpClient.GetByteArrayAsync(primaryFile.Url);
            await File.WriteAllBytesAsync(filePath, fileBytes);

            StatusMessage = $"{SelectedProject.Title} descargado correctamente a {instance.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al descargar: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
