using System;
using System.IO;
using System.Threading.Tasks;
using TVLauncher.Models;
using TVLauncher.Services;
using Xunit;

namespace TVLauncher.Tests;

public sealed class CosmeticsServiceTests
{
    [Fact]
    public async Task EnsureModJarAsync_ExtractsTheEmbeddedJar()
    {
        using var dir = new TempDirectory();
        var config = new ConfigService(dir.Root);
        var service = new CosmeticsService(config, new FakeCosmeticsPublisher());

        bool ok = await service.EnsureModJarAsync();

        string path = Path.Combine(config.GetCosmeticsPath(), "tvl-cosmetics.jar");
        Assert.True(ok);
        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
    }

    [Fact]
    public async Task EnsureModJarAsync_IsIdempotent()
    {
        using var dir = new TempDirectory();
        var config = new ConfigService(dir.Root);
        var service = new CosmeticsService(config, new FakeCosmeticsPublisher());

        await service.EnsureModJarAsync();
        string path = Path.Combine(config.GetCosmeticsPath(), "tvl-cosmetics.jar");
        DateTime first = File.GetLastWriteTimeUtc(path);
        await Task.Delay(20);
        await service.EnsureModJarAsync();

        Assert.Equal(first, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task SyncCosmeticsToInstanceAsync_WritesTheModConfig()
    {
        using var dir = new TempDirectory();
        var config = new ConfigService(dir.Root);
        config.Config.CosmeticsSupabaseUrl = "https://demo.supabase.co";
        config.Config.CosmeticsSupabaseAnonKey = "anon-key";
        config.Config.EquippedCapeId = "cape_tvlauncher";
        config.Config.EquippedHatId = "hat_tvlauncher";
        var service = new CosmeticsService(config, new FakeCosmeticsPublisher());
        var instance = new MinecraftInstance
        {
            GameDirectory = dir.Combine("instance")
        };

        await service.SyncCosmeticsToInstanceAsync(instance);

        string path = Path.Combine(instance.GameDirectory, "config", "tvl-cosmetics.json");
        string json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"supabase_url\": \"https://demo.supabase.co\"", json);
        Assert.Contains("\"local_cape_id\": \"cape_tvlauncher\"", json);
        Assert.Contains("\"local_hat_id\": \"hat_tvlauncher\"", json);
        Assert.Contains("\"schema_version\": 1", json);
    }

    [Fact]
    public async Task SyncCosmeticsToInstanceAsync_DisablesTheModWhenCosmeticsAreDisabled()
    {
        using var dir = new TempDirectory();
        var config = new ConfigService(dir.Root);
        config.Config.EnableCosmetics = false;
        var service = new CosmeticsService(config, new FakeCosmeticsPublisher());
        var instance = new MinecraftInstance
        {
            GameDirectory = dir.Combine("instance")
        };

        await service.SyncCosmeticsToInstanceAsync(instance);

        string path = Path.Combine(instance.GameDirectory, "config", "tvl-cosmetics.json");
        string json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"enabled\": false", json);
    }

    [Fact]
    public void LoadCosmeticsConfig_RemovesObsoleteIds()
    {
        using var dir = new TempDirectory();
        var config = new ConfigService(dir.Root);
        config.Config.EquippedCapeId = "cape_tvlauncher";
        config.Config.EquippedHatId = "hat_tvlauncher";
        Directory.CreateDirectory(config.GetCosmeticsPath());
        File.WriteAllText(
            Path.Combine(config.GetCosmeticsPath(), "cosmetics_config.json"),
            """
            {
              "EquippedCosmetics": ["cape_1", "hat_1", "hat_tvlauncher"]
            }
            """);

        var service = new CosmeticsService(config, new FakeCosmeticsPublisher());

        Assert.DoesNotContain("cape_1", service.EquippedCosmetics);
        Assert.DoesNotContain("hat_1", service.EquippedCosmetics);
        Assert.Contains("hat_tvlauncher", service.EquippedCosmetics);
        Assert.Contains("cape_tvlauncher", service.EquippedCosmetics);
    }
}
