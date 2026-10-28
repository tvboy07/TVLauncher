using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TVLauncher.ViewModels;

namespace TVLauncher.Views;

public partial class ModrinthBrowserWindow : Window
{
    public ModrinthBrowserWindow()
    {
        InitializeComponent();
        DataContext = App.ServiceProvider?.GetRequiredService<ModrinthBrowserViewModel>();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
