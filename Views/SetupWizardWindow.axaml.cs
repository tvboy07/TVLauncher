using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using System;
using TVLauncher.ViewModels;

namespace TVLauncher.Views;

public partial class SetupWizardWindow : Window
{
    private SetupWizardViewModel? _viewModel;

    public SetupWizardWindow()
    {
        InitializeComponent();
        _viewModel = App.ServiceProvider?.GetRequiredService<SetupWizardViewModel>();
        DataContext = _viewModel;
        
        if (_viewModel != null)
        {
            _viewModel.SetupCompleted += OnSetupCompleted;
        }
    }

    private void OnSetupCompleted(object? sender, EventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = App.ServiceProvider?.GetRequiredService<MainWindow>();
            if (mainWindow != null)
            {
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                Close();
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
