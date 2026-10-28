using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using TVLauncher.Services;

namespace TVLauncher.ViewModels;

public partial class SetupWizardViewModel : ViewModelBase
{
    private readonly ConfigService _configService;

    public event EventHandler? SetupCompleted;

    [ObservableProperty]
    private int _currentStep = 0;

    [ObservableProperty]
    private bool _enableModrinthStore = true;

    [ObservableProperty]
    private bool _enableCosmetics = true;

    [ObservableProperty]
    private bool _autoOptimizeMemory = true;

    public SetupWizardViewModel(ConfigService configService)
    {
        _configService = configService;
        EnableModrinthStore = _configService.Config.EnableModrinthStore;
        EnableCosmetics = _configService.Config.EnableCosmetics;
        AutoOptimizeMemory = _configService.Config.AutoOptimizeMemory;
    }

    [RelayCommand]
    private void NextStep()
    {
        if (CurrentStep < 2)
        {
            CurrentStep++;
        }
        else
        {
            FinishSetup();
        }
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep > 0)
        {
            CurrentStep--;
        }
    }

    [RelayCommand]
    private void FinishSetup()
    {
        _configService.Config.EnableModrinthStore = EnableModrinthStore;
        _configService.Config.EnableCosmetics = EnableCosmetics;
        _configService.Config.AutoOptimizeMemory = AutoOptimizeMemory;
        _configService.CompleteFirstRun();
        _configService.SaveConfig();
        
        SetupCompleted?.Invoke(this, EventArgs.Empty);
    }
}
