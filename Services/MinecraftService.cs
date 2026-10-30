using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TVLauncher.Helpers;
using TVLauncher.Models;

namespace TVLauncher.Services;

public sealed class GameLogEventArgs : EventArgs
{
    public string Line { get; }
    public bool IsError { get; }

    public GameLogEventArgs(string line, bool isError)
    {
        Line = line;
        IsError = isError;
    }
}

/// <summary>
/// Orquesta el lanzamiento de Minecraft: prepara archivos, elige Java,
/// construye argumentos, arranca el proceso y captura sus logs en tiempo real.
/// </summary>
public class MinecraftService
{
    private readonly ConfigService _configService;
    private readonly InstanceManager _instanceManager;
    private readonly AuthService _authService;
    private readonly JavaService _javaService;
    private readonly GameFilesService _gameFilesService;
    private readonly LaunchArgumentsBuilder _argumentsBuilder = new();

    private readonly object _processLock = new();
    private readonly object _logLock = new();
    private readonly string _gameLogPath;
    private Process? _process;

    // Datos de la última ejecución para localizar crash-reports/hs_err al salir.
    private string? _lastGameDirectory;
    private string? _lastVersionId;

    // Distingue un cierre pedido por el usuario (StopGame) de un fallo real:
    // solo el segundo debe volcar diagnósticos de crash.
    private volatile bool _stopRequested;

    public event EventHandler<string>? DownloadProgressChanged;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<GameLogEventArgs>? GameLogReceived;
    public event EventHandler<int>? GameExited;
    public event EventHandler<int>? LaunchProgressChanged;

    public bool IsGameRunning { get; private set; }
    public int? LastExitCode { get; private set; }
    public int? RunningProcessId { get; private set; }

    public MinecraftService(
        ConfigService configService,
        InstanceManager instanceManager,
        AuthService authService,
        JavaService javaService,
        GameFilesService gameFilesService)
    {
        _configService = configService;
        _instanceManager = instanceManager;
        _authService = authService;
        _javaService = javaService;
        _gameFilesService = gameFilesService;

        _gameLogPath = Path.Combine(
            Path.GetDirectoryName(Log.LogFile) ?? Path.GetTempPath(),
            "minecraft-console.log");

        // Reenviar progreso de descargas a la UI
        _gameFilesService.StatusChanged += (_, msg) => StatusChanged?.Invoke(this, msg);
        _gameFilesService.DownloadProgressChanged += (_, msg) => DownloadProgressChanged?.Invoke(this, msg);
        _gameFilesService.ProgressChanged += (_, percent) => LaunchProgressChanged?.Invoke(this, percent);
        _javaService.StatusChanged += (_, msg) => StatusChanged?.Invoke(this, msg);
    }

    // ---------- Lanzamiento ----------

    public async Task LaunchMinecraftAsync(MinecraftInstance instance, CancellationToken cancellationToken = default)
    {
        if (IsGameRunning)
        {
            StatusChanged?.Invoke(this, "Minecraft ya está en ejecución");
            return;
        }

        try
        {
            _stopRequested = false;

            if (_configService.Config.AutoOptimizeMemory)
            {
                StatusChanged?.Invoke(this, "Optimizando memoria...");
                _configService.OptimizeMemory();
            }

            var session = _authService.CurrentSession
                ?? throw new InvalidOperationException("No hay cuenta activa. Inicia sesión antes de jugar.");

            var account = _authService.ActiveAccount;
            bool onlineAccount = account is { Type: AccountType.Microsoft };

            // 0. Modloader: si la instancia usa Fabric y aún no apunta al id "fabric-loader-...",
            //    instalar el perfil y actualizar la instancia para que quede persistente.
            string versionToLaunch = instance.Version;
            if (IsFabric(instance) && !versionToLaunch.StartsWith("fabric-loader", StringComparison.OrdinalIgnoreCase))
            {
                var loaderVersion = string.IsNullOrWhiteSpace(instance.ModLoaderVersion)
                    ? await _gameFilesService.GetLatestFabricLoaderAsync(versionToLaunch, cancellationToken)
                    : instance.ModLoaderVersion;

                versionToLaunch = await _gameFilesService.InstallFabricAsync(
                    versionToLaunch, loaderVersion ?? "latest", cancellationToken);

                Log.Info($"[Launch] Fabric instalado; id de versión: {versionToLaunch}");
            }
            else if (IsForge(instance) && !IsForgeVersion(versionToLaunch))
            {
                // Forge/NeoForge: el instalador oficial construye el perfil y los parches
                // del cliente (también en versiones legacy como 1.12.2).
                int installerJavaMajor = JavaService.GetRequiredJavaMajor(versionToLaunch);
                var installerJava = await _javaService.ResolveAsync(
                    instance.JavaPath, installerJavaMajor, cancellationToken);

                versionToLaunch = await _gameFilesService.InstallForgeAsync(
                    versionToLaunch, instance.ModLoaderVersion, IsNeoForge(instance),
                    installerJava.Path, cancellationToken);

                Log.Info($"[Launch] {(IsNeoForge(instance) ? "NeoForge" : "Forge")} instalado; id de versión: {versionToLaunch}");
            }

            // Recordar el id efectivo para las siguientes ejecuciones (sin descargar nada más)
            if (!versionToLaunch.Equals(instance.Version, StringComparison.OrdinalIgnoreCase))
            {
                instance.Version = versionToLaunch;
                _instanceManager.UpdateInstance(instance);
            }

            // 1. Archivos del juego (version.json, client.jar, librerías, natives, assets)
            StatusChanged?.Invoke(this, "Preparando archivos del juego...");
            var installation = await _gameFilesService.EnsureVersionAsync(versionToLaunch, cancellationToken);

            // 2. Java compatible
            int requiredJava = installation.RequiredJavaMajor > 0
                ? installation.RequiredJavaMajor
                : JavaService.GetRequiredJavaMajor(instance.Version);

            StatusChanged?.Invoke(this, $"Verificando Java {requiredJava}...");
            string? preferredJava = string.IsNullOrWhiteSpace(instance.JavaPath) ? null : instance.JavaPath;
            var java = await _javaService.ResolveAsync(preferredJava, requiredJava, cancellationToken);

            StatusChanged?.Invoke(this, $"Java seleccionado: {java.Version} ({java.Path})");

            // 3. Argumentos
            var plan = _argumentsBuilder.Build(new LaunchContext
            {
                Instance = instance,
                Username = session.Username,
                Uuid = session.UUID,
                AccessToken = string.IsNullOrWhiteSpace(session.AccessToken) ? "0" : session.AccessToken,
                IsOnlineAccount = onlineAccount,
                Installation = installation,
                JavaPath = java.Path
            });

            Log.Info($"[Launch] Comando: {plan.ToLogString()}");

            // Cabecera de la ejecución en el log del juego: permite saber exactamente
            // con qué versión, Java, classpath y argumentos se lanzó cada intento.
            _lastGameDirectory = instance.GameDirectory;
            _lastVersionId = versionToLaunch;
            AppendGameLog($"===== TVLauncher: iniciando {versionToLaunch} =====");
            AppendGameLog($"Java: {plan.JavaPath}");
            AppendGameLog($"Directorio de juego: {plan.WorkingDirectory}");
            AppendGameLog($"Nativos: {installation.NativesDirectory}");
            AppendGameLog($"Classpath: {plan.Classpath.Split(Path.PathSeparator).Length} entradas");
            AppendGameLog($"Comando: {plan.ToLogString()}");

            // 4. Proceso
            var startInfo = new ProcessStartInfo
            {
                FileName = plan.JavaPath,
                WorkingDirectory = plan.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var argument in plan.Arguments)
                startInfo.ArgumentList.Add(argument);

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, e) => OnGameOutput(e.Data, isError: false);
            process.ErrorDataReceived += (_, e) => OnGameOutput(e.Data, isError: true);
            process.Exited += (_, _) => OnGameExited(process);

            StatusChanged?.Invoke(this, $"Iniciando Minecraft {instance.Version}...");
            if (!process.Start())
                throw new InvalidOperationException("No se pudo iniciar el proceso de Java.");

            lock (_processLock)
            {
                _process = process;
            }

            IsGameRunning = true;
            RunningProcessId = process.Id;
            LastExitCode = null;

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            instance.LastPlayed = DateTime.Now;
            _instanceManager.UpdateInstance(instance);

            OnGameOutput($"--- TVLauncher: Minecraft {instance.Version} iniciado (PID {process.Id}) ---", false);
            StatusChanged?.Invoke(this, $"Minecraft iniciado correctamente (PID {process.Id})");
        }
        catch (OperationCanceledException)
        {
            IsGameRunning = false;
            StatusChanged?.Invoke(this, "Lanzamiento cancelado");
            throw;
        }
        catch (Exception ex)
        {
            IsGameRunning = false;
            RunningProcessId = null;
            StatusChanged?.Invoke(this, $"Error al lanzar Minecraft: {ex.Message}");
            Log.Error($"[Launch] Error: {ex}");
            throw;
        }
    }

    public void StopGame()
    {
        lock (_processLock)
        {
            if (_process is not { HasExited: false })
                return;

            _stopRequested = true;

            try
            {
                StatusChanged?.Invoke(this, "Cerrando Minecraft...");
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Log.Warn($"[Launch] No se pudo cerrar Minecraft: {ex.Message}");
            }
        }
    }

    // ---------- Logs del juego ----------

    private void OnGameOutput(string? line, bool isError)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        // stderr contiene los stack traces de la JVM (p. ej. ResolutionException):
        // se marca para que el volcado se pueda localizar de un vistazo.
        AppendGameLog(isError ? $"[stderr] {line}" : line);

        if (isError)
            Log.Warn($"[Juego] {line}");

        GameLogReceived?.Invoke(this, new GameLogEventArgs(line, isError));
    }

    private void AppendGameLog(string line)
    {
        try
        {
            lock (_logLock)
            {
                string? directory = Path.GetDirectoryName(_gameLogPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                // Rotar en vez de borrar: si el juego falla, el log de la sesión anterior
                // (y su stack trace) sigue disponible para diagnóstico.
                if (File.Exists(_gameLogPath) && new FileInfo(_gameLogPath).Length > 5 * 1024 * 1024)
                {
                    string oldLogPath = Path.ChangeExtension(_gameLogPath, ".old.log");
                    File.Move(_gameLogPath, oldLogPath, overwrite: true);
                }

                File.AppendAllText(_gameLogPath, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // El log del juego nunca debe romper el lanzamiento
        }
    }

    private void OnGameExited(Process process)
    {
        // Esperar a que los lectores asíncronos vacíen stdout/stderr antes de leer el
        // código de salida y buscar crash-reports: si no, el log puede perder las
        // últimas líneas, que son justo donde suele estar la causa del cierre.
        try
        {
            // WaitForExit(int) no garantiza que los lectores asíncronos hayan
            // terminado; la sobrecarga sin parámetros sí espera a vaciar stdout/stderr.
            if (process.WaitForExit(5000))
                process.WaitForExit();
        }
        catch (Exception ex)
        {
            Log.Warn($"[Launch] No se pudo esperar al cierre del proceso: {ex.Message}");
        }

        int exitCode = -1;
        try
        {
            exitCode = process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Warn($"[Launch] No se pudo leer el código de salida: {ex.Message}");
        }

        LastExitCode = exitCode;
        IsGameRunning = false;
        RunningProcessId = null;

        lock (_processLock)
        {
            if (ReferenceEquals(_process, process))
                _process = null;
        }

        bool userStopped = _stopRequested;
        OnGameOutput($"--- TVLauncher: Minecraft finalizado (código {exitCode}) ---", exitCode != 0 && !userStopped);

        if (exitCode != 0 && !userStopped)
        {
            Log.Error($"[Launch] Minecraft ({_lastVersionId ?? "desconocida"}) terminó con código {exitCode}.");
            ReportCrashDiagnostics(exitCode);
        }
        else if (userStopped)
        {
            // Cierre pedido por el usuario: no es un fallo, no se vuelcan diagnósticos.
            Log.Info($"[Launch] Minecraft ({_lastVersionId ?? "desconocida"}) cerrado por el usuario (código {exitCode}).");
        }

        StatusChanged?.Invoke(this, exitCode == 0
            ? "Minecraft se cerró correctamente"
            : userStopped
                ? "Minecraft cerrado"
                : $"Minecraft finalizó con código {exitCode}. Revisa el log del juego.");

        GameExited?.Invoke(this, exitCode);
    }

    /// <summary>
    /// Vuelca en minecraft-console.log y tvlauncher.log el crash-report o el hs_err
    /// generado por la ejecución que acaba de fallar. Si el fallo ocurre antes de que
    /// Minecraft arranque (p. ej. ResolutionException de ModLauncher) no hay
    /// crash-report: en ese caso el stderr ya capturado contiene el stack trace exacto.
    /// </summary>
    private void ReportCrashDiagnostics(int exitCode)
    {
        try
        {
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(_lastGameDirectory))
            {
                string crashDirectory = Path.Combine(_lastGameDirectory, "crash-reports");
                if (Directory.Exists(crashDirectory))
                {
                    var report = new DirectoryInfo(crashDirectory)
                        .GetFiles("crash-*.txt")
                        .OrderByDescending(file => file.LastWriteTimeUtc)
                        .FirstOrDefault();

                    if (report != null && DateTime.UtcNow - report.LastWriteTimeUtc < TimeSpan.FromMinutes(15))
                        candidates.Add(report.FullName);
                }

                // Crashes nativos de la JVM (EXCEPTION_ACCESS_VIOLATION, etc.)
                var hsErr = new DirectoryInfo(_lastGameDirectory)
                    .GetFiles("hs_err_pid*.log")
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (hsErr != null && DateTime.UtcNow - hsErr.LastWriteTimeUtc < TimeSpan.FromMinutes(15))
                    candidates.Add(hsErr.FullName);
            }

            if (candidates.Count == 0)
            {
                AppendGameLog("--- TVLauncher: sin crash-report; la causa está en el volcado [stderr] anterior ---");
                Log.Warn($"[Crash] Minecraft terminó con código {exitCode} sin crash-report; revisa minecraft-console.log");
                return;
            }

            foreach (string file in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string content = File.ReadAllText(file);
                const int maxLength = 256 * 1024;
                if (content.Length > maxLength)
                    content = content[..maxLength] + Environment.NewLine + "... (crash-report truncado)";

                string name = Path.GetFileName(file);
                Log.Error($"[Crash] {name}:{Environment.NewLine}{content}");

                AppendGameLog($"===== {name} =====");
                foreach (string line in content.Split('\n'))
                    AppendGameLog(line.TrimEnd('\r'));
                AppendGameLog($"===== fin de {name} =====");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[Crash] No se pudieron adjuntar los diagnósticos del fallo: {ex.Message}");
        }
    }

    // ---------- Versiones ----------

    public async Task<IReadOnlyList<VersionManifestEntry>> GetVersionListAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            StatusChanged?.Invoke(this, "Cargando lista de versiones...");
            var manifest = await _gameFilesService.GetManifestAsync(cancellationToken);
            StatusChanged?.Invoke(this, $"{manifest.Versions.Count} versiones disponibles");
            return manifest.Versions;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error al cargar versiones: {ex.Message}");
            throw;
        }
    }

    public async Task InstallVersionAsync(string versionId, CancellationToken cancellationToken = default)
    {
        try
        {
            StatusChanged?.Invoke(this, $"Instalando versión {versionId}...");
            await _gameFilesService.EnsureVersionAsync(versionId, cancellationToken);
            StatusChanged?.Invoke(this, $"Versión {versionId} instalada correctamente");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error al instalar {versionId}: {ex.Message}");
            throw;
        }
    }

    private static bool IsFabric(MinecraftInstance instance)
        => instance.ModLoader.Equals("Fabric", StringComparison.OrdinalIgnoreCase);

    private static bool IsNeoForge(MinecraftInstance instance)
        => instance.ModLoader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase);

    private static bool IsForge(MinecraftInstance instance)
        => instance.ModLoader.Equals("Forge", StringComparison.OrdinalIgnoreCase) || IsNeoForge(instance);

    /// <summary>
    /// Indica si el id de versión ya corresponde al perfil de Forge/NeoForge instalado.
    /// NeoForge 1.20.1 usa el artefacto heredado y su perfil se llama
    /// <c>1.20.1-forge-47.x.y</c>, así que basta con comprobar "forge" en ambos casos.
    /// </summary>
    private static bool IsForgeVersion(string versionId)
        => versionId.Contains("forge", StringComparison.OrdinalIgnoreCase);

    public async Task InstallModLoaderAsync(MinecraftInstance instance)
    {
        if (instance.ModLoader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
            return;

        if (IsFabric(instance) && !instance.Version.StartsWith("fabric-loader", StringComparison.OrdinalIgnoreCase))
        {
            var loaderVersion = string.IsNullOrWhiteSpace(instance.ModLoaderVersion)
                ? await _gameFilesService.GetLatestFabricLoaderAsync(instance.Version)
                : instance.ModLoaderVersion;

            string versionId = await _gameFilesService.InstallFabricAsync(
                instance.Version, loaderVersion ?? "latest");

            instance.Version = versionId;
            _instanceManager.UpdateInstance(instance);
            StatusChanged?.Invoke(this, $"Fabric instalado ({versionId})");
            return;
        }

        if (IsForge(instance) && !IsForgeVersion(instance.Version))
        {
            int requiredJava = JavaService.GetRequiredJavaMajor(instance.Version);
            var java = await _javaService.ResolveAsync(instance.JavaPath, requiredJava);

            string versionId = await _gameFilesService.InstallForgeAsync(
                instance.Version, instance.ModLoaderVersion, IsNeoForge(instance), java.Path);

            instance.Version = versionId;
            _instanceManager.UpdateInstance(instance);
            StatusChanged?.Invoke(this, $"{(IsNeoForge(instance) ? "NeoForge" : "Forge")} instalado ({versionId})");
            return;
        }

        StatusChanged?.Invoke(this,
            $"La instalación automática de {instance.ModLoader} aún no está disponible; " +
            "usa una versión ya instalada en la carpeta versions.");
    }

    public bool IsVersionInstalled(string versionId) => _gameFilesService.IsVersionInstalled(versionId);
}
