using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using System;
using TVLauncher.Services;
using TVLauncher.ViewModels;
using TVLauncher.Views;

namespace TVLauncher;

public partial class App : Application
{
    public static IServiceProvider? ServiceProvider { get; private set; }

    public static void SetMainWindow(Avalonia.Controls.Window window)
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = window;
        }
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            // Configure Dependency Injection
            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var configService = ServiceProvider.GetRequiredService<ConfigService>();

                if (configService.Config.FirstRun)
                {
                    desktop.MainWindow = ServiceProvider.GetRequiredService<SetupWizardWindow>();
                }
                else
                {
                    desktop.MainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                }
            }

            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("[TVLauncher] ERROR CRÍTICO EN INICIALIZACIÓN");
            Console.WriteLine("========================================");
            Console.WriteLine($"Exception: {ex.GetType().Name}");
            Console.WriteLine($"Message: {ex.Message}");
            Console.WriteLine($"StackTrace: {ex.StackTrace}");
            Console.WriteLine("========================================");

            throw;
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Services
        services.AddSingleton<ConfigService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<InstanceManager>();
        services.AddSingleton<MinecraftService>();
        services.AddSingleton<JavaService>();
        services.AddSingleton<GameFilesService>();
        services.AddSingleton<CosmeticsService>();
        services.AddSingleton<ModrinthService>();
        services.AddSingleton<SkinService>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SetupWizardViewModel>();
        services.AddTransient<SkinsPreviewViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ModrinthBrowserViewModel>();

        // Views
        services.AddTransient<MainWindow>();
        services.AddTransient<SetupWizardWindow>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<ModrinthBrowserWindow>();
    }
}
