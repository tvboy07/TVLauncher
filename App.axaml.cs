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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            Console.WriteLine("[TVLauncher] Inicializando aplicación...");
            
            // Configure Dependency Injection
            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();
            Console.WriteLine("[TVLauncher] DI configurado correctamente");

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Check if first run
                var configService = ServiceProvider.GetRequiredService<ConfigService>();
                Console.WriteLine($"[TVLauncher] FirstRun: {configService.Config.FirstRun}");
                
                if (configService.Config.FirstRun)
                {
                    Console.WriteLine("[TVLauncher] Abriendo SetupWizardWindow...");
                    var setupWizard = ServiceProvider.GetRequiredService<SetupWizardWindow>();
                    desktop.MainWindow = setupWizard;
                    Console.WriteLine("[TVLauncher] SetupWizardWindow asignado");
                }
                else
                {
                    Console.WriteLine("[TVLauncher] Abriendo MainWindow...");
                    var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                    desktop.MainWindow = mainWindow;
                    Console.WriteLine("[TVLauncher] MainWindow asignado correctamente");
                }
            }
            else
            {
                Console.WriteLine("[TVLauncher] ERROR: ApplicationLifetime no es IClassicDesktopStyleApplicationLifetime");
            }

            base.OnFrameworkInitializationCompleted();
            Console.WriteLine("[TVLauncher] OnFrameworkInitializationCompleted finalizado");
        }
        catch (Exception ex)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("[TVLauncher] ERROR CRÍTICO EN INICIALIZACIÓN");
            Console.WriteLine("========================================");
            Console.WriteLine($"Exception: {ex.GetType().Name}");
            Console.WriteLine($"Message: {ex.Message}");
            Console.WriteLine($"StackTrace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine("----------------------------------------");
                Console.WriteLine($"InnerException: {ex.InnerException.GetType().Name}");
                Console.WriteLine($"InnerMessage: {ex.InnerException.Message}");
                Console.WriteLine($"InnerStackTrace: {ex.InnerException.StackTrace}");
            }
            Console.WriteLine("========================================");
            
            // Intentar mostrar ventana de error si es posible
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
