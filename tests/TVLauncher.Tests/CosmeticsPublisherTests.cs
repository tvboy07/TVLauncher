using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TVLauncher.Models;
using TVLauncher.Services;
using Xunit;

namespace TVLauncher.Tests;

public sealed class CosmeticsPublisherTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task PublishAsync_SendsTheExpectedRequest()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"ok\":true}");
        using var http = new HttpClient(handler);
        var (config, _) = CreateServices();
        config.Config.CosmeticsSupabaseUrl = "https://demo.supabase.co";
        config.Config.CosmeticsSupabaseAnonKey = "anon-key";
        var publisher = new CosmeticsPublisher(config, CreateAuth(config), http);

        var result = await publisher.PublishAsync(
            "cape_tvlauncher", "hat_tvlauncher", "minecraft-token", CancellationToken.None);

        Assert.Equal(PublishCosmeticsStatus.Published, result.Status);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(
            "https://demo.supabase.co/functions/v1/publish-cosmetics",
            handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("anon-key", handler.LastRequest.Headers.GetValues("apikey").Single());
        Assert.Equal(
            "Bearer minecraft-token",
            handler.LastRequest.Headers.GetValues("Authorization").Single());
        Assert.Equal(
            "{\"cape_id\":\"cape_tvlauncher\",\"hat_id\":\"hat_tvlauncher\"}",
            handler.LastBody);
    }

    [Fact]
    public async Task PublishAsync_ReturnsFailedOn401WithoutThrowing()
    {
        var handler = new RecordingHandler(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_minecraft_token\"}");
        using var http = new HttpClient(handler);
        var (config, _) = CreateServices();
        config.Config.CosmeticsSupabaseUrl = "https://demo.supabase.co";
        config.Config.CosmeticsSupabaseAnonKey = "anon-key";
        var publisher = new CosmeticsPublisher(config, CreateAuth(config), http);

        var result = await publisher.PublishAsync(null, null, "token", CancellationToken.None);

        Assert.Equal(PublishCosmeticsStatus.Failed, result.Status);
        Assert.Equal(401, result.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task PublishAsync_ReturnsFailedOnTimeoutWithoutThrowing()
    {
        var handler = new ThrowingHandler(new TaskCanceledException("timeout"));
        using var http = new HttpClient(handler);
        var (config, _) = CreateServices();
        config.Config.CosmeticsSupabaseUrl = "https://demo.supabase.co";
        config.Config.CosmeticsSupabaseAnonKey = "anon-key";
        var publisher = new CosmeticsPublisher(config, CreateAuth(config), http);

        var result = await publisher.PublishAsync("cape_tvlauncher", null, "token", CancellationToken.None);

        Assert.Equal(PublishCosmeticsStatus.Failed, result.Status);
    }

    [Fact]
    public async Task PublishCurrentAsync_SkipsWhenSupabaseIsNotConfigured()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"ok\":true}");
        using var http = new HttpClient(handler);
        var (config, auth) = CreateServices();
        var publisher = new CosmeticsPublisher(config, auth, http);

        var result = await publisher.PublishCurrentAsync();

        Assert.Equal(PublishCosmeticsStatus.Skipped, result.Status);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task PublishCurrentAsync_SkipsOfflineAccounts()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"ok\":true}");
        using var http = new HttpClient(handler);
        var (config, auth) = CreateServices();
        config.Config.CosmeticsSupabaseUrl = "https://demo.supabase.co";
        config.Config.CosmeticsSupabaseAnonKey = "anon-key";
        SaveAccount(config, new MinecraftAccount
        {
            Username = "Notch",
            Uuid = "b50ad385-829d-3141-a216-7e7d7539ba7f",
            AccessToken = "offline-token",
            Type = AccountType.Offline,
            IsActive = true
        });
        var publisher = new CosmeticsPublisher(config, new AuthService(config), http);

        var result = await publisher.PublishCurrentAsync();

        Assert.Equal(PublishCosmeticsStatus.Skipped, result.Status);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task PublishCurrentAsync_PublishesTheEquippedIdsForMicrosoftAccounts()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"ok\":true}");
        using var http = new HttpClient(handler);
        var (config, auth) = CreateServices();
        config.Config.CosmeticsSupabaseUrl = "https://demo.supabase.co";
        config.Config.CosmeticsSupabaseAnonKey = "anon-key";
        config.Config.EquippedCapeId = "cape_tvlauncher";
        config.Config.EquippedHatId = "hat_tvlauncher";
        SaveAccount(config, new MinecraftAccount
        {
            Username = "Steve",
            Uuid = "11111111-2222-3333-4444-555555555555",
            AccessToken = "minecraft-token",
            Type = AccountType.Microsoft,
            IsActive = true
        });
        var publisher = new CosmeticsPublisher(config, new AuthService(config), http);

        var result = await publisher.PublishCurrentAsync();

        Assert.Equal(PublishCosmeticsStatus.Published, result.Status);
        Assert.Equal(
            "Bearer minecraft-token",
            handler.LastRequest!.Headers.GetValues("Authorization").Single());
        Assert.Contains("\"cape_id\":\"cape_tvlauncher\"", handler.LastBody);
        Assert.Contains("\"hat_id\":\"hat_tvlauncher\"", handler.LastBody);
    }

    private (ConfigService, AuthService) CreateServices()
    {
        var config = new ConfigService(_dir.Root);
        return (config, new AuthService(config));
    }

    private static AuthService CreateAuth(ConfigService config) => new(config);

    private static void SaveAccount(ConfigService config, MinecraftAccount account)
    {
        var store = new AccountsStore(config.Config.DataPath);
        store.Save(new List<MinecraftAccount> { account });
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _content;

        public RecordingHandler(HttpStatusCode status, string content)
        {
            _status = status;
            _content = content;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(_exception);
    }
}
