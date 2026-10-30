using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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

    /// <summary>ViewModel de la tienda de Modrinth embebida como página del shell.</summary>
    public ModrinthBrowserViewModel Modrinth { get; }

    /// <summary>ViewModel de la página de ajustes embebida en el shell.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>Cliente solo para el avatar; el resto de la red pasa por los servicios.</summary>
    private static readonly HttpClient AvatarHttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };

    [ObservableProperty]
    private AppPage _currentPage = AppPage.Instances;

    /// <summary>Título que muestra el header del shell para la página activa.</summary>
    public string PageTitle => CurrentPage switch
    {
        AppPage.Modrinth => "Tienda de Modrinth",
        AppPage.Cosmetics => "Cosméticos",
        AppPage.Settings => "Ajustes",
        _ => "Instancias"
    };

    /// <summary>Subtítulo descriptivo de la página activa.</summary>
    public string PageSubtitle => CurrentPage switch
    {
        AppPage.Modrinth => "Descubre, descarga y aplica mods a tu instancia",
        AppPage.Cosmetics => "Personaliza tu personaje y previsualízalo en 3D",
        AppPage.Settings => "Memoria, módulos y servidor por defecto",
        _ => "Elige una instancia y lánzala cuando estés listo"
    };

    public bool IsInstancesPage => CurrentPage == AppPage.Instances;
    public bool IsModrinthPage => CurrentPage == AppPage.Modrinth;
    public bool IsCosmeticsPage => CurrentPage == AppPage.Cosmetics;
    public bool IsSettingsPage => CurrentPage == AppPage.Settings;

    [ObservableProperty]
    private ObservableCollection<MinecraftInstance> _instances = new();

    [ObservableProperty]
    private MinecraftInstance? _selectedInstance;

    [ObservableProperty]
    private string _activeAccountName = "Sin cuenta";

    /// <summary>Cabeza de skin descargada de mc-heads.net; null si no hay red o cuenta.</summary>
    [ObservableProperty]
    private Bitmap? _avatar;

    /// <summary>Iniciales del gamertag, usadas como respaldo del avatar.</summary>
    [ObservableProperty]
    private string _avatarInitials = "?";

    [ObservableProperty]
    private bool _hasAvatar;

    /// <summary>Texto del estado de la cuenta en el header (Conectado / Offline / Sin cuenta).</summary>
    [ObservableProperty]
    private string _accountStatus = "Sin cuenta";

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
        SkinsPreviewViewModel cosmetics,
        ModrinthBrowserViewModel modrinth,
        SettingsViewModel settings)
    {
        _configService = configService;
        _instanceManager = instanceManager;
        _authService = authService;
        _minecraftService = minecraftService;
        _cosmeticsService = cosmeticsService;
        _modrinthService = modrinthService;
        Cosmetics = cosmetics;
        Modrinth = modrinth;
        Settings = settings;
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
        Cosmetics.CloseRequested += (_, _) => _ = ShowInstances();
        Settings.CloseRequested += (_, _) => _ = ShowInstances();

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
        RefreshProfile();
        OnPropertyChanged(nameof(CanLaunch));
    }

    /// <summary>
    /// Sincroniza el gamertag, el estado y el avatar del header con la cuenta activa.
    /// Si el armario tiene una skin equipada de otro usuario, el avatar la refleja.
    /// El avatar se pide en segundo plano: si falla, quedan las iniciales.
    /// </summary>
    private void RefreshProfile()
    {
        var activeAccount = _authService.ActiveAccount;
        if (activeAccount != null)
        {
            ActiveAccountName = activeAccount.Username;
            AccountStatus = activeAccount.Type == AccountType.Microsoft
                ? "Conectado"
                : "Modo offline";
        }
        else
        {
            ActiveAccountName = "Sin cuenta";
            AccountStatus = "Sin sesión";
        }

        // La skin equipada en el armario manda sobre la cuenta para el avatar.
        string avatarName = !string.IsNullOrWhiteSpace(_configService.Config.EquippedSkinUsername)
            ? _configService.Config.EquippedSkinUsername!
            : activeAccount?.Username ?? ActiveAccountName;

        AvatarInitials = BuildInitials(avatarName);
        Avatar = null;
        HasAvatar = false;

        if (!string.IsNullOrWhiteSpace(avatarName) && activeAccount != null)
            _ = LoadAvatarAsync(avatarName, bustCache: true);
    }

    /// <summary>
    /// Tras volver del armario: recarga skin/capa/sombrero desde la configuración
    /// y refresca el avatar del header con la skin equipada.
    /// </summary>
    private async Task RefreshFromCosmeticsAsync()
    {
        try
        {
            await Cosmetics.RefreshEquippedStateAsync();
        }
        catch (Exception ex)
        {
            Log.Warn($"[MainVM] No se pudo sincronizar el armario: {ex.Message}");
        }

        RefreshProfile();
    }

    /// <summary>Iniciales para el respaldo del avatar (máximo dos letras).</summary>
    internal static string BuildInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        var words = name.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2
            ? $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}"
            : name.Trim()[..1].ToUpperInvariant();
    }

    /// <summary>Nombre cuya cabeza se está solicitando; evita aplicar respuestas viejas.</summary>
    private string _avatarRequestName = string.Empty;

    /// <summary>Descarga la cabeza de la skin (64 px) y la expone como avatar circular.</summary>
    private async Task LoadAvatarAsync(string username, bool bustCache = false)
    {
        try
        {
            _avatarRequestName = username;
            string url = $"https://mc-heads.net/avatar/{Uri.EscapeDataString(username)}/64";
            if (bustCache)
                url += $"?t={DateTime.UtcNow.Ticks}";

            byte[] bytes = await AvatarHttpClient.GetByteArrayAsync(url);

            // La skin pudo cambiar mientras descargábamos.
            if (!string.Equals(username, _avatarRequestName, StringComparison.OrdinalIgnoreCase))
                return;

            using var stream = new MemoryStream(bytes);
            Avatar = new Bitmap(stream);
            HasAvatar = true;
        }
        catch (Exception ex)
        {
            Log.Warn($"[MainVM] Avatar no disponible para {username}: {ex.Message}");
        }
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

                // Fuerza una notificación aunque el valor sea el mismo: al
                // recrear ItemsSource el ComboBox puede descartar el SelectedItem
                // y dejarse en blanco. Pasar por null garantiza que lo reenganche.
                SelectedVersion = null;
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
        RefreshProfile();
        OnPropertyChanged(nameof(CanLaunch));
    }

    partial void OnCurrentPageChanged(AppPage value)
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(IsInstancesPage));
        OnPropertyChanged(nameof(IsModrinthPage));
        OnPropertyChanged(nameof(IsCosmeticsPage));
        OnPropertyChanged(nameof(IsSettingsPage));
    }

    /// <summary>Navega a una página del shell y registra el cambio.</summary>
    private void NavigateTo(AppPage page)
    {
        if (CurrentPage == page)
            return;

        CurrentPage = page;
        Log.Info($"[MainVM] Página activa: {page}");
    }

    [RelayCommand]
    private async Task ShowInstances()
    {
        bool returningFromCosmetics = CurrentPage == AppPage.Cosmetics;
        NavigateTo(AppPage.Instances);

        if (returningFromCosmetics)
            await RefreshFromCosmeticsAsync();
    }

    [RelayCommand]
    private void ShowModrinth()
    {
        if (!_configService.Config.EnableModrinthStore)
        {
            StatusMessage = "La tienda de Modrinth está deshabilitada en configuración";
            return;
        }

        NavigateTo(AppPage.Modrinth);
    }

    [RelayCommand]
    private void ShowCosmetics()
    {
        NavigateTo(AppPage.Cosmetics);
        _ = Cosmetics.EnsureSkinLoadedAsync();
    }

    [RelayCommand]
    private void ShowSettings()
    {
        Settings.LoadSettings();
        NavigateTo(AppPage.Settings);
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
        {
            Log.Error("[MainVM] No se pudo resolver InstanceEditWindow");
            return;
        }

        var editVm = editWindow.DataContext as InstanceEditViewModel;
        if (editVm != null)
        {
            editVm.Load(SelectedInstance);
            Log.Info($"[MainVM] Editando instancia '{SelectedInstance.Name}'");

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                editWindow.ShowDialog(desktop.MainWindow);
                LoadData();
            }
        }
        else
        {
            Log.Error("[MainVM] InstanceEditWindow sin InstanceEditViewModel como DataContext");
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

            // Volver a Instancias para liberar el visor 3D antes de lanzar
            NavigateTo(AppPage.Instances);

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
        ShowCosmetics();
        Log.Info("[MainVM] Armario de cosméticos abierto en la vista principal");
    }

    [RelayCommand]
    private async Task CloseCosmetics()
    {
        await ShowInstances();
        Log.Info("[MainVM] Armario de cosméticos cerrado");
    }

    [RelayCommand]
    private void OpenModStore() => ShowModrinth();

    /// <summary>Abre la gestión de cuentas (única ventana modal que se conserva).</summary>
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
    private void OpenSettings() => ShowSettings();

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
