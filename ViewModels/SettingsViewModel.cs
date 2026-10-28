using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using TVLauncher.Services;

namespace TVLauncher.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ConfigService _configService;

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
    private string _defaultSmpName;

    [ObservableProperty]
    private string _defaultSmpIp;

    [ObservableProperty]
    private int _defaultSmpPort;

    public SettingsViewModel(ConfigService configService)
    {
        _configService = configService;
        LoadSettings();
    }

    private void LoadSettings()
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
    }
}
