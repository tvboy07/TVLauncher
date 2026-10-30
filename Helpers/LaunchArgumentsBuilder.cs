using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using TVLauncher.Helpers;
using TVLauncher.Models;
using TVLauncher.Services;

namespace TVLauncher.Helpers;

public sealed class LaunchContext
{
    public MinecraftInstance Instance { get; init; } = new();
    public string Username { get; init; } = string.Empty;
    public string Uuid { get; init; } = string.Empty;
    public string AccessToken { get; init; } = "0";
    public bool IsOnlineAccount { get; init; }
    public GameInstallation Installation { get; init; } = new();
    public string JavaPath { get; init; } = string.Empty;
}

public sealed class LaunchPlan
{
    public string JavaPath { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public string Classpath { get; init; } = string.Empty;
    public string NativesDirectory { get; init; } = string.Empty;
    public List<string> Arguments { get; init; } = new();

    /// <summary>Línea de comandos para logs, con el token de acceso oculto.</summary>
    public string ToLogString()
    {
        var masked = new List<string>(Arguments.Count);
        for (int i = 0; i < Arguments.Count; i++)
        {
            string arg = Arguments[i];
            if (arg == "--accessToken" && i + 1 < Arguments.Count)
            {
                masked.Add(arg);
                masked.Add("***");
                i++;
                continue;
            }

            if (arg.StartsWith("--accessToken=", StringComparison.OrdinalIgnoreCase))
            {
                masked.Add("--accessToken=***");
                continue;
            }

            masked.Add(arg.Contains(' ') ? $"\"{arg}\"" : arg);
        }

        return $"\"{JavaPath}\" {string.Join(' ', masked)}";
    }
}

/// <summary>
/// Construye los argumentos de la JVM y del juego a partir del version.json
/// (con soporte para herencia de versiones, reglas y placeholders oficiales).
/// </summary>
public class LaunchArgumentsBuilder
{
    public const string LauncherName = "TVLauncher";
    public const string LauncherVersion = "1.0.0";

    public LaunchPlan Build(LaunchContext context)
    {
        var instance = context.Instance;
        var installation = context.Installation;
        var version = installation.Version;

        Directory.CreateDirectory(instance.GameDirectory);
        Directory.CreateDirectory(installation.NativesDirectory);

        var features = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["is_demo_user"] = false,
            ["has_custom_resolution"] = instance.ResolutionEnabled,
            ["has_quick_plays_support"] = false,
            ["is_quick_play_singleplayer"] = false,
            ["is_quick_play_multiplayer"] = false,
            ["is_quick_play_realms"] = false
        };

        var libraries = version.Libraries
            .Where(l => RuleEvaluator.IsAllowed(l.Rules, features))
            .ToList();

        ExtractNatives(libraries, installation.NativesDirectory, installation.LibrariesDirectory);

        string classpath = BuildClasspath(installation, libraries);
        var placeholders = BuildPlaceholders(context, classpath);

        var jvmTokens = version.Arguments?.Jvm.Count > 0
            ? ExpandArguments(version.Arguments.Jvm, features).ToList()
            : new List<string>();

        bool hasLibraryPath = jvmTokens.Any(t =>
            t.Contains("java.library.path", StringComparison.OrdinalIgnoreCase));

        var args = new List<string>
        {
            $"-Xms{Math.Max(1, instance.MinRamGB)}G",
            $"-Xmx{Math.Max(1, instance.MaxRamGB)}G",
            "-Dminecraft.launcher.brand=" + LauncherName,
            "-Dminecraft.launcher.version=" + LauncherVersion,
            "-Dlog4j2.formatMsgNoLookups=true"
        };

        if (!hasLibraryPath)
            args.Add($"-Djava.library.path={installation.NativesDirectory}");

        if (!string.IsNullOrWhiteSpace(installation.LoggingConfigPath) && File.Exists(installation.LoggingConfigPath))
            args.Add($"-Dlog4j.configurationFile={installation.LoggingConfigPath}");

        // Argumentos JVM personalizados de la instancia
        foreach (var custom in instance.JVMArgs)
        {
            if (string.IsNullOrWhiteSpace(custom))
                continue;

            args.Add(ReplacePlaceholders(custom, placeholders, logUnresolved: false));
        }

        if (jvmTokens.Count > 0)
        {
            foreach (var token in jvmTokens)
                args.Add(ReplacePlaceholders(token, placeholders));
        }
        else
        {
            // Versiones antiguas (<= 1.12): la clase principal y el classpath se añaden a mano
            args.Add("-cp");
            args.Add(classpath);
        }

        args.Add(version.MainClass);

        // Argumentos del juego
        if (version.Arguments?.Game.Count > 0)
        {
            foreach (var token in ExpandArguments(version.Arguments.Game, features))
                args.Add(ReplacePlaceholders(token, placeholders));
        }
        else if (!string.IsNullOrWhiteSpace(version.MinecraftArguments))
        {
            foreach (var token in version.MinecraftArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                args.Add(ReplacePlaceholders(token, placeholders));
        }
        else
        {
            Log.Warn($"[Args] La versión {version.Id} no define argumentos de juego; se usan los mínimos");
            args.AddRange(new[]
            {
                "--username", context.Username,
                "--version", installation.Version.Id,
                "--gameDir", instance.GameDirectory,
                "--assetsDir", installation.AssetsDirectory,
                "--assetIndex", installation.AssetIndexName,
                "--uuid", context.Uuid,
                "--accessToken", context.AccessToken
            });
        }

        return new LaunchPlan
        {
            JavaPath = context.JavaPath,
            WorkingDirectory = instance.GameDirectory,
            Classpath = classpath,
            NativesDirectory = installation.NativesDirectory,
            Arguments = args
        };
    }

    // ---------- Classpath ----------

    private static string BuildClasspath(GameInstallation installation, IEnumerable<LibraryInfo> libraries)
    {
        var entries = new List<string> { installation.VersionJarPath };

        foreach (var library in libraries)
        {
            var artifact = library.Downloads?.Artifact;

            // Librerías que solo publican clasificadores nativos (jinput-platform,
            // lwjgl-platform legacy, twitch-platform) no aportan nada al classpath.
            if (library.Downloads != null && artifact == null)
                continue;

            string relative = artifact?.Path ?? GameFilesService.GetMavenPath(library.Name);

            if (string.IsNullOrWhiteSpace(relative))
            {
                Log.Warn($"[Args] Librería sin ruta de artefacto: {library.Name}");
                continue;
            }

            string fullPath = Path.Combine(installation.LibrariesDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
                entries.Add(fullPath);
            else
                Log.Warn($"[Args] Librería no encontrada en disco: {fullPath}");
        }

        return string.Join(Path.PathSeparator, entries.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    // ---------- Nativas ----------

    private static void ExtractNatives(IEnumerable<LibraryInfo> libraries, string nativesDirectory, string librariesDirectory)
    {
        int extracted = 0;

        foreach (var library in libraries)
        {
            string? jarPath = null;
            var exclusions = library.Extract?.Exclude ?? new List<string>();

            if (library.Natives != null &&
                library.Natives.TryGetValue(RuleEvaluator.GetOsName(), out string? classifier))
            {
                classifier = classifier.Replace("${arch}", Environment.Is64BitProcess ? "64" : "32");

                if (library.Downloads?.Classifiers != null &&
                    library.Downloads.Classifiers.TryGetValue(classifier, out var native))
                {
                    jarPath = native.Path;
                }
            }

            // Formato moderno (1.19+): la librería nativa es un artefacto con clasificador
            if (jarPath == null &&
                library.Name.Contains(":natives-", StringComparison.OrdinalIgnoreCase) &&
                library.Name.Contains("windows", StringComparison.OrdinalIgnoreCase))
            {
                jarPath = library.Downloads?.Artifact?.Path ?? GameFilesService.GetMavenPath(library.Name);
            }

            if (string.IsNullOrWhiteSpace(jarPath))
                continue;

            string fullJar = Path.Combine(librariesDirectory, jarPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullJar))
            {
                Log.Warn($"[Args] Nativa no encontrada: {fullJar}");
                continue;
            }

            try
            {
                using var archive = ZipFile.OpenRead(fullJar);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    if (entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (exclusions.Any(ex => entry.FullName.StartsWith(ex, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    string destination = Path.Combine(nativesDirectory, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                    var info = new FileInfo(destination);
                    if (info.Exists && info.Length == entry.Length)
                        continue;

                    entry.ExtractToFile(destination, overwrite: true);
                    extracted++;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[Args] No se pudieron extraer nativas de '{fullJar}': {ex.Message}");
            }
        }

        if (extracted > 0)
            Log.Info($"[Args] {extracted} archivos nativos extraídos en {nativesDirectory}");
    }

    // ---------- Placeholders ----------

    private static Dictionary<string, string> BuildPlaceholders(LaunchContext context, string classpath)
    {
        var instance = context.Instance;
        var installation = context.Installation;

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["${auth_player_name}"] = context.Username,
            // El nombre de versión debe ser el del perfil realmente resuelto (Forge/NeoForge),
            // no el de la instancia: -DignoreList lo usa para excluir el jar del module layer.
            ["${version_name}"] = string.IsNullOrWhiteSpace(installation.Version.Id)
                ? instance.Version
                : installation.Version.Id,
            ["${game_directory}"] = instance.GameDirectory,
            ["${assets_root}"] = installation.AssetsDirectory,
            ["${assets_index_name}"] = installation.AssetIndexName,
            ["${auth_uuid}"] = context.Uuid.Replace("-", string.Empty),
            ["${auth_access_token}"] = context.AccessToken,
            ["${auth_session}"] = context.AccessToken,
            ["${user_type}"] = context.IsOnlineAccount ? "msa" : "legacy",
            ["${version_type}"] = instance.VersionType,
            ["${natives_directory}"] = installation.NativesDirectory,
            ["${library_directory}"] = installation.LibrariesDirectory,
            ["${classpath_separator}"] = Path.PathSeparator.ToString(),
            ["${launcher_name}"] = LauncherName,
            ["${launcher_version}"] = LauncherVersion,
            ["${classpath}"] = classpath,
            ["${game_assets}"] = Path.Combine(installation.AssetsDirectory, "virtual", "legacy"),
            ["${user_properties}"] = "{}",
            ["${resolution_width}"] = Math.Max(1, instance.ResolutionWidth).ToString(),
            ["${resolution_height}"] = Math.Max(1, instance.ResolutionHeight).ToString(),
            ["${clientid}"] = string.Empty,
            ["${auth_xuid}"] = string.Empty,
            ["${xuid}"] = string.Empty,
            ["${quickPlayPath}"] = string.Empty,
            ["${quickPlaySingleplayer}"] = string.Empty,
            ["${quickPlayMultiplayer}"] = string.Empty,
            ["${quickPlayRealms}"] = string.Empty
        };
    }

    private static string ReplacePlaceholders(string value, IReadOnlyDictionary<string, string> placeholders,
        bool logUnresolved = true)
    {
        if (!value.Contains('$'))
            return value;

        foreach (var pair in placeholders)
            value = value.Replace(pair.Key, pair.Value, StringComparison.Ordinal);

        if (logUnresolved && value.Contains("${", StringComparison.Ordinal))
            Log.Warn($"[Args] Placeholder sin resolver: {value}");

        return value;
    }

    private static IEnumerable<string> ExpandArguments(IEnumerable<JToken> tokens,
        IReadOnlyDictionary<string, bool> features)
    {
        foreach (var token in tokens)
        {
            if (token.Type == JTokenType.String)
            {
                string? plain = token.Value<string>();
                if (!string.IsNullOrEmpty(plain))
                    yield return plain;
                continue;
            }

            if (token is not JObject obj)
                continue;

            var rules = obj["rules"]?.ToObject<List<RuleInfo>>();
            if (!RuleEvaluator.IsAllowed(rules, features))
                continue;

            var value = obj["value"];
            if (value is JArray array)
            {
                foreach (var item in array)
                {
                    if (item.Type == JTokenType.String)
                        yield return item.Value<string>()!;
                }
            }
            else if (value?.Type == JTokenType.String)
            {
                yield return value.Value<string>()!;
            }
        }
    }
}
