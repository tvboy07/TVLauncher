using System.IO;
using TVLauncher.Services;
using Xunit;

namespace TVLauncher.Tests;

/// <summary>
/// Migración de <c>config.json</c>: el hostname placeholder del SMP
/// (<c>play.tvlauncher.net</c>, que ya no resuelve) pasa a la IP real.
/// </summary>
public sealed class ConfigMigrationTests
{
    private const string RealIp = "147.15.17.142";

    [Fact]
    public void LoadConfig_ReplacesTheLegacySmpHostWithTheRealIp()
    {
        using var dir = new TempDirectory();
        string path = dir.WriteFile("config.json", """
            {
              "DefaultSmpServer": {
                "Name": "TVLauncher SMP",
                "Ip": "play.tvlauncher.net",
                "Port": 25565,
                "HiddenAddress": false
              }
            }
            """);

        var service = new ConfigService(dir.Root);

        Assert.Equal(RealIp, service.Config.DefaultSmpServer.Ip);
        string saved = File.ReadAllText(path);
        Assert.Contains(RealIp, saved, System.StringComparison.Ordinal);
        Assert.DoesNotContain("play.tvlauncher.net", saved, System.StringComparison.Ordinal);
    }

    [Fact]
    public void LoadConfig_MigratesTheHostCaseInsensitively()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("config.json", """
            { "DefaultSmpServer": { "Ip": "PLAY.TVLauncher.NET", "Port": 25565 } }
            """);

        var service = new ConfigService(dir.Root);

        Assert.Equal(RealIp, service.Config.DefaultSmpServer.Ip);
    }

    [Fact]
    public void LoadConfig_KeepsACustomSmpHost()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("config.json", """
            { "DefaultSmpServer": { "Ip": "mi-servidor.example.com", "Port": 25566 } }
            """);

        var service = new ConfigService(dir.Root);

        Assert.Equal("mi-servidor.example.com", service.Config.DefaultSmpServer.Ip);
        Assert.Equal(25566, service.Config.DefaultSmpServer.Port);
    }

    [Fact]
    public void LoadConfig_DoesNotRewriteTheFileWhenNothingToMigrate()
    {
        using var dir = new TempDirectory();
        string path = dir.WriteFile("config.json", """
            { "DefaultSmpServer": { "Ip": "mi-servidor.example.com", "Port": 25566 } }
            """);
        var before = File.GetLastWriteTimeUtc(path);

        _ = new ConfigService(dir.Root);

        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void FreshInstall_WritesTheRealIpByDefault()
    {
        using var dir = new TempDirectory();

        var service = new ConfigService(dir.Root);

        Assert.Equal(RealIp, service.Config.DefaultSmpServer.Ip);
        Assert.Contains(RealIp, File.ReadAllText(Path.Combine(dir.Root, "config.json")),
            System.StringComparison.Ordinal);
    }
}
