using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using TVLauncher.Helpers;
using TVLauncher.Models;

namespace TVLauncher.Services;

/// <summary>
/// Descarga y prepara los archivos necesarios para lanzar una versión:
/// version.json, client.jar, librerías, nativas, assets y configuración de logging.
/// </summary>
public class GameFilesService
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string ResourcesUrl = "https://resources.download.minecraft.net";
    private static readonly TimeSpan ManifestMaxAge = TimeSpan.FromHours(6);

    /// <summary>
    /// Mínimo de entradas que debe contener una caché del manifiesto para considerarse completa.
    /// El manifiesto v2 oficial supera ampliamente esta cifra (incluye legacy alpha/beta).
    /// </summary>
    private const int MinimumManifestVersions = 500;

    private readonly ConfigService _configService;
    private readonly HttpClient _http;
    private readonly string _basePath;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<int>? ProgressChanged;
    public event EventHandler<string>? DownloadProgressChanged;

    public GameFilesService(ConfigService configService)
    {
        _configService = configService;

        string dataPath = configService.Config.DataPath;
        _basePath = string.IsNullOrWhiteSpace(dataPath) ? configService.GetCachePath() : dataPath;

        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 32,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TVLauncher/1.0.0");
    }

    public string BasePath => _basePath;
    public string VersionsDirectory => Path.Combine(_basePath, "versions");
    public string LibrariesDirectory => Path.Combine(_basePath, "libraries");
    public string AssetsDirectory => Path.Combine(_basePath, "assets");
    public string NativesDirectory(string versionId) => Path.Combine(_basePath, "natives", versionId);

    public bool IsVersionInstalled(string versionId)
    {
        string json = Path.Combine(VersionsDirectory, versionId, $"{versionId}.json");
        string jar = Path.Combine(VersionsDirectory, versionId, $"{versionId}.jar");
        return File.Exists(json) && File.Exists(jar);
    }

    /// <summary>
    /// Instala Fabric: descarga el version.json desde meta.fabricmc.net y lo guarda en
    /// versions/{fabric-loader-{loader}-{mc}}/{id}.json. Devuelve el id resultante.
    /// </summary>
    public async Task<string> InstallFabricAsync(string minecraftVersion, string loaderVersion,
        CancellationToken cancellationToken = default)
    {
        string loader = string.IsNullOrWhiteSpace(loaderVersion) ? "latest" : loaderVersion;
        string url = $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(minecraftVersion)}/{Uri.EscapeDataString(loader)}/profile/json";

        string versionId;
        string directory;
        string path;

        try
        {
            StatusChanged?.Invoke(this, $"Descargando Fabric ({minecraftVersion}, loader {loader})...");
            string json = await _http.GetStringAsync(url, cancellationToken);

            var profile = JsonConvert.DeserializeObject<MinecraftVersionJson>(json)
                          ?? throw new InvalidOperationException("Respuesta inválida de meta.fabricmc.net");

            versionId = profile.Id;
            if (string.IsNullOrWhiteSpace(versionId))
                throw new InvalidOperationException("El perfil de Fabric no define id");

            // Si el usuario pidió una versión concreta no-"latest", normalizar al id real
            // pero reutilizar la metadata ya descargada si es la misma.
            directory = Path.Combine(VersionsDirectory, versionId);
            path = Path.Combine(directory, $"{versionId}.json");
            Directory.CreateDirectory(directory);

            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                await File.WriteAllTextAsync(path, json, cancellationToken);

            Log.Info($"[GameFiles] Fabric instalado: {versionId}");
            return versionId;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"No se pudo instalar Fabric {loaderVersion} para {minecraftVersion}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Devuelve la última versión del loader de Fabric publicada en meta.fabricmc.net.
    /// </summary>
    public async Task<string?> GetLatestFabricLoaderAsync(string minecraftVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            string json = await _http.GetStringAsync(
                $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(minecraftVersion)}", cancellationToken);
            var entries = JsonConvert.DeserializeObject<List<FabricLoaderEntry>>(json);
            return entries?
                .OrderByDescending(e => e.Order ?? 0)
                .Select(e => e.Loader?.Version)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }
        catch (Exception ex)
        {
            Log.Warn($"[GameFiles] No se pudo consultar el loader de Fabric: {ex.Message}");
            return null;
        }
    }

    private class FabricLoaderEntry
    {
        [Newtonsoft.Json.JsonProperty("loader")]
        public FabricLoader Loader { get; set; } = new();

        [Newtonsoft.Json.JsonProperty("intermediary")]
        public FabricIntermediary Intermediary { get; set; } = new();

        [Newtonsoft.Json.JsonProperty("source")]
        public string Source { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonProperty("order")]
        public int? Order { get; set; }
    }

    private class FabricLoader
    {
        [Newtonsoft.Json.JsonProperty("version")]
        public string? Version { get; set; } = string.Empty;
    }

    private class FabricIntermediary
    {
        [Newtonsoft.Json.JsonProperty("version")]
        public string Version { get; set; } = string.Empty;
    }

    // ---------- Forge / NeoForge ----------

    private const string ForgeMavenBase = "https://maven.minecraftforge.net/";
    private const string NeoForgeMavenBase = "https://maven.neoforged.net/releases/";

    private static readonly Regex NumericPartRegex = new(@"\d+", RegexOptions.Compiled);

    /// <summary>
    /// Devuelve las versiones publicadas del loader de Forge/NeoForge compatibles con
    /// <paramref name="minecraftVersion"/>, de la más reciente a la más antigua.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetForgeLoaderVersionsAsync(string minecraftVersion, bool neoForge,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (url, prefix) = GetForgeMetadataSource(minecraftVersion, neoForge);
            string xml = await _http.GetStringAsync(url, cancellationToken);
            var document = XDocument.Parse(xml);

            // El orden de maven-metadata no es fiable (Forge publica de más reciente a
            // más antigua y NeoForge al revés según el artefacto), así que ordenamos
            // numéricamente de forma descendente para preseleccionar la última versión.
            var versions = document.Descendants("version")
                .Select(e => e.Value.Trim())
                .Where(v => !string.IsNullOrEmpty(v) &&
                            v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(v => BuildVersionSortKey(v, prefix))
                .ThenByDescending(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Log.Info($"[GameFiles] {versions.Count} versiones de {(neoForge ? "NeoForge" : "Forge")} para {minecraftVersion}");
            return versions;
        }
        catch (Exception ex)
        {
            Log.Warn($"[GameFiles] No se pudieron obtener las versiones de " +
                     $"{(neoForge ? "NeoForge" : "Forge")}: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Instala Forge/NeoForge ejecutando su instalador oficial con Java. El instalador
    /// resuelve los procesadores, los parches del cliente y las bibliotecas Maven para
    /// cualquier versión (moderna o legacy, p. ej. 1.12.2) y deja el perfil listo en
    /// <c>versions/{id}</c>. Devuelve el id de versión instalado.
    /// </summary>
    public async Task<string> InstallForgeAsync(string minecraftVersion, string loaderVersion, bool neoForge,
        string javaPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            throw new ArgumentException("La versión de Minecraft no puede estar vacía", nameof(minecraftVersion));

        if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath))
            throw new InvalidOperationException("Se necesita un Java válido para ejecutar el instalador de Forge.");

        if (string.IsNullOrWhiteSpace(loaderVersion))
        {
            var available = await GetForgeLoaderVersionsAsync(minecraftVersion, neoForge, cancellationToken);
            loaderVersion = available.FirstOrDefault()
                ?? throw new InvalidOperationException(
                    $"No hay versiones de {(neoForge ? "NeoForge" : "Forge")} publicadas para {minecraftVersion}");
        }

        var (installerUrl, mavenVersion, installerName) =
            BuildForgeInstaller(minecraftVersion, loaderVersion.Trim(), neoForge);

        string installerDirectory = Path.Combine(_basePath, "installers");
        Directory.CreateDirectory(installerDirectory);
        string installerPath = Path.Combine(installerDirectory, installerName);

        StatusChanged?.Invoke(this,
            $"Descargando instalador de {(neoForge ? "NeoForge" : "Forge")} {mavenVersion}...");
        await EnsureFileAsync(installerUrl, installerPath, 0, null, cancellationToken, fatal: true);

        var (embeddedVersionJson, versionId) =
            ReadForgeInstallerProfile(installerPath, minecraftVersion, mavenVersion, neoForge);

        // El instalador oficial aborta si no existe un perfil del launcher.
        string profilesPath = Path.Combine(_basePath, "launcher_profiles.json");
        if (!File.Exists(profilesPath))
            await File.WriteAllTextAsync(profilesPath, "{\"profiles\":{},\"settings\":{},\"version\":3}", cancellationToken);

        var startedAt = DateTime.UtcNow;
        StatusChanged?.Invoke(this,
            $"Instalando {(neoForge ? "NeoForge" : "Forge")} {mavenVersion} (puede tardar varios minutos)...");

        string output;
        if (InstallerSupportsClientInstall(installerPath))
        {
            output = await RunForgeInstallerAsync(javaPath, installerPath, cancellationToken);
            Log.Info($"[GameFiles] Instalador {installerName} completado. Últimas líneas:{Environment.NewLine}{output}");
        }
        else
        {
            // Los instaladores antiguos (1.7.10 - 1.11.2) no admiten --installClient:
            // solo ofrecen GUI o instalación de servidor. Para ellos el perfil se
            // reconstruye desde install_profile.json (universal jar + versionInfo).
            Log.Info("[GameFiles] Instalador legacy sin --installClient; usando install_profile.json");
            string? legacyId = await TryLegacyInstallAsync(installerPath, cancellationToken);
            if (legacyId == null)
                throw new InvalidOperationException(
                    $"El instalador {installerName} no admite instalación por línea de comandos " +
                    "y no contiene un perfil legacy utilizable.");

            versionId = legacyId;
            embeddedVersionJson = null;
            output = "instalación legacy desde install_profile.json";
        }

        string versionJsonPath = Path.Combine(VersionsDirectory, versionId, $"{versionId}.json");

        // Los instaladores antiguos (p. ej. 1.8.9) no siempre escriben el perfil:
        // usar la copia embebida en el instalador como respaldo.
        if (!File.Exists(versionJsonPath) && !string.IsNullOrWhiteSpace(embeddedVersionJson))
        {
            Directory.CreateDirectory(Path.Combine(VersionsDirectory, versionId));
            await File.WriteAllTextAsync(versionJsonPath, embeddedVersionJson, cancellationToken);
            Log.Info($"[GameFiles] Perfil embebido escrito en {versionJsonPath}");
        }

        // Algunos instaladores usan un id distinto al declarado: localizar el perfil nuevo.
        if (!File.Exists(versionJsonPath))
        {
            var candidate = Directory.Exists(VersionsDirectory)
                ? Directory.EnumerateDirectories(VersionsDirectory)
                    .Select(d => new FileInfo(Path.Combine(d, Path.GetFileName(d) + ".json")))
                    .Where(f => f.Exists && f.LastWriteTimeUtc >= startedAt.AddMinutes(-2))
                    .Where(f => f.Name.Contains("forge", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault()
                : null;

            if (candidate != null)
            {
                versionId = Path.GetFileNameWithoutExtension(candidate.Name);
                versionJsonPath = candidate.FullName;
            }
        }

        if (!File.Exists(versionJsonPath))
            throw new InvalidOperationException(
                $"El instalador de {(neoForge ? "NeoForge" : "Forge")} terminó pero no se creó el perfil {versionId}.");

        Log.Info($"[GameFiles] {(neoForge ? "NeoForge" : "Forge")} instalado: {versionId}");
        StatusChanged?.Invoke(this, $"{(neoForge ? "NeoForge" : "Forge")} {mavenVersion} instalado");
        return versionId;
    }

    /// <summary>
    /// Convierte una versión de loader (p. ej. <c>1.20.1-47.4.5</c> o <c>21.1.265</c>) en una
    /// clave ordenable: se descarta el prefijo del juego y cada tramo numérico se rellena
    /// con ceros para que la comparación lexicográfica coincida con la numérica.
    /// </summary>
    private static string BuildVersionSortKey(string version, string prefix)
    {
        string value = !string.IsNullOrEmpty(prefix) &&
                       version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? version[prefix.Length..]
            : version;

        var numbers = NumericPartRegex.Matches(value)
            .Select(m => long.TryParse(m.Value, out long number) ? number : 0)
            .Select(number => number.ToString().PadLeft(10, '0'));

        return string.Join('.', numbers);
    }

    private static (string Url, string Prefix) GetForgeMetadataSource(string minecraftVersion, bool neoForge)
    {
        if (!neoForge)
            return ($"{ForgeMavenBase}net/minecraftforge/forge/maven-metadata.xml", minecraftVersion + "-");

        // NeoForge 1.20.1 se publica bajo el artefacto heredado net.neoforged:forge
        if (minecraftVersion == "1.20.1")
            return ($"{NeoForgeMavenBase}net/neoforged/forge/maven-metadata.xml", "1.20.1-");

        var parts = minecraftVersion.Split('.');
        string prefix = parts.Length >= 3
            ? $"{parts[1]}.{parts[2]}."
            : parts.Length == 2 ? $"{parts[1]}.0." : string.Empty;

        // La línea 20.4 cubre tanto 1.20.3 como 1.20.4.
        if (minecraftVersion == "1.20.3")
            prefix = "20.4.";

        return ($"{NeoForgeMavenBase}net/neoforged/neoforge/maven-metadata.xml", prefix);
    }

    private static (string Url, string MavenVersion, string FileName) BuildForgeInstaller(
        string minecraftVersion, string loaderVersion, bool neoForge)
    {
        if (!neoForge)
        {
            string version = loaderVersion.StartsWith(minecraftVersion + "-", StringComparison.OrdinalIgnoreCase)
                ? loaderVersion
                : $"{minecraftVersion}-{loaderVersion}";

            return ($"{ForgeMavenBase}net/minecraftforge/forge/{version}/forge-{version}-installer.jar",
                version, $"forge-{version}-installer.jar");
        }

        if (minecraftVersion == "1.20.1")
        {
            string version = loaderVersion.StartsWith("1.20.1-", StringComparison.OrdinalIgnoreCase)
                ? loaderVersion
                : $"1.20.1-{loaderVersion}";

            return ($"{NeoForgeMavenBase}net/neoforged/forge/{version}/forge-{version}-installer.jar",
                version, $"neoforge-forge-{version}-installer.jar");
        }

        return ($"{NeoForgeMavenBase}net/neoforged/neoforge/{loaderVersion}/neoforge-{loaderVersion}-installer.jar",
            loaderVersion, $"neoforge-{loaderVersion}-installer.jar");
    }

    /// <summary>
    /// Lee <c>version.json</c> e <c>install_profile.json</c> del instalador para conocer
    /// de antemano el id del perfil y disponer de un JSON de respaldo.
    /// </summary>
    private static (string? VersionJson, string VersionId) ReadForgeInstallerProfile(string installerPath,
        string minecraftVersion, string mavenVersion, bool neoForge)
    {
        string? versionJson = null;
        string? versionId = null;

        using var archive = ZipFile.OpenRead(installerPath);

        var versionEntry = archive.GetEntry("version.json");
        if (versionEntry != null)
        {
            using var reader = new StreamReader(versionEntry.Open());
            versionJson = reader.ReadToEnd();
            versionId = JObject.Parse(versionJson)["id"]?.Value<string>();
        }

        if (string.IsNullOrWhiteSpace(versionId))
        {
            var profileEntry = archive.GetEntry("install_profile.json");
            if (profileEntry != null)
            {
                using var reader = new StreamReader(profileEntry.Open());
                var profile = JObject.Parse(reader.ReadToEnd());

                versionId = profile["versionInfo"]?["id"]?.Value<string>()
                            ?? profile["install"]?["version"]?.Value<string>();

                if (versionJson == null && profile["versionInfo"] is JObject info)
                    versionJson = info.ToString(Formatting.Indented);
            }
        }

        versionId ??= $"{minecraftVersion}-{(neoForge ? "neoforge" : "forge")}-{mavenVersion}";
        return (versionJson, versionId);
    }

    private async Task<string> RunForgeInstallerAsync(string javaPath, string installerPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            WorkingDirectory = _basePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(installerPath);
        startInfo.ArgumentList.Add("--installClient");
        startInfo.ArgumentList.Add(_basePath);

        var tail = new Queue<string>();
        void Capture(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            lock (tail)
            {
                tail.Enqueue(line);
                while (tail.Count > 80)
                    tail.Dequeue();
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("El instalador de Forge superó el tiempo máximo de instalación (15 minutos).");
        }

        string output;
        lock (tail)
            output = string.Join(Environment.NewLine, tail);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"El instalador terminó con código {process.ExitCode}.{Environment.NewLine}{output}");

        return output;
    }

    /// <summary>
    /// Indica si el instalador admite <c>--installClient</c>. Se detecta buscando esa
    /// opción en sus clases: los instaladores de 1.7.10 a 1.11.2 solo ofrecen la GUI
    /// o <c>--installServer</c>, mientras que 1.12+ y NeoForge sí la aceptan.
    /// </summary>
    private static bool InstallerSupportsClientInstall(string installerPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(installerPath);
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase))
                    continue;

                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);

                if (Encoding.ASCII.GetString(buffer.ToArray()).Contains("installClient", StringComparison.Ordinal))
                    return true;
            }
        }
        catch (Exception ex)
        {
            // Ante la duda, intentar la vía oficial (--installClient).
            Log.Warn($"[GameFiles] No se pudo inspeccionar el instalador: {ex.Message}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Instala un Forge legacy cuyo instalador no admite <c>--installClient</c>
    /// (1.7.10 - 1.11.2). Extrae el universal jar incluido en el instalador, lo deja en
    /// la ruta Maven de <c>libraries</c> y escribe el perfil <c>versionInfo</c> como
    /// <c>versions/{id}/{id}.json</c>. Devuelve el id instalado o <c>null</c> si el
    /// instalador no contiene un perfil legacy utilizable.
    /// </summary>
    private async Task<string?> TryLegacyInstallAsync(string installerPath, CancellationToken cancellationToken)
    {
        try
        {
            using var archive = ZipFile.OpenRead(installerPath);

            var profileEntry = archive.GetEntry("install_profile.json");
            if (profileEntry == null)
                return null;

            string profileJson;
            using (var reader = new StreamReader(profileEntry.Open()))
                profileJson = await reader.ReadToEndAsync(cancellationToken);

            var profile = JObject.Parse(profileJson);
            if (profile["install"] is not JObject install ||
                profile["versionInfo"] is not JObject versionInfo)
                return null;

            string? versionId = versionInfo["id"]?.Value<string>() ?? install["target"]?.Value<string>();
            string? fileName = install["filePath"]?.Value<string>();
            string? mavenName = install["path"]?.Value<string>();

            if (string.IsNullOrWhiteSpace(versionId) ||
                string.IsNullOrWhiteSpace(fileName) ||
                string.IsNullOrWhiteSpace(mavenName))
                return null;

            var jarEntry = archive.GetEntry(fileName) ??
                           archive.Entries.FirstOrDefault(e =>
                               e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            if (jarEntry == null)
            {
                Log.Warn($"[GameFiles] El instalador no contiene {fileName}");
                return null;
            }

            string relativePath = GetMavenPath(mavenName);
            string targetJar = Path.Combine(LibrariesDirectory,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(targetJar)!);
            jarEntry.ExtractToFile(targetJar, overwrite: true);

            string versionDirectory = Path.Combine(VersionsDirectory, versionId);
            Directory.CreateDirectory(versionDirectory);
            string versionJsonPath = Path.Combine(versionDirectory, $"{versionId}.json");
            await File.WriteAllTextAsync(versionJsonPath, versionInfo.ToString(Formatting.Indented), cancellationToken);

            StatusChanged?.Invoke(this, $"Forge legacy instalado ({versionId})");
            Log.Info($"[GameFiles] Universal jar legacy en {targetJar}");
            return versionId;
        }
        catch (Exception ex)
        {
            Log.Warn($"[GameFiles] No se pudo aplicar la instalación legacy: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Prepara todos los archivos de la versión y devuelve la información necesaria para el lanzamiento.
    /// </summary>
    public async Task<GameInstallation> EnsureVersionAsync(string versionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            throw new ArgumentException("La versión no puede estar vacía", nameof(versionId));

        StatusChanged?.Invoke(this, $"Preparando Minecraft {versionId}...");

        var manifest = await GetManifestAsync(cancellationToken);
        var version = await LoadVersionTreeAsync(versionId, manifest, cancellationToken);

        if (string.IsNullOrWhiteSpace(version.MainClass))
            throw new InvalidOperationException($"La versión '{versionId}' no define mainClass.");

        // El client.jar vive en el directorio de la versión que lo aporta (vanilla
        // cuando instalamos un modloader con inheritsFrom), no en el del modloader.
        string jarSourceId = string.IsNullOrWhiteSpace(version.ClientSourceId) ? version.Id : version.ClientSourceId;
        string versionDirectory = Path.Combine(VersionsDirectory, jarSourceId);
        Directory.CreateDirectory(versionDirectory);

        // ---------- client.jar ----------
        var client = version.Downloads.Client
            ?? throw new InvalidOperationException($"La versión '{versionId}' no tiene client.jar en su metadata.");

        string clientJarPath = Path.Combine(versionDirectory, $"{jarSourceId}.jar");
        StatusChanged?.Invoke(this, "Comprobando client.jar...");
        await EnsureFileAsync(client.Url, clientJarPath, client.Size, client.Sha1, cancellationToken, fatal: true);

        // Forge/NeoForge (1.17+) referencian ${version_name}.jar dentro de -DignoreList:
        // ModLauncher excluye del module layer el jar del juego solo si su nombre de
        // archivo coincide. Si se lanza con el jar vanilla (1.20.1.jar), ModLauncher lo
        // convierte en el módulo automático _1._20._1, que choca con el módulo minecraft
        // creado por FML y aborta con ResolutionException. Se replica el layout oficial:
        // una copia del client.jar en versions/{perfil}/{perfil}.jar y se lanza con ella.
        clientJarPath = EnsureProfileJar(version, clientJarPath, jarSourceId, VersionsDirectory);

        // ---------- librerías ----------
        var features = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var libraries = version.Libraries
            .Where(l => RuleEvaluator.IsAllowed(l.Rules, features))
            .ToList();

        StatusChanged?.Invoke(this, $"Descargando librerías (0/{libraries.Count})...");

        int downloaded = 0;
        foreach (var library in libraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureLibraryAsync(library, cancellationToken);

            downloaded++;
            if (downloaded % 5 == 0 || downloaded == libraries.Count)
            {
                StatusChanged?.Invoke(this, $"Descargando librerías ({downloaded}/{libraries.Count})...");
                DownloadProgressChanged?.Invoke(this, $"Librerías {downloaded}/{libraries.Count}");
                ReportProgress(5 + (int)(25.0 * downloaded / Math.Max(1, libraries.Count)));
            }
        }

        // ---------- assets ----------
        string assetIndexName = version.AssetIndex?.Id ?? version.Assets ?? "legacy";
        string assetIndexPath = Path.Combine(AssetsDirectory, "indexes", $"{assetIndexName}.json");
        string? loggingConfigPath = null;

        if (version.AssetIndex != null && !string.IsNullOrWhiteSpace(version.AssetIndex.Url))
        {
            StatusChanged?.Invoke(this, $"Descargando índice de assets ({assetIndexName})...");
            await EnsureFileAsync(version.AssetIndex.Url, assetIndexPath, version.AssetIndex.Size,
                version.AssetIndex.Sha1, cancellationToken, fatal: true);

            if (File.Exists(assetIndexPath))
                await DownloadAssetsAsync(assetIndexPath, cancellationToken);
        }
        else
        {
            Log.Warn($"[GameFiles] La versión {versionId} no incluye assetIndex; se omite la descarga de assets");
        }

        // ---------- configuración de logging (log4j) ----------
        var loggingFile = version.Logging?.Client?.File;
        if (loggingFile != null && !string.IsNullOrWhiteSpace(loggingFile.Url))
        {
            string id = string.IsNullOrWhiteSpace(loggingFile.Id) ? "client" : loggingFile.Id!;
            loggingConfigPath = Path.Combine(AssetsDirectory, "log_configs", id);
            await EnsureFileAsync(loggingFile.Url, loggingConfigPath, loggingFile.Size,
                loggingFile.Sha1, cancellationToken, fatal: false);
        }

        // ---------- directorio de nativas ----------
        string nativesDirectory = NativesDirectory(version.Id);
        Directory.CreateDirectory(nativesDirectory);

        int requiredJava = version.JavaVersion?.MajorVersion ?? 0;

        ReportProgress(100);
        StatusChanged?.Invoke(this, "Archivos del juego listos");

        return new GameInstallation
        {
            VersionId = version.Id,
            Version = version,
            VersionJarPath = clientJarPath,
            NativesDirectory = nativesDirectory,
            AssetsDirectory = AssetsDirectory,
            LibrariesDirectory = LibrariesDirectory,
            AssetIndexName = assetIndexName,
            RequiredJavaMajor = requiredJava,
            LoggingConfigPath = loggingConfigPath
        };
    }

    /// <summary>
    /// Devuelve la ruta del jar con el que debe lanzarse el perfil. Los perfiles de
    /// Forge/NeoForge con ModLauncher declaran <c>${version_name}</c> en sus argumentos
    /// JVM (dentro de <c>-DignoreList</c>): necesitan un jar cuyo nombre coincida con el
    /// id del perfil para que ModLauncher lo excluya del module layer. En ese caso se
    /// copia el client.jar heredado a <c>versions/{id}/{id}.jar</c>. Si el perfil ya
    /// trae su propio jar (OptiFine u otros parches) se respeta tal cual.
    /// </summary>
    internal static string EnsureProfileJar(MinecraftVersionJson version, string sourceJarPath,
        string jarSourceId, string versionsDirectory)
    {
        if (version.Id.Equals(jarSourceId, StringComparison.OrdinalIgnoreCase) || !RequiresProfileJar(version))
            return sourceJarPath;

        string profileDirectory = Path.Combine(versionsDirectory, version.Id);
        Directory.CreateDirectory(profileDirectory);
        string profileJarPath = Path.Combine(profileDirectory, $"{version.Id}.jar");

        // Nunca sobrescribir un jar propio ya instalado (p. ej. OptiFine).
        if (File.Exists(profileJarPath) && new FileInfo(profileJarPath).Length > 0)
            return profileJarPath;

        try
        {
            // Copia a .part y renombrado final para no dejar un jar a medias si falla.
            string tempPath = profileJarPath + ".part";
            File.Copy(sourceJarPath, tempPath, overwrite: true);
            File.Move(tempPath, profileJarPath, overwrite: true);

            Log.Info($"[GameFiles] Jar del perfil {version.Id} preparado desde {jarSourceId}.jar");
            return profileJarPath;
        }
        catch (Exception ex)
        {
            Log.Warn($"[GameFiles] No se pudo preparar el jar del perfil {version.Id}: {ex.Message}");
            return sourceJarPath;
        }
    }

    private static bool RequiresProfileJar(MinecraftVersionJson version)
    {
        var jvm = version.Arguments?.Jvm;
        if (jvm == null)
            return false;

        return jvm.Any(token =>
            token.ToString().Contains("${version_name}", StringComparison.Ordinal));
    }

    // ---------- Manifest y version.json ----------

    public async Task<VersionManifest> GetManifestAsync(CancellationToken cancellationToken = default)
    {
        string cacheFile = Path.Combine(_basePath, "cache", "version_manifest_v2.json");

        if (File.Exists(cacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile) < ManifestMaxAge)
        {
            try
            {
                var cached = JsonConvert.DeserializeObject<VersionManifest>(await File.ReadAllTextAsync(cacheFile, cancellationToken));

                // El manifiesto v2 de Mojang contiene más de mil entradas (release, snapshot,
                // old_beta y old_alpha). Una caché recortada o antigua (p.ej. de un manifiesto v1)
                // ocultaría versiones históricas como 1.12.2 u 1.8.9, así que forzamos su descarga.
                if (cached is { Versions.Count: >= MinimumManifestVersions })
                    return cached;

                if (cached is { Versions.Count: > 0 })
                    Log.Warn($"[GameFiles] Caché de manifiesto incompleta ({cached.Versions.Count} versiones); se descargará de nuevo");
            }
            catch (Exception ex)
            {
                Log.Warn($"[GameFiles] Manifiesto en caché inválido: {ex.Message}");
            }
        }

        try
        {
            Log.Info("[GameFiles] Descargando manifiesto de versiones...");
            string json = await _http.GetStringAsync(ManifestUrl, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            await File.WriteAllTextAsync(cacheFile, json, cancellationToken);

            return JsonConvert.DeserializeObject<VersionManifest>(json)
                   ?? throw new InvalidOperationException("Manifiesto de versiones vacío");
        }
        catch (Exception ex) when (File.Exists(cacheFile))
        {
            Log.Warn($"[GameFiles] No se pudo actualizar el manifiesto ({ex.Message}); se usa la caché");
            return JsonConvert.DeserializeObject<VersionManifest>(await File.ReadAllTextAsync(cacheFile, cancellationToken))
                   ?? throw new InvalidOperationException("No hay manifiesto de versiones disponible");
        }
    }

    /// <summary>Carga version.json resolviendo <c>inheritsFrom</c> (Fabric/Forge/NeoForge).</summary>
    private async Task<MinecraftVersionJson> LoadVersionTreeAsync(string versionId, VersionManifest manifest,
        CancellationToken cancellationToken)
    {
        var entry = manifest.Versions.FirstOrDefault(v =>
            v.Id.Equals(versionId, StringComparison.OrdinalIgnoreCase));

        if (entry == null)
        {
            // Versiones personalizadas (Fabric/Forge/NeoForge) no figuran en el manifiesto
            // de Mojang, pero sí existen como {id}.json en la carpeta versions/.
            string localPath = Path.Combine(VersionsDirectory, versionId, $"{versionId}.json");
            if (!File.Exists(localPath))
                throw new InvalidOperationException($"La versión '{versionId}' no existe en el manifiesto de Mojang ni está instalada localmente");

            Log.Info($"[GameFiles] {versionId} es una versión local (modloader); usando metadata instalada");
            var localVersion = JsonConvert.DeserializeObject<MinecraftVersionJson>(await File.ReadAllTextAsync(localPath, cancellationToken))
                               ?? throw new InvalidOperationException($"No se pudo leer {localPath}");
            localVersion.JsonPath = localPath;
            if (string.IsNullOrWhiteSpace(localVersion.Id))
                localVersion.Id = versionId;

            if (!string.IsNullOrWhiteSpace(localVersion.InheritsFrom))
            {
                var parentEntry = manifest.Versions.FirstOrDefault(v =>
                    v.Id.Equals(localVersion.InheritsFrom, StringComparison.OrdinalIgnoreCase));
                var parent = parentEntry != null
                    ? await LoadVersionJsonAsync(parentEntry, cancellationToken)
                    : await LoadVersionTreeAsync(localVersion.InheritsFrom, manifest, cancellationToken);
                return MergeVersions(parent, localVersion);
            }

            return localVersion;
        }

        var version = await LoadVersionJsonAsync(entry, cancellationToken);

        if (!string.IsNullOrWhiteSpace(version.InheritsFrom))
        {
            Log.Info($"[GameFiles] {versionId} hereda de {version.InheritsFrom}");
            var parent = await LoadVersionTreeAsync(version.InheritsFrom, manifest, cancellationToken);
            version = MergeVersions(parent, version);
        }

        return version;
    }

    private async Task<MinecraftVersionJson> LoadVersionJsonAsync(VersionManifestEntry entry,
        CancellationToken cancellationToken)
    {
        string directory = Path.Combine(VersionsDirectory, entry.Id);
        string path = Path.Combine(directory, $"{entry.Id}.json");

        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            Log.Info($"[GameFiles] Descargando metadata de {entry.Id}");
            Directory.CreateDirectory(directory);
            await EnsureFileAsync(entry.Url, path, 0, entry.Sha1, cancellationToken, fatal: true);
        }

        var version = JsonConvert.DeserializeObject<MinecraftVersionJson>(await File.ReadAllTextAsync(path, cancellationToken))
                      ?? throw new InvalidOperationException($"No se pudo leer {path}");

        version.JsonPath = path;
        if (string.IsNullOrWhiteSpace(version.Id))
            version.Id = entry.Id;

        return version;
    }

    internal static MinecraftVersionJson MergeVersions(MinecraftVersionJson parent, MinecraftVersionJson child)
    {
        // Los perfiles de modloader pueden reemplazar una librería del padre por otra
        // versión (p. ej. Forge 1.16.5 sube log4j-core 2.8.1 -> 2.15.0). Si ambas versiones
        // acaban en el classpath, se cargan clases de la primera y los llamadores compilados
        // contra la otra fallan con NoSuchMethodError (caso real: ThrowableProxy de log4j y
        // las crash reports de Forge). Se unifica por group:artifact[:classifier] dando
        // prioridad al perfil hijo y colocando sus librerías primero, igual que el launcher
        // oficial: así el perfil siempre gana al metadata heredado.
        var libraries = new List<LibraryInfo>(child.Libraries);
        var childKeys = new HashSet<string>(child.Libraries.Select(LibraryKey), StringComparer.OrdinalIgnoreCase);

        foreach (var library in parent.Libraries)
        {
            // Solo se descarta el duplicado del padre si el perfil hijo aporta su propia
            // versión de esa librería. Las repeticiones dentro de un mismo metadata son
            // legítimas (p. ej. 1.16.5 declara lwjgl 3.2.1 y 3.2.2 con reglas de
            // arquitectura distintas) y deben conservarse.
            if (!childKeys.Contains(LibraryKey(library)))
                libraries.Add(library);
        }

        return new MinecraftVersionJson
        {
            Id = string.IsNullOrWhiteSpace(child.Id) ? parent.Id : child.Id,
            InheritsFrom = null,
            MainClass = string.IsNullOrWhiteSpace(child.MainClass) ? parent.MainClass : child.MainClass,
            MinecraftArguments = string.IsNullOrWhiteSpace(child.MinecraftArguments)
                ? parent.MinecraftArguments
                : child.MinecraftArguments,
            Assets = child.Assets ?? parent.Assets,
            AssetIndex = child.AssetIndex ?? parent.AssetIndex,
            Downloads = new VersionDownloads
            {
                Client = child.Downloads?.Client ?? parent.Downloads?.Client,
                Server = child.Downloads?.Server ?? parent.Downloads?.Server
            },
            JavaVersion = child.JavaVersion ?? parent.JavaVersion,
            Logging = child.Logging ?? parent.Logging,
            Type = child.Type ?? parent.Type,
            Libraries = libraries,
            Arguments = new VersionArguments
            {
                Game = parent.Arguments?.Game.Concat(child.Arguments?.Game ?? Enumerable.Empty<Newtonsoft.Json.Linq.JToken>()).ToList() ?? new(),
                Jvm = parent.Arguments?.Jvm.Concat(child.Arguments?.Jvm ?? Enumerable.Empty<Newtonsoft.Json.Linq.JToken>()).ToList() ?? new()
            },
            JsonPath = child.JsonPath,
            ClientSourceId = child.Downloads?.Client != null
                ? (string.IsNullOrWhiteSpace(child.Id) ? parent.Id : child.Id)
                : (string.IsNullOrWhiteSpace(parent.ClientSourceId) ? parent.Id : parent.ClientSourceId)
        };
    }

    /// <summary>
    /// Clave de unicidad de una librería ignorando la versión: <c>group:artifact[:classifier]</c>.
    /// Permite que el perfil hijo sustituya la versión que trae el metadata heredado sin
    /// dejar dos copias de la misma librería en el classpath.
    /// </summary>
    internal static string LibraryKey(LibraryInfo library)
    {
        string[] parts = (library.Name ?? string.Empty).Split(':');
        if (parts.Length < 3)
            return library.Name ?? string.Empty;

        string classifier = parts.Length >= 4 ? parts[3] : string.Empty;
        return string.IsNullOrEmpty(classifier)
            ? $"{parts[0]}:{parts[1]}"
            : $"{parts[0]}:{parts[1]}:{classifier}";
    }

    // ---------- Descargas ----------

    private async Task EnsureLibraryAsync(LibraryInfo library, CancellationToken cancellationToken)
    {
        // Artefacto principal
        var artifact = library.Downloads?.Artifact;
        string? relativePath = artifact?.Path;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            // Varias librerías legacy (jinput-platform, lwjgl-platform, twitch-platform)
            // solo publican clasificadores nativos: no existe artefacto principal y
            // pedirlo devuelve 404, así que se descargan únicamente sus nativas.
            if (library.Downloads != null)
            {
                if (library.Downloads.Classifiers is not { Count: > 0 })
                {
                    Log.Warn($"[GameFiles] Librería sin artefacto ni clasificadores: {library.Name}");
                    return;
                }
            }
            else
            {
                relativePath = GetMavenPath(library.Name);
            }
        }

        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            string targetPath = Path.Combine(LibrariesDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string? url = artifact?.Url;

            if (string.IsNullOrWhiteSpace(url))
            {
                url = ResolveLibraryBaseUrl(library, relativePath).TrimEnd('/') + "/" + relativePath;
            }

            await EnsureFileAsync(url, targetPath, artifact?.Size ?? 0, artifact?.Sha1, cancellationToken, fatal: true);
        }

        // Nativas con clasificador (formato antiguo, hasta 1.18)
        if (library.Natives == null ||
            !library.Natives.TryGetValue(RuleEvaluator.GetOsName(), out string? classifier))
            return;

        classifier = classifier.Replace("${arch}", Environment.Is64BitProcess ? "64" : "32");

        if (library.Downloads?.Classifiers != null &&
            library.Downloads.Classifiers.TryGetValue(classifier, out var native))
        {
            string nativePath = Path.Combine(LibrariesDirectory, native.Path!.Replace('/', Path.DirectorySeparatorChar));
            await EnsureFileAsync(native.Url, nativePath, native.Size, native.Sha1, cancellationToken, fatal: true);
        }
    }

    /// <summary>Convierte <c>group:artifact:version[:classifier][@ext]</c> en una ruta de Maven.</summary>
    public static string GetMavenPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        string extension = "jar";
        int atIndex = name.IndexOf('@');
        if (atIndex >= 0)
        {
            extension = name[(atIndex + 1)..];
            name = name[..atIndex];
        }

        var parts = name.Split(':');
        if (parts.Length < 3)
            return string.Empty;

        string group = parts[0].Replace('.', '/');
        string artifact = parts[1];
        string version = parts[2];
        string? classifier = parts.Length > 3 ? parts[3] : null;

        string file = $"{artifact}-{version}";
        if (!string.IsNullOrWhiteSpace(classifier))
            file += $"-{classifier}";
        file += $".{extension}";

        return $"{group}/{artifact}/{version}/{file}";
    }

    /// <summary>
    /// Resuelve el repositorio Maven de una librería. Algunos artefactos de Forge
    /// (p. ej. el jar universal de 1.12.2) se publican sin URL y no viven en el
    /// repositorio de Mojang, así que se enrutan a su Maven correspondiente.
    /// </summary>
    private static string ResolveLibraryBaseUrl(LibraryInfo library, string relativePath)
    {
        if (!string.IsNullOrWhiteSpace(library.Url))
            return library.Url!;

        if (relativePath.StartsWith("net/minecraftforge/", StringComparison.OrdinalIgnoreCase) ||
            library.Name.StartsWith("net.minecraftforge:", StringComparison.OrdinalIgnoreCase))
            return ForgeMavenBase;

        if (relativePath.StartsWith("net/neoforged/", StringComparison.OrdinalIgnoreCase) ||
            library.Name.StartsWith("net.neoforged:", StringComparison.OrdinalIgnoreCase))
            return NeoForgeMavenBase;

        return "https://libraries.minecraft.net/";
    }

    private async Task DownloadAssetsAsync(string assetIndexPath, CancellationToken cancellationToken)
    {
        AssetIndex? index;
        try
        {
            index = JsonConvert.DeserializeObject<AssetIndex>(await File.ReadAllTextAsync(assetIndexPath, cancellationToken));
        }
        catch (Exception ex)
        {
            Log.Warn($"[GameFiles] Índice de assets inválido: {ex.Message}");
            return;
        }

        if (index?.Objects == null || index.Objects.Count == 0)
            return;

        // Varios nombres pueden apuntar al mismo hash: descargar cada objeto una sola vez
        // evita que dos tareas escriban el mismo archivo .part a la vez.
        var objects = index.Objects.Values
            .Where(asset => !string.IsNullOrEmpty(asset.Hash) && asset.Hash.Length >= 3)
            .GroupBy(asset => asset.Hash, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        int total = objects.Count;
        int done = 0;

        StatusChanged?.Invoke(this, $"Descargando assets (0/{total})...");

        using var throttler = new SemaphoreSlim(24);
        var tasks = objects.Select(async asset =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                if (string.IsNullOrEmpty(asset.Hash) || asset.Hash.Length < 3)
                    return;

                string url = $"{ResourcesUrl}/{asset.Hash[..2]}/{asset.Hash}";
                string path = Path.Combine(AssetsDirectory, "objects", asset.Hash[..2], asset.Hash);

                await EnsureFileAsync(url, path, asset.Size, asset.Hash, cancellationToken, fatal: false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn($"[GameFiles] Asset omitido: {ex.Message}");
            }
            finally
            {
                int current = Interlocked.Increment(ref done);
                if (current % 100 == 0 || current == total)
                {
                    StatusChanged?.Invoke(this, $"Descargando assets ({current}/{total})...");
                    DownloadProgressChanged?.Invoke(this, $"Assets {current}/{total}");
                    ReportProgress(30 + (int)(68.0 * current / Math.Max(1, total)));
                }
                throttler.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    internal async Task EnsureFileAsync(string url, string path, long expectedSize, string? expectedSha1,
        CancellationToken cancellationToken, bool fatal)
    {
        try
        {
            // Reutilizar un archivo ya presente se decide por tamaño: revalidar el
            // SHA1 de todas las librerías y assets en cada arranque costaba ~12 s
            // (1.2 GB). La integridad se garantiza sobre lo que se descarga: el
            // .part se valida contra el hash antes de pasar a ser definitivo.
            if (FileMatches(path, expectedSize, expectedSha1: null))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tempPath = path + ".part";

            using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();

                await using var fileStream = File.Create(tempPath);
                await response.Content.CopyToAsync(fileStream, cancellationToken);
            }

            // El archivo descargado no se publica como definitivo hasta validar el hash.
            if (!FileMatches(tempPath, expectedSize: 0, expectedSha1))
            {
                File.Delete(tempPath);
                throw new InvalidDataException("El archivo descargado no coincide con el SHA1 esperado");
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn($"[GameFiles] Error descargando '{url}': {ex.Message}");
            if (fatal)
                throw new IOException($"No se pudo descargar un archivo necesario ({Path.GetFileName(path)}): {ex.Message}", ex);
        }
    }

    /// <summary>SHA1 en hexadecimal minúscula del contenido de un archivo.</summary>
    internal static string ComputeSha1(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// Indica si un archivo local ya sirve: existe, coincide el tamaño esperado
    /// (cuando se conoce) y, si hay SHA1, coincide el contenido.
    /// </summary>
    internal static bool FileMatches(string path, long expectedSize, string? expectedSha1)
    {
        if (!File.Exists(path))
            return false;

        long length = new FileInfo(path).Length;
        if (expectedSize > 0 && length != expectedSize)
            return false;

        if (!string.IsNullOrWhiteSpace(expectedSha1))
        {
            if (length == 0)
                return false;

            return string.Equals(ComputeSha1(path), expectedSha1, StringComparison.OrdinalIgnoreCase);
        }

        return expectedSize > 0 || length > 0;
    }

    private void ReportProgress(int percent)
        => ProgressChanged?.Invoke(this, Math.Clamp(percent, 0, 100));

    private class AssetIndex
    {
        [JsonProperty("objects")]
        public Dictionary<string, AssetObject> Objects { get; set; } = new();
    }

    private class AssetObject
    {
        [JsonProperty("hash")]
        public string Hash { get; set; } = string.Empty;

        [JsonProperty("size")]
        public long Size { get; set; }
    }
}
