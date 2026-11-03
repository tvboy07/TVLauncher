using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TVLauncher.Models;
using TVLauncher.Services;
using Xunit;

namespace TVLauncher.Tests;

public sealed class MojangSkinUploaderTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task UploadAsync_SendsMultipartToMojang()
    {
        var handler = new RecordingHandler(HttpStatusCode.NoContent, string.Empty);
        using var http = new HttpClient(handler);
        var uploader = new MojangSkinUploader(http);
        string skinPath = WriteSkin();

        SkinUploadResult result = await uploader.UploadAsync(
            MicrosoftAccount(), skinPath, slim: false, CancellationToken.None);

        Assert.Equal(SkinUploadStatus.Uploaded, result.Status);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(
            "https://api.minecraftservices.com/minecraft/profile/skins",
            handler.LastRequest.RequestUri!.ToString());
        Assert.Equal(
            "Bearer minecraft-token",
            handler.LastRequest.Headers.GetValues("Authorization").Single());
        Assert.Contains("multipart/form-data", handler.LastContentType);

        string body = Encoding.Latin1.GetString(handler.LastBody!);
        Assert.Contains("name=variant", body);
        Assert.Contains("classic", body);
        Assert.Contains("name=file", body);
        Assert.Contains("skin.png", body);
    }

    [Fact]
    public async Task UploadAsync_UploadsTheSlimVariant()
    {
        var handler = new RecordingHandler(HttpStatusCode.NoContent, string.Empty);
        using var http = new HttpClient(handler);
        var uploader = new MojangSkinUploader(http);

        SkinUploadResult result = await uploader.UploadAsync(
            MicrosoftAccount(), WriteSkin(), slim: true, CancellationToken.None);

        Assert.Equal(SkinUploadStatus.Uploaded, result.Status);
        string body = Encoding.Latin1.GetString(handler.LastBody!);
        Assert.Contains("slim", body);
    }

    [Fact]
    public async Task UploadAsync_SkipsOfflineAccounts()
    {
        var handler = new RecordingHandler(HttpStatusCode.NoContent, string.Empty);
        using var http = new HttpClient(handler);
        var uploader = new MojangSkinUploader(http);
        var account = new MinecraftAccount { Type = AccountType.Offline, AccessToken = "x" };

        SkinUploadResult result = await uploader.UploadAsync(
            account, WriteSkin(), slim: false, CancellationToken.None);

        Assert.Equal(SkinUploadStatus.Skipped, result.Status);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task UploadAsync_ReturnsFailedOnRejectedSkin()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.BadRequest,
            "{\"errorMessage\":\"Failed to parse skin texture\"}");
        using var http = new HttpClient(handler);
        var uploader = new MojangSkinUploader(http);

        SkinUploadResult result = await uploader.UploadAsync(
            MicrosoftAccount(), WriteSkin(), slim: false, CancellationToken.None);

        Assert.Equal(SkinUploadStatus.Failed, result.Status);
        Assert.Equal(400, result.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task UploadAsync_FailsWhenTheFileDoesNotExist()
    {
        var handler = new RecordingHandler(HttpStatusCode.NoContent, string.Empty);
        using var http = new HttpClient(handler);
        var uploader = new MojangSkinUploader(http);

        SkinUploadResult result = await uploader.UploadAsync(
            MicrosoftAccount(), _dir.Combine("missing.png"), slim: false, CancellationToken.None);

        Assert.Equal(SkinUploadStatus.Failed, result.Status);
        Assert.Null(handler.LastRequest);
    }

    private static MinecraftAccount MicrosoftAccount() => new()
    {
        Type = AccountType.Microsoft,
        AccessToken = "minecraft-token"
    };

    private string WriteSkin()
    {
        string path = _dir.Combine("skin.png");
        File.WriteAllBytes(path, SkinImageNormalizerTests.CreatePng(64, 64, SkiaSharp.SKColors.Green));
        return path;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public RecordingHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public byte[]? LastBody { get; private set; }
        public string LastContentType { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastContentType = request.Content?.Headers.ContentType?.ToString() ?? string.Empty;
            LastBody = request.Content == null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body)
            };
        }
    }
}
