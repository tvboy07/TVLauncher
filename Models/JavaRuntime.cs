using System;

namespace TVLauncher.Models;

/// <summary>
/// Instalación de Java detectada y validada (se ejecuta <c>java -version</c> para comprobarla).
/// </summary>
public sealed class JavaRuntime
{
    public string Path { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public int MajorVersion { get; init; }
    public bool Is64Bit { get; init; }
    public string Vendor { get; init; } = string.Empty;

    public bool IsValid => !string.IsNullOrWhiteSpace(Path) && MajorVersion > 0;

    public override string ToString()
        => $"Java {Version} ({(Is64Bit ? "64-bit" : "32-bit")}) - {Path}";
}
