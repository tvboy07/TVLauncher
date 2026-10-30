using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace TVLauncher.Models;

// ---------- Manifest de versiones (piston-meta) ----------

public class VersionManifest
{
    [JsonProperty("latest")]
    public LatestVersions Latest { get; set; } = new();

    [JsonProperty("versions")]
    public List<VersionManifestEntry> Versions { get; set; } = new();
}

public class LatestVersions
{
    [JsonProperty("release")]
    public string Release { get; set; } = string.Empty;

    [JsonProperty("snapshot")]
    public string Snapshot { get; set; } = string.Empty;
}

public class VersionManifestEntry
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("url")]
    public string Url { get; set; } = string.Empty;

    [JsonProperty("sha1")]
    public string Sha1 { get; set; } = string.Empty;

    [JsonProperty("releaseTime")]
    public DateTime ReleaseTime { get; set; }
}

// ---------- version.json ----------

public class MinecraftVersionJson
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("inheritsFrom")]
    public string? InheritsFrom { get; set; }

    [JsonProperty("mainClass")]
    public string MainClass { get; set; } = string.Empty;

    [JsonProperty("minecraftArguments")]
    public string? MinecraftArguments { get; set; }

    [JsonProperty("assets")]
    public string? Assets { get; set; }

    [JsonProperty("assetIndex")]
    public AssetIndexInfo? AssetIndex { get; set; }

    [JsonProperty("downloads")]
    public VersionDownloads Downloads { get; set; } = new();

    [JsonProperty("javaVersion")]
    public JavaVersionInfo? JavaVersion { get; set; }

    [JsonProperty("logging")]
    public LoggingInfo? Logging { get; set; }

    [JsonProperty("libraries")]
    public List<LibraryInfo> Libraries { get; set; } = new();

    [JsonProperty("arguments")]
    public VersionArguments? Arguments { get; set; }

    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonIgnore]
    public string JsonPath { get; set; } = string.Empty;

    /// <summary>
    /// Id de la versión que aporta el client.jar (para versiones heredadas como Fabric/Forge,
    /// evita duplicar el jar en el directorio del modloader).
    /// </summary>
    [JsonIgnore]
    public string ClientSourceId { get; set; } = string.Empty;
}

public class VersionDownloads
{
    [JsonProperty("client")]
    public DownloadItem? Client { get; set; }

    [JsonProperty("server")]
    public DownloadItem? Server { get; set; }
}

public class DownloadItem
{
    [JsonProperty("url")]
    public string Url { get; set; } = string.Empty;

    [JsonProperty("sha1")]
    public string Sha1 { get; set; } = string.Empty;

    [JsonProperty("size")]
    public long Size { get; set; }

    [JsonProperty("path")]
    public string? Path { get; set; }

    [JsonProperty("id")]
    public string? Id { get; set; }
}

public class AssetIndexInfo : DownloadItem
{
}

public class JavaVersionInfo
{
    [JsonProperty("component")]
    public string Component { get; set; } = string.Empty;

    [JsonProperty("majorVersion")]
    public int MajorVersion { get; set; }
}

public class LoggingInfo
{
    [JsonProperty("client")]
    public LoggingClient? Client { get; set; }
}

public class LoggingClient
{
    [JsonProperty("argument")]
    public string Argument { get; set; } = string.Empty;

    [JsonProperty("file")]
    public DownloadItem File { get; set; } = new();

    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;
}

public class LibraryInfo
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("url")]
    public string? Url { get; set; }

    [JsonProperty("downloads")]
    public LibraryDownloads? Downloads { get; set; }

    [JsonProperty("natives")]
    public Dictionary<string, string>? Natives { get; set; }

    [JsonProperty("rules")]
    public List<RuleInfo>? Rules { get; set; }

    [JsonProperty("extract")]
    public ExtractInfo? Extract { get; set; }
}

public class LibraryDownloads
{
    [JsonProperty("artifact")]
    public DownloadItem? Artifact { get; set; }

    [JsonProperty("classifiers")]
    public Dictionary<string, DownloadItem>? Classifiers { get; set; }
}

public class ExtractInfo
{
    [JsonProperty("exclude")]
    public List<string>? Exclude { get; set; }
}

public class RuleInfo
{
    [JsonProperty("action")]
    public string Action { get; set; } = "allow";

    [JsonProperty("os")]
    public RuleOs? Os { get; set; }

    [JsonProperty("features")]
    public Dictionary<string, bool>? Features { get; set; }
}

public class RuleOs
{
    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("arch")]
    public string? Arch { get; set; }

    [JsonProperty("version")]
    public string? Version { get; set; }
}

public class VersionArguments
{
    [JsonProperty("game")]
    public List<JToken> Game { get; set; } = new();

    [JsonProperty("jvm")]
    public List<JToken> Jvm { get; set; } = new();
}

// ---------- Resultado de la resolución de archivos ----------

public class GameInstallation
{
    public string VersionId { get; init; } = string.Empty;
    public MinecraftVersionJson Version { get; init; } = new();
    public string VersionJarPath { get; init; } = string.Empty;
    public string NativesDirectory { get; init; } = string.Empty;
    public string AssetsDirectory { get; init; } = string.Empty;
    public string LibrariesDirectory { get; init; } = string.Empty;
    public string AssetIndexName { get; init; } = string.Empty;
    public int RequiredJavaMajor { get; init; }
    public string? LoggingConfigPath { get; init; }
}
