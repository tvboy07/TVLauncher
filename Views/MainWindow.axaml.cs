using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using TVLauncher.ViewModels;

namespace TVLauncher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.ServiceProvider?.GetRequiredService<MainWindowViewModel>();

        // El launcher se oculta mientras el juego está en ejecución y
        // se restaura automáticamente cuando el usuario cierra Minecraft.
        if (DataContext is MainWindowViewModel vm)
        {
            vm.LauncherHideRequested += (_, _) =>
                Dispatcher.UIThread.Post(Hide);

            vm.LauncherShowRequested += (_, _) =>
                Dispatcher.UIThread.Post(() =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                });
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
