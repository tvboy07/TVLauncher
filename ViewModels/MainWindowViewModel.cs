using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using TVLauncher.Models;
using TVLauncher.Services;
using TVLauncher.Views;

namespace TVLauncher.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly InstanceManager _instanceManager;
    private readonly AuthService _authService;
    private readonly MinecraftService _minecraftService;
    private readonly CosmeticsService _cosmeticsService;
    private readonly ModrinthService _modrinthService;

    [ObservableProperty]
    private ObservableCollection<MinecraftInstance> _instances = new();

    [ObservableProperty]
    private MinecraftInstance? _selectedInstance;

    [ObservableProperty]
    private string _activeAccountName = "Sin cuenta";

    [ObservableProperty]
    private string _statusMessage = "Listo";

    [ObservableProperty]
    private string _downloadProgress = "";

    [ObservableProperty]
    private string _instanceInfo = "";

    [ObservableProperty]
    private AppConfig _config;

    public MainWindowViewModel(
        ConfigService configService,
        InstanceManager instanceManager,
        AuthService authService,
        MinecraftService minecraftService,
        CosmeticsService cosmeticsService,
        ModrinthService modrinthService)
    {
        _configService = configService;
        _instanceManager = instanceManager;
        _authService = authService;
        _minecraftService = minecraftService;
        _cosmeticsService = cosmeticsService;
        _modrinthService = modrinthService;
        _config = configService.Config;

        // Suscribirse a eventos
        _instanceManager.InstanceCreated += OnInstanceCreated;
        _instanceManager.InstanceDeleted += OnInstanceDeleted;
        _instanceManager.InstanceUpdated += OnInstanceUpdated;
        _authService.ActiveAccountChanged += OnActiveAccountChanged;
        _minecraftService.StatusChanged += OnStatusChanged;
        _minecraftService.DownloadProgressChanged += OnDownloadProgressChanged;

        // Cargar datos
        LoadData();
    }

    private void LoadData()
    {
        // Cargar instancias
        Instances.Clear();
        foreach (var instance in _instanceManager.Instances)
        {
            Instances.Add(instance);
        }

        // Seleccionar instancia
        SelectedInstance = _instanceManager.GetSelectedInstance();
        if (SelectedInstance != null)
        {
            UpdateInstanceInfo();
        }

        // Cuenta activa
        var activeAccount = _authService.ActiveAccount;
        if (activeAccount != null)
        {
            ActiveAccountName = activeAccount.Username;
        }
        else
        {
            ActiveAccountName = "Sin cuenta";
        }
    }

    partial void OnSelectedInstanceChanged(MinecraftInstance? value)
    {
        if (value != null)
        {
            _instanceManager.SetSelectedInstance(value.Id);
            UpdateInstanceInfo();
        }
    }

    private void UpdateInstanceInfo()
    {
        if (SelectedInstance != null)
        {
            InstanceInfo = $"{SelectedInstance.ModLoader} | RAM: {SelectedInstance.MaxRamGB}GB | Creado: {SelectedInstance.CreatedAt:dd/MM/yyyy}";
        }
    }

    private void OnInstanceCreated(object? sender, MinecraftInstance instance)
    {
        Instances.Add(instance);
    }

    private void OnInstanceDeleted(object? sender, MinecraftInstance instance)
    {
        Instances.Remove(instance);
    }

    private void OnInstanceUpdated(object? sender, MinecraftInstance instance)
    {
        // Actualizar en la colección
    }

    private void OnActiveAccountChanged(object? sender, MinecraftAccount account)
    {
        ActiveAccountName = account.Username;
    }

    private void OnStatusChanged(object? sender, string message)
    {
        StatusMessage = message;
    }

    private void OnDownloadProgressChanged(object? sender, string progress)
    {
        DownloadProgress = progress;
    }

    [RelayCommand]
    private void CreateInstance()
    {
        StatusMessage = "Crear instancia - Funcionalidad en desarrollo";
    }

    [RelayCommand]
    private void EditInstance()
    {
        if (SelectedInstance != null)
        {
            StatusMessage = $"Editando {SelectedInstance.Name} - Funcionalidad en desarrollo";
        }
    }

    [RelayCommand]
    private async Task LaunchGame()
    {
        if (SelectedInstance == null)
        {
            StatusMessage = "No hay instancia seleccionada";
            return;
        }

        if (_authService.ActiveAccount == null)
        {
            StatusMessage = "No hay cuenta activa. Añade una cuenta primero.";
            return;
        }

        try
        {
            // Inyección automática de cosméticos (Opción A + C)
            if (_configService.Config.AutoInjectCosmetics)
            {
                StatusMessage = "Inyectando cosméticos...";
                await _cosmeticsService.InjectCosmeticsToInstanceAsync(SelectedInstance);
            }
            
            StatusMessage = "Lanzando Minecraft...";
            await _minecraftService.LaunchMinecraftAsync(SelectedInstance);
            
            if (!_configService.Config.KeepLauncherOpen)
            {
                // Cerrar launcher si está configurado
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenCosmetics()
    {
        var cosmeticsWindow = App.ServiceProvider?.GetRequiredService<SkinsPreviewWindow>();
        if (cosmeticsWindow == null)
        {
            StatusMessage = "No se pudo abrir el armario de cosméticos";
            Log.Error("[MainVM] No se pudo resolver SkinsPreviewWindow desde DI");
            return;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow != null)
        {
            Log.Info("[MainVM] Abriendo armario de cosméticos");
            cosmeticsWindow.ShowDialog(desktop.MainWindow);
        }
        else
        {
            cosmeticsWindow.Show();
        }
    }

    [RelayCommand]
    private void OpenModStore()
    {
        if (!_configService.Config.EnableModrinthStore)
        {
            StatusMessage = "La tienda de Modrinth está deshabilitada en configuración";
            return;
        }
        
        var modrinthWindow = App.ServiceProvider?.GetRequiredService<ModrinthBrowserWindow>();
        if (modrinthWindow != null)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                modrinthWindow.ShowDialog(desktop.MainWindow);
            }
        }
    }

    [RelayCommand]
    private void OpenInstanceFolder()
    {
        if (SelectedInstance != null && Directory.Exists(SelectedInstance.GameDirectory))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SelectedInstance.GameDirectory,
                UseShellExecute = true,
                Verb = "open"
            });
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var settingsWindow = App.ServiceProvider?.GetRequiredService<SettingsWindow>();
        if (settingsWindow != null)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                settingsWindow.ShowDialog(desktop.MainWindow!);
            }
        }
    }

    [RelayCommand]
    private void OpenAccounts()
    {
        StatusMessage = "Gestor de cuentas - Funcionalidad en desarrollo";
    }
}
