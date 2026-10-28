using Avalonia;
using System;

namespace TVLauncher;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            Console.WriteLine("[TVLauncher] Iniciando programa...");
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
            Console.WriteLine("[TVLauncher] Programa finalizado");
        }
        catch (Exception ex)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("[TVLauncher] ERROR CRÍTICO EN MAIN");
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
            Console.ReadKey();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
