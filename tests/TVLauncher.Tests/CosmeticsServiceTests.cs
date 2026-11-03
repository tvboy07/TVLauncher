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

    [Fact]
    public async Task EquipCosmetic_PublishesAfterTheDebounce()
    {
        using var dir = new TempDirectory();
        var config = new ConfigService(dir.Root);
        var publisher = new FakeCosmeticsPublisher();
        var service = new CosmeticsService(config, publisher)
        {
            PublishDebounce = TimeSpan.Zero
        };

        service.EquipCosmetic("cape_tvlauncher");
        await Task.Delay(100);

        Assert.Equal(1, publisher.CallCount);
    }

    [Fact]
    public async Task UnequipCosmetic_PublishesAfterTheDebounce()
    {
        using var dir = new TempDirectory();
        var config = new ConfigService(dir.Root);
        var publisher = new FakeCosmeticsPublisher();
        var service = new CosmeticsService(config, publisher)
        {
            PublishDebounce = TimeSpan.Zero
        };
        service.EquippedCosmetics.Add("cape_tvlauncher");

        service.UnequipCosmetic("cape_tvlauncher");
        await Task.Delay(100);

        Assert.Equal(1, publisher.CallCount);
    }

    [Fact]
    public async Task SyncCosmeticsToInstanceAsync_CopiesTheEquippedSkin()
    {
        using var dir = new TempDirectory();
        var config = new ConfigService(dir.Root);
        string equipped = dir.Combine("equipped.png");
        File.WriteAllBytes(equipped, SkinImageNormalizerTests.CreatePng(64, 64, SkiaSharp.SKColors.Blue));
        config.Config.EquippedSkinPath = equipped;
        var service = new CosmeticsService(config, new FakeCosmeticsPublisher());
        var instance = new MinecraftInstance { GameDirectory = dir.Combine("instance") };

        await service.SyncCosmeticsToInstanceAsync(instance);

        string skinPath = Path.Combine(instance.GameDirectory, "config", "tvl-cosmetics-skin.png");
        Assert.True(File.Exists(skinPath));
        using var written = SkiaSharp.SKBitmap.Decode(skinPath);
        Assert.NotNull(written);
        Assert.Equal(64, written!.Width);
        string json = await File.ReadAllTextAsync(
            Path.Combine(instance.GameDirectory, "config", "tvl-cosmetics.json"));
        Assert.Contains("\"local_skin_file\": \"tvl-cosmetics-skin.png\"", json);
    }

    [Fact]
    public async Task SyncCosmeticsToInstanceAsync_OmitsSkinWhenNoneIsEquipped()
    {
        using var dir = new TempDirectory();
        var config = new ConfigService(dir.Root);
        config.Config.EquippedSkinPath = null;
        var service = new CosmeticsService(config, new FakeCosmeticsPublisher());
        var instance = new MinecraftInstance { GameDirectory = dir.Combine("instance") };

        await service.SyncCosmeticsToInstanceAsync(instance);

        Assert.False(File.Exists(
            Path.Combine(instance.GameDirectory, "config", "tvl-cosmetics-skin.png")));
        string json = await File.ReadAllTextAsync(
            Path.Combine(instance.GameDirectory, "config", "tvl-cosmetics.json"));
        Assert.Contains("\"local_skin_file\": null", json);
    }

    [Fact]
    public async Task InjectCosmeticsToInstanceAsync_DisablesTheModWhenCosmeticsAreOff()
    {
        using var dir = new TempDirectory();
        var config = new ConfigService(dir.Root);
        config.Config.EnableCosmetics = false;
        config.Config.AutoInjectCosmetics = true;
        var service = new CosmeticsService(config, new FakeCosmeticsPublisher());
        var instance = new MinecraftInstance { GameDirectory = dir.Combine("instance") };

        await service.InjectCosmeticsToInstanceAsync(instance);

        string json = await File.ReadAllTextAsync(
            Path.Combine(instance.GameDirectory, "config", "tvl-cosmetics.json"));
        Assert.Contains("\"enabled\": false", json);
        Assert.False(File.Exists(
            Path.Combine(instance.GameDirectory, "mods", "tvl-cosmetics.jar")));
    }

    [Fact]
    public async Task InjectCosmeticsToInstanceAsync_PublishesBeforeCopyingTheMod()
    {
        using var dir = new TempDirectory();
        var config = new ConfigService(dir.Root);
        var publisher = new FakeCosmeticsPublisher();
        var service = new CosmeticsService(config, publisher);
        var instance = new MinecraftInstance
        {
            GameDirectory = dir.Combine("instance")
        };

        await service.InjectCosmeticsToInstanceAsync(instance);

        Assert.Equal(1, publisher.CallCount);
        Assert.True(File.Exists(
            Path.Combine(instance.GameDirectory, "mods", "tvl-cosmetics.jar")));
    }
}
