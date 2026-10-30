using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TVLauncher.Models;

namespace TVLauncher.Services;

/// <summary>
/// Detección, validación y selección de instalaciones de Java para lanzar Minecraft.
/// Orden de prioridad: ruta configurada -> JAVA_HOME -> PATH -> instalaciones conocidas del sistema.
/// </summary>
public class JavaService
{
    private static readonly Regex VersionRegex = new("version\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ArchRegex = new("(64|32)-Bit", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ConfigService _configService;
    private readonly SemaphoreSlim _detectLock = new(1, 1);
    private readonly Dictionary<string, JavaRuntime?> _validatedCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<JavaRuntime> _detected = new();
    private bool _scanned;

    public IReadOnlyList<JavaRuntime> Detected => _detected;
    public event EventHandler<string>? StatusChanged;

    public JavaService(ConfigService configService)
    {
        _configService = configService;
    }

    /// <summary>Busca y valida todas las instalaciones de Java del sistema.</summary>
    public async Task<IReadOnlyList<JavaRuntime>> DetectAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        await _detectLock.WaitAsync(cancellationToken);
        try
        {
            if (_scanned && !forceRefresh)
                return _detected;

            StatusChanged?.Invoke(this, "Buscando instalaciones de Java...");
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Ruta configurada por el usuario
            AddCandidate(candidates, _configService.Config.JavaPath);

            // 2. JAVA_HOME
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrWhiteSpace(javaHome))
                AddCandidate(candidates, System.IO.Path.Combine(javaHome, "bin", "java.exe"));

            // 3. PATH
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathVar.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    AddCandidate(candidates, System.IO.Path.Combine(dir.Trim('"'), "java.exe"));
                }
                catch
                {
                    // Entradas de PATH inválidas se ignoran
                }
            }

            // 4. Instalaciones conocidas (Oracle, Adoptium, Microsoft, Zulu, Corretto, launcher de Mojang...)
            foreach (var root in GetKnownJavaRoots())
                ScanRoot(candidates, root);

            var validationTasks = candidates.Select(ValidateAsync);
            var results = await Task.WhenAll(validationTasks);

            _detected.Clear();
            _detected.AddRange(results
                .Where(r => r is { IsValid: true })
                .Cast<JavaRuntime>()
                .GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(r => r.MajorVersion)
                .ThenByDescending(r => r.Is64Bit));

            _scanned = true;
            Log.Info($"[Java] {_detected.Count} instalaciones detectadas: " +
                     string.Join(", ", _detected.Select(r => $"{r.MajorVersion}({r.Path})")));
            StatusChanged?.Invoke(this, _detected.Count == 0
                ? "No se encontró ningún Java instalado"
                : $"{_detected.Count} instalaciones de Java detectadas");

            return _detected;
        }
        finally
        {
            _detectLock.Release();
        }
    }

    /// <summary>
    /// Devuelve el mejor Java disponible para la versión mayor requerida.
    /// Si <paramref name="preferredPath"/> es válido y compatible, se usa tal cual.
    /// </summary>
    public async Task<JavaRuntime> ResolveAsync(string? preferredPath, int requiredMajor, CancellationToken cancellationToken = default)
    {
        // 1. Ruta explícita (instancia o configuración global)
        var candidatePath = !string.IsNullOrWhiteSpace(preferredPath)
            ? preferredPath
            : _configService.Config.JavaPath;

        if (!string.IsNullOrWhiteSpace(candidatePath))
        {
            var preferred = await ValidateAsync(candidatePath);
            if (preferred is { IsValid: true })
            {
                if (requiredMajor <= 0 || preferred.MajorVersion >= requiredMajor)
                {
                    Log.Info($"[Java] Usando Java configurado: {preferred}");
                    return preferred;
                }

                Log.Warn($"[Java] El Java configurado ({preferred.MajorVersion}) es anterior al requerido ({requiredMajor}); se buscará otra instalación");
            }
            else
            {
                Log.Warn($"[Java] La ruta configurada no es un Java válido: {candidatePath}");
            }
        }

        // 2. Búsqueda automática
        var all = await DetectAsync(forceRefresh: !_scanned, cancellationToken);

        JavaRuntime? best = null;
        if (requiredMajor > 0)
        {
            best = all
                .Where(r => r.MajorVersion >= requiredMajor)
                .OrderBy(r => r.MajorVersion)          // la más cercana a la requerida (no la más nueva)
                .ThenByDescending(r => r.Is64Bit)
                .FirstOrDefault();
        }
        else
        {
            best = all.OrderByDescending(r => r.MajorVersion).FirstOrDefault();
        }

        if (best == null)
        {
            var available = all.Count == 0
                ? "no se detectó ninguna instalación"
                : $"detectadas: {string.Join(", ", all.Select(r => $"Java {r.MajorVersion}"))}";

            throw new InvalidOperationException(
                $"Se requiere Java {requiredMajor} o superior para esta versión y {available}. " +
                "Instálalo o indica la ruta en Configuración > Java.");
        }

        Log.Info($"[Java] Seleccionado automáticamente: {best}");
        return best;
    }

    /// <summary>Versión mayor de Java recomendada cuando la metadata no la indica.</summary>
    public static int GetRequiredJavaMajor(string versionId)
    {
        if (TryParseMinecraftVersion(versionId, out var major, out var minor))
        {
            if (major > 1) return 21;          // 26.x en adelante
            if (minor >= 21) return 21;
            if (minor >= 18) return 17;
            if (minor == 17) return 16;
            return 8;
        }

        // Snapshots / ids desconocidos: asumir moderno
        return 21;
    }

    private static bool TryParseMinecraftVersion(string versionId, out int major, out int minor)
    {
        major = minor = 0;
        if (string.IsNullOrWhiteSpace(versionId))
            return false;

        var parts = versionId.Split('.');
        if (parts.Length < 2)
            return false;

        return int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor);
    }

    /// <summary>Comprueba un ejecutable de Java ejecutando <c>java -version</c>.</summary>
    public async Task<JavaRuntime?> ValidateAsync(string? javaPath)
    {
        if (string.IsNullOrWhiteSpace(javaPath))
            return null;

        string fullPath;
        try
        {
            fullPath = System.IO.Path.GetFullPath(javaPath);
        }
        catch
        {
            return null;
        }

        lock (_validatedCache)
        {
            if (_validatedCache.TryGetValue(fullPath, out var cached))
                return cached;
        }

        JavaRuntime? runtime = null;
        if (File.Exists(fullPath))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fullPath,
                    Arguments = "-version",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                var stderrTask = process.StandardError.ReadToEndAsync();
                var stdoutTask = process.StandardOutput.ReadToEndAsync();

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    Log.Warn($"[Java] Tiempo de espera agotado validando {fullPath}");
                    return null;
                }

                string output = (await stderrTask) + (await stdoutTask);
                runtime = ParseVersionOutput(fullPath, output);
            }
            catch (Exception ex)
            {
                Log.Warn($"[Java] No se pudo validar '{fullPath}': {ex.Message}");
            }
        }

        lock (_validatedCache)
        {
            _validatedCache[fullPath] = runtime;
        }

        return runtime;
    }

    private static JavaRuntime? ParseVersionOutput(string path, string output)
    {
        var versionMatch = VersionRegex.Match(output);
        if (!versionMatch.Success)
            return null;

        string version = versionMatch.Groups[1].Value;
        var parts = version.Split('.', '_', '-', '+');

        int major;
        if (parts.Length > 1 && parts[0] == "1")
        {
            // Formato antiguo: 1.8.0_381 -> 8
            if (!int.TryParse(parts[1], out major))
                return null;
        }
        else if (!int.TryParse(parts[0], out major))
        {
            return null;
        }

        var archMatch = ArchRegex.Match(output);
        bool is64 = archMatch.Success
            ? archMatch.Groups[1].Value == "64"
            : Environment.Is64BitOperatingSystem;

        string vendor = "Desconocido";
        if (output.Contains("Temurin", StringComparison.OrdinalIgnoreCase)) vendor = "Eclipse Temurin";
        else if (output.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) vendor = "Microsoft";
        else if (output.Contains("Zulu", StringComparison.OrdinalIgnoreCase)) vendor = "Azul Zulu";
        else if (output.Contains("Corretto", StringComparison.OrdinalIgnoreCase)) vendor = "Amazon Corretto";
        else if (output.Contains("OpenJDK", StringComparison.OrdinalIgnoreCase)) vendor = "OpenJDK";
        else if (output.Contains("Oracle", StringComparison.OrdinalIgnoreCase)) vendor = "Oracle";

        return new JavaRuntime
        {
            Path = path,
            Version = version,
            MajorVersion = major,
            Is64Bit = is64,
            Vendor = vendor
        };
    }

    private static void AddCandidate(ISet<string> candidates, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            // Si apunta a un directorio, completar con bin\java.exe
            if (Directory.Exists(path))
                path = System.IO.Path.Combine(path, "bin", "java.exe");

            if (File.Exists(path))
                candidates.Add(System.IO.Path.GetFullPath(path));
        }
        catch
        {
            // Ignorar rutas inválidas
        }
    }

    private static void ScanRoot(ISet<string> candidates, string root)
    {
        try
        {
            if (!Directory.Exists(root))
                return;

            AddCandidate(candidates, System.IO.Path.Combine(root, "bin", "java.exe"));

            // Buscar java.exe hasta 4 niveles (runtime\<componente>\<plataforma>\<componente>\bin)
            var pending = new Queue<(string Dir, int Depth)>();
            pending.Enqueue((root, 0));

            while (pending.Count > 0)
            {
                var (dir, depth) = pending.Dequeue();
                if (depth >= 4)
                    continue;

                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    AddCandidate(candidates, System.IO.Path.Combine(sub, "bin", "java.exe"));
                    pending.Enqueue((sub, depth + 1));
                }
            }
        }
        catch
        {
            // Directorios protegidos o inaccesibles: continuar
        }
    }

    private static IEnumerable<string> GetKnownJavaRoots()
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        string[] roots =
        {
            System.IO.Path.Combine(pf, "Java"),
            System.IO.Path.Combine(pf, "Eclipse Adoptium"),
            System.IO.Path.Combine(pf, "Eclipse Foundation"),
            System.IO.Path.Combine(pf, "Microsoft"),
            System.IO.Path.Combine(pf, "Zulu"),
            System.IO.Path.Combine(pf, "BellSoft"),
            System.IO.Path.Combine(pf, "Amazon Corretto"),
            System.IO.Path.Combine(pf, "Semeru"),
            System.IO.Path.Combine(pf, "AdoptOpenJDK"),
            System.IO.Path.Combine(pf, "GraalVM"),
            System.IO.Path.Combine(local, "Programs", "Eclipse Adoptium"),
            System.IO.Path.Combine(local, "Programs", "Microsoft"),
            System.IO.Path.Combine(roaming, ".minecraft", "runtime"),
            System.IO.Path.Combine(pf86, "Minecraft Launcher", "runtime")
        };

        foreach (var root in roots)
            yield return root;
    }
}
