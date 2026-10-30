using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    /// <summary>ViewModel del armario de cosméticos embebido en la ventana principal.</summary>
    public SkinsPreviewViewModel Cosmetics { get; }

    [ObservableProperty]
    private bool _isCosmeticsOpen;

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

    [ObservableProperty]
    private ObservableCollection<string> _gameLogLines = new();

    [ObservableProperty]
    private bool _hasGameLog;

    [ObservableProperty]
    private bool _isGameRunning;

    /// <summary>Solicitado cuando el proceso del juego arrancó con éxito: el launcher debe ocultarse.</summary>
    public event EventHandler? LauncherHideRequested;

    /// <summary>Solicitado cuando el juego terminó: el launcher debe volver a mostrarse.</summary>
    public event EventHandler? LauncherShowRequested;

    [ObservableProperty]
    private bool _isLaunching;

    [ObservableProperty]
    private ObservableCollection<string> _availableVersions = new();

    [ObservableProperty]
    private string? _selectedVersion;

    [ObservableProperty]
    private int _minRamGB = 2;

    [ObservableProperty]
    private int _maxRamGB = 4;

    public bool CanLaunch => !IsGameRunning && !IsLaunching && _authService.ActiveAccount != null && SelectedInstance != null;

    public MainWindowViewModel(
        ConfigService configService,
        InstanceManager instanceManager,
        AuthService authService,
        MinecraftService minecraftService,
        CosmeticsService cosmeticsService,
        ModrinthService modrinthService,
        SkinsPreviewViewModel cosmetics)
    {
        _configService = configService;
        _instanceManager = instanceManager;
        _authService = authService;
        _minecraftService = minecraftService;
        _cosmeticsService = cosmeticsService;
        _modrinthService = modrinthService;
        Cosmetics = cosmetics;
        _config = configService.Config;

        // Cargar configuración de RAM
        _minRamGB = configService.Config.MinRamGB;
        _maxRamGB = configService.Config.MaxRamGB;

        // Suscribirse a eventos
        _instanceManager.InstanceCreated += OnInstanceCreated;
        _instanceManager.InstanceDeleted += OnInstanceDeleted;
        _instanceManager.InstanceUpdated += OnInstanceUpdated;
        _authService.ActiveAccountChanged += OnActiveAccountChanged;
        _minecraftService.StatusChanged += OnStatusChanged;
        _minecraftService.DownloadProgressChanged += OnDownloadProgressChanged;
        _minecraftService.GameLogReceived += OnGameLogReceived;
        _minecraftService.GameExited += OnGameExited;
        Cosmetics.CloseRequested += (_, _) => IsCosmeticsOpen = false;

        // Cargar datos
        LoadData();

        // La carga de versiones (red) se difiere después del primer render de la ventana
        // para que la interfaz aparezca de inmediato.
        Dispatcher.UIThread.Post(async () => await LoadVersionsAsync());
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
        OnPropertyChanged(nameof(CanLaunch));
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            // Trabajo pesado (red/disco) fuera del hilo de UI para no bloquear el arranque
            var versions = await Task.Run(async () => await _minecraftService.GetVersionListAsync());

            var installed = _instanceManager.Instances
                .Select(i => i.Version)
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct()
                .ToList();

            // Lista completa del manifiesto (incluye versiones legacy) sin recortes.
            var list = installed
                .Concat(versions.Select(v => v.Id))
                .Distinct()
                .ToList();

            // Las colecciones vinculadas a la UI deben modificarse en el hilo de UI
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AvailableVersions.Clear();
                foreach (var id in list)
                    AvailableVersions.Add(id);

                if (SelectedInstance != null && !string.IsNullOrEmpty(SelectedInstance.Version))
                    SelectedVersion = SelectedInstance.Version;
                else if (AvailableVersions.Count > 0)
                    SelectedVersion = AvailableVersions[0];
            });
        }
        catch (Exception ex)
        {
            Log.Error($"Error cargando versiones: {ex.Message}");
        }
    }

    partial void OnSelectedInstanceChanged(MinecraftInstance? value)
    {
        if (value != null)
        {
            _instanceManager.SetSelectedInstance(value.Id);
            UpdateInstanceInfo();
            if (!string.IsNullOrEmpty(value.Version))
                SelectedVersion = value.Version;
        }
        OnPropertyChanged(nameof(CanLaunch));
    }

    partial void OnSelectedVersionChanged(string? value)
    {
        if (SelectedInstance != null && !string.IsNullOrEmpty(value))
        {
            SelectedInstance.Version = value;
            _instanceManager.UpdateInstance(SelectedInstance);
            UpdateInstanceInfo();
        }
    }

    partial void OnMinRamGBChanged(int value)
    {
        _configService.Config.MinRamGB = value;
        _configService.SaveConfig();
    }

    partial void OnMaxRamGBChanged(int value)
    {
        _configService.Config.MaxRamGB = value;
        if (SelectedInstance != null)
        {
            SelectedInstance.MinRamGB = _minRamGB;
            SelectedInstance.MaxRamGB = value;
            _instanceManager.UpdateInstance(SelectedInstance);
        }
        _configService.SaveConfig();
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
        OnPropertyChanged(nameof(CanLaunch));
    }

    private void OnStatusChanged(object? sender, string message)
    {
        StatusMessage = message;
    }

    private void OnDownloadProgressChanged(object? sender, string progress)
    {
        DownloadProgress = progress;
    }

    private void OnGameLogReceived(object? sender, GameLogEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            GameLogLines.Add(e.Line);
            while (GameLogLines.Count > 200)
            {
                GameLogLines.RemoveAt(0);
            }

            HasGameLog = true;

            if (e.IsError || e.Line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                e.Line.Contains("Exception", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = e.Line.Length > 160 ? e.Line[..160] + "..." : e.Line;
            }
        });
    }

    private void OnGameExited(object? sender, int exitCode)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsGameRunning = false;
            IsLaunching = false;
            OnPropertyChanged(nameof(CanLaunch));
            DownloadProgress = string.Empty;

            // El juego se cerró: restaurar el launcher
            LauncherShowRequested?.Invoke(this, EventArgs.Empty);
            StatusMessage = exitCode == 0
                ? "Minecraft se cerró correctamente"
                : $"Minecraft finalizó con código {exitCode}";
        });
    }

    [RelayCommand]
    private void CreateInstance()
    {
        var createWindow = App.ServiceProvider?.GetRequiredService<CreateInstanceWindow>();
        if (createWindow != null)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                createWindow.ShowDialog(desktop.MainWindow);
                LoadData();
            }
        }
    }

    [RelayCommand]
    private void EditInstance()
    {
        if (SelectedInstance == null)
        {
            StatusMessage = "No hay instancia seleccionada";
            return;
        }

        var editWindow = App.ServiceProvider?.GetRequiredService<InstanceEditWindow>();
        if (editWindow == null)
            return;

        var editVm = editWindow.DataContext as InstanceEditViewModel;
        if (editVm != null)
        {
            editVm.Load(SelectedInstance);

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                editWindow.ShowDialog(desktop.MainWindow);
                LoadData();
            }
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

        if (string.IsNullOrWhiteSpace(SelectedVersion))
        {
            StatusMessage = "Selecciona una versión de Minecraft";
            return;
        }

        try
        {
            IsLaunching = true;
            OnPropertyChanged(nameof(CanLaunch));

            // Aplicar la versión seleccionada (la RAM ya es responsabilidad de la instancia/modal)
            if (!string.IsNullOrEmpty(SelectedVersion))
                SelectedInstance.Version = SelectedVersion;
            _instanceManager.UpdateInstance(SelectedInstance);

            // Cerrar el armario para liberar el visor 3D antes de lanzar
            IsCosmeticsOpen = false;

            // Inyección automática de cosméticos (Opción A + C)
            if (_configService.Config.AutoInjectCosmetics)
            {
                StatusMessage = "Inyectando cosméticos...";
                await _cosmeticsService.InjectCosmeticsToInstanceAsync(SelectedInstance);
            }
            
            StatusMessage = "Lanzando Minecraft...";
            IsGameRunning = true;
            OnPropertyChanged(nameof(CanLaunch));

            await _minecraftService.LaunchMinecraftAsync(SelectedInstance);

            // El proceso del juego arrancó con éxito: ocultar el launcher
            // para no competir por CPU/RAM ni generar tráfico de fondo (picos de ping).
            LauncherHideRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            IsGameRunning = false;
            IsLaunching = false;
            OnPropertyChanged(nameof(CanLaunch));
        }
        finally
        {
            if (!IsGameRunning)
            {
                IsLaunching = false;
                OnPropertyChanged(nameof(CanLaunch));
            }
        }
    }

    [RelayCommand]
    private void OpenCosmetics()
    {
        // Se muestra dentro de la ventana principal (sin ventanas emergentes)
        IsCosmeticsOpen = true;
        Log.Info("[MainVM] Armario de cosméticos abierto en la vista principal");
        _ = Cosmetics.EnsureSkinLoadedAsync();
    }

    [RelayCommand]
    private void CloseCosmetics()
    {
        IsCosmeticsOpen = false;
        Log.Info("[MainVM] Armario de cosméticos cerrado");
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
        var accountWindow = App.ServiceProvider?.GetRequiredService<AccountManagerWindow>();
        if (accountWindow != null)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                accountWindow.ShowDialog(desktop.MainWindow);
            }
        }
    }
}
