using System;
using System.IO;
using System.Threading.Tasks;
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
}
