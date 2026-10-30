using System.IO;

namespace TVLauncher.Tests;

/// <summary>
/// Carpeta temporal aislada por test. Evita que las pruebas toquen
/// <c>%LOCALAPPDATA%\TVLauncher</c> o los archivos reales del juego.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Root { get; }

    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "tvl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Combine(params string[] parts)
        => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    public string WriteFile(string relativePath, string content)
    {
        string full = Combine(relativePath.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // Un archivo bloqueado por el SO no debe hacer fallar el test.
        }
    }
}
