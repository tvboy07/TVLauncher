using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using TVLauncher.Services;

namespace TVLauncher.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ConfigService _configService;

    /// <summary>
    /// Se dispara cuando la página de ajustes debe cerrarse (guardar o cancelar).
    /// El shell la usa para volver a la página anterior en lugar de cerrar una ventana.
    /// </summary>
    public event EventHandler? CloseRequested;

    [ObservableProperty]
    private int _maxRamGB;

    [ObservableProperty]
    private int _minRamGB;

    [ObservableProperty]
    private bool _enableModrinthStore;

    [ObservableProperty]
    private bool _enableCosmetics;

    [ObservableProperty]
    private bool _autoOptimizeMemory;

    [ObservableProperty]
    private bool _keepLauncherOpen;

    [ObservableProperty]
    private string _defaultSmpName = string.Empty;

    [ObservableProperty]
    private string _defaultSmpIp = string.Empty;

    [ObservableProperty]
    private int _defaultSmpPort;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public SettingsViewModel(ConfigService configService)
    {
        _configService = configService;
        LoadSettings();
    }

    public void LoadSettings()
    {
        MaxRamGB = _configService.Config.MaxRamGB;
        MinRamGB = _configService.Config.MinRamGB;
        EnableModrinthStore = _configService.Config.EnableModrinthStore;
        EnableCosmetics = _configService.Config.EnableCosmetics;
        AutoOptimizeMemory = _configService.Config.AutoOptimizeMemory;
        KeepLauncherOpen = _configService.Config.KeepLauncherOpen;
        DefaultSmpName = _configService.Config.DefaultSmpServer.Name;
        DefaultSmpIp = _configService.Config.DefaultSmpServer.Ip;
        DefaultSmpPort = _configService.Config.DefaultSmpServer.Port;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _configService.Config.MaxRamGB = MaxRamGB;
        _configService.Config.MinRamGB = MinRamGB;
        _configService.Config.EnableModrinthStore = EnableModrinthStore;
        _configService.Config.EnableCosmetics = EnableCosmetics;
        _configService.Config.AutoOptimizeMemory = AutoOptimizeMemory;
        _configService.Config.KeepLauncherOpen = KeepLauncherOpen;
        _configService.Config.DefaultSmpServer.Name = DefaultSmpName;
        _configService.Config.DefaultSmpServer.Ip = DefaultSmpIp;
        _configService.Config.DefaultSmpServer.Port = DefaultSmpPort;

        _configService.SaveConfig();
        StatusMessage = "Ajustes guardados";
        Log.Info("[Settings] Configuración guardada desde el shell");
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        // Descartar cambios: recargar desde disco antes de salir de la página.
        LoadSettings();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
