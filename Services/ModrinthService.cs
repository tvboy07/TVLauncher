using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TVLauncher.Services;

public class ModrinthService
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _downloadClient;
    private readonly ConfigService _configService;
    private const string ApiBaseUrl = "https://api.modrinth.com/v2";
    
    public event EventHandler<string>? StatusChanged;

    public ModrinthService(ConfigService configService)
    {
        _configService = configService;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TVLauncher/1.0.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        // Los archivos de Modrinth pesan cientos de MB (Pixelmon 1.20.1 = 392 MB):
        // el timeout por defecto de HttpClient (100 s) aborta la descarga a mitad.
        // El corte lo controla el CancellationToken del llamador.
        _downloadClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _downloadClient.DefaultRequestHeaders.Add("User-Agent", "TVLauncher/1.0.0");
    }

    public async Task<List<ModrinthProject>> SearchModsAsync(string query, string gameVersion = "", string modLoader = "", int limit = 20)
    {
        if (!_configService.Config.EnableModrinthStore)
        {
            return new List<ModrinthProject>();
        }

        try
        {
            StatusChanged?.Invoke(this, "Buscando mods en Modrinth...");
            
            string facets = "[[\"project_type:mod\"]]";
            if (!string.IsNullOrEmpty(gameVersion))
            {
                facets = $"[[\"project_type:mod\"],[\"versions:{gameVersion}\"]]";
            }
            if (!string.IsNullOrEmpty(modLoader))
            {
                facets = $"[[\"project_type:mod\"],[\"versions:{gameVersion}\"],[\"categories:{modLoader.ToLower()}\"]]";
            }
            
            string url = $"{ApiBaseUrl}/search?query={Uri.EscapeDataString(query)}&facets={Uri.EscapeDataString(facets)}&limit={limit}";
            string response = await _httpClient.GetStringAsync(url);
            
            var searchResult = JsonConvert.DeserializeObject<ModrinthSearchResult>(response);
            StatusChanged?.Invoke(this, $"Se encontraron {searchResult?.Hits?.Count ?? 0} mods");
            
            return searchResult?.Hits ?? new List<ModrinthProject>();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error al buscar mods: {ex.Message}");
            return new List<ModrinthProject>();
        }
    }

    public async Task<ModrinthProject?> GetProjectAsync(string projectId)
    {
        try
        {
            string url = $"{ApiBaseUrl}/project/{projectId}";
            string response = await _httpClient.GetStringAsync(url);
            return JsonConvert.DeserializeObject<ModrinthProject>(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al obtener proyecto: {ex.Message}");
            return null;
        }
    }

    public async Task<List<ModrinthVersion>> GetProjectVersionsAsync(string projectId, string gameVersion = "", string modLoader = "")    {
        try
        {
            string url = $"{ApiBaseUrl}/project/{projectId}/version";
            if (!string.IsNullOrEmpty(gameVersion) || !string.IsNullOrEmpty(modLoader))
            {
                List<string> filters = new();
                if (!string.IsNullOrEmpty(gameVersion)) filters.Add($"game_versions=[\"{gameVersion}\"]");
                if (!string.IsNullOrEmpty(modLoader)) filters.Add($"loaders=[\"{modLoader.ToLower()}\"]");
                url += "?" + string.Join("&", filters);
            }
            
            string response = await _httpClient.GetStringAsync(url);
            return JsonConvert.DeserializeObject<List<ModrinthVersion>>(response) ?? new List<ModrinthVersion>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al obtener versiones del proyecto: {ex.Message}");
            return new List<ModrinthVersion>();
        }
    }

    /// <summary>Versiones de Minecraft publicadas, según Modrinth.</summary>
    public async Task<List<string>> GetGameVersionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string response = await _httpClient.GetStringAsync($"{ApiBaseUrl}/tag/game_version", cancellationToken);
            return ParseGameVersions(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al obtener las versiones de Minecraft: {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>Convierte el payload de <c>/v2/tag/game_version</c> en la lista del desplegable.</summary>
    internal static List<string> ParseGameVersions(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        List<ModrinthGameVersionTag>? tags;
        try
        {
            tags = JsonConvert.DeserializeObject<List<ModrinthGameVersionTag>>(json);
        }
        catch (JsonException)
        {
            return new List<string>();
        }

        if (tags == null)
            return new List<string>();

        return tags
            .Where(t => !string.IsNullOrWhiteSpace(t.Version)
                        && string.Equals(t.VersionType, "release", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.Date)
            .Select(t => t.Version!)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Descarga un archivo a disco por streaming, con progreso 0-100. Escribe en
    /// un <c>.part</c> y solo lo publica cuando el tamaño recibido coincide con el
    /// declarado, para no dejar un mod corrupto en <c>mods/</c>.
    /// </summary>
    public async Task DownloadFileAsync(string url, string destinationPath,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string partPath = destinationPath + ".part";

        try
        {
            using var response = await _downloadClient.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(
                partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                byte[] buffer = new byte[81920];
                long received = 0;
                double lastReported = 0;

                while (true)
                {
                    int read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                        break;

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;

                    if (totalBytes is > 0)
                    {
                        double percent = received * 100d / totalBytes.Value;
                        if (percent - lastReported >= 0.5 || received == totalBytes.Value)
                        {
                            lastReported = percent;
                            progress?.Report(percent);
                        }
                    }
                }

                await destination.FlushAsync(cancellationToken);

                if (totalBytes is > 0 && received != totalBytes.Value)
                {
                    throw new IOException(
                        $"Descarga incompleta: se esperaban {totalBytes.Value} bytes y llegaron {received}.");
                }
            }

            // El FileStream ya está cerrado: el rename funciona también en Windows.
            File.Move(partPath, destinationPath, overwrite: true);
            progress?.Report(100d);

            Log.Info($"[Modrinth] Descargado {Path.GetFileName(destinationPath)} ({new FileInfo(destinationPath).Length} bytes)");
        }
        catch
        {
            TryDelete(partPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Limpieza best-effort del temporal.
        }
    }
}

public class ModrinthSearchResult
{
    [JsonProperty("hits")]
    public List<ModrinthProject> Hits { get; set; } = new();
    
    [JsonProperty("total_hits")]
    public int TotalHits { get; set; }
}

public class ModrinthProject
{
    [JsonProperty("project_id")]
    public string ProjectId { get; set; } = string.Empty;
    
    [JsonProperty("slug")]
    public string Slug { get; set; } = string.Empty;
    
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;
    
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;
    
    [JsonProperty("icon_url")]
    public string IconUrl { get; set; } = string.Empty;
    
    [JsonProperty("author")]
    public string Author { get; set; } = string.Empty;
    
    [JsonProperty("downloads")]
    public int Downloads { get; set; }
    
    [JsonProperty("categories")]
    public List<string> Categories { get; set; } = new();
}

public class ModrinthVersion
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonProperty("version_number")]
    public string VersionNumber { get; set; } = string.Empty;
    
    [JsonProperty("game_versions")]
    public List<string> GameVersions { get; set; } = new();
    
    [JsonProperty("loaders")]
    public List<string> Loaders { get; set; } = new();
    
    [JsonProperty("files")]
    public List<ModrinthFile> Files { get; set; } = new();
}

public class ModrinthFile
{
    [JsonProperty("url")]
    public string Url { get; set; } = string.Empty;
    
    [JsonProperty("filename")]
    public string Filename { get; set; } = string.Empty;
    
    [JsonProperty("primary")]
    public bool Primary { get; set; }

    /// <summary>Tamaño del archivo en bytes (Modrinth lo publica en el listado de versiones).</summary>
    [JsonProperty("size")]
    public long Size { get; set; }
}

/// <summary>Entrada de <c>/v2/tag/game_version</c>.</summary>
public class ModrinthGameVersionTag
{
    [JsonProperty("version")]
    public string? Version { get; set; }

    [JsonProperty("version_type")]
    public string? VersionType { get; set; }

    [JsonProperty("date")]
    public DateTime Date { get; set; }
}
