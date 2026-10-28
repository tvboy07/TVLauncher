using System;
using System.IO;

namespace TVLauncher.Services;

/// <summary>
/// Logging sencillo a consola y a archivo, para diagnosticar problemas de
/// inicialización/renderizado cuando la app se ejecuta sin consola visible.
/// </summary>
public static class Log
{
    private static readonly object _lock = new();
    private static readonly string _logDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TVLauncher", "logs");
    private static readonly string _logFile = Path.Combine(_logDir, "tvlauncher.log");

    public static string LogFile => _logFile;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
        Console.WriteLine(line);

        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(_logDir);
                if (File.Exists(_logFile) && new FileInfo(_logFile).Length > 1024 * 512)
                {
                    var old = _logFile + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(_logFile, old);
                }
                File.AppendAllText(_logFile, line + Environment.NewLine);
            }
        }
        catch
        {
            // El logging nunca debe tumbar la aplicación.
        }
    }
}
