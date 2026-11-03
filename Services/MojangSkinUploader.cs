using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using TVLauncher.Models;

namespace TVLauncher.Services;

public enum SkinUploadStatus
{
    Uploaded,
    Skipped,
    Failed
}

public sealed record SkinUploadResult(
    SkinUploadStatus Status,
    int? StatusCode = null,
    string? Error = null);

/// <summary>
/// Cambia la skin de la cuenta de Minecraft subiéndola al endpoint oficial.
/// Solo aplica a cuentas Microsoft; las offline se resuelven con el mod local.
/// </summary>
public interface ISkinUploader
{
    Task<SkinUploadResult> UploadAsync(
        MinecraftAccount account,
        string skinPath,
        bool slim,
        CancellationToken cancellationToken = default);
}

public sealed class MojangSkinUploader : ISkinUploader
{
    public const string Endpoint = "https://api.minecraftservices.com/minecraft/profile/skins";

    private readonly HttpClient _http;

    public MojangSkinUploader()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
    {
    }

    internal MojangSkinUploader(HttpClient http)
    {
        _http = http;
    }

    public async Task<SkinUploadResult> UploadAsync(
        MinecraftAccount account,
        string skinPath,
        bool slim,
        CancellationToken cancellationToken = default)
    {
        if (account.Type != AccountType.Microsoft || string.IsNullOrWhiteSpace(account.AccessToken))
            return new SkinUploadResult(SkinUploadStatus.Skipped);

        if (string.IsNullOrWhiteSpace(skinPath) || !File.Exists(skinPath))
            return new SkinUploadResult(SkinUploadStatus.Failed, Error: "No se encontró el archivo de la skin.");

        byte[] normalized;
        try
        {
            normalized = SkinImageNormalizer.Normalize(await File.ReadAllBytesAsync(skinPath, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return new SkinUploadResult(SkinUploadStatus.Failed, Error: ex.Message);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(slim ? "slim" : "classic"), "variant");

        var file = new ByteArrayContent(normalized);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", "skin.png");
        request.Content = content;

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return new SkinUploadResult(SkinUploadStatus.Uploaded);

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new SkinUploadResult(
                SkinUploadStatus.Failed,
                (int)response.StatusCode,
                string.IsNullOrWhiteSpace(body)
                    ? $"Mojang rechazó la skin ({(int)response.StatusCode})."
                    : body.Trim());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SkinUploadResult(SkinUploadStatus.Failed, Error: ex.Message);
        }
    }
}
