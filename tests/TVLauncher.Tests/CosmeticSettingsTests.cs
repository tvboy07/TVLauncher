using TVLauncher.Services;
using TVLauncher.ViewModels;
using Xunit;

namespace TVLauncher.Tests;

public sealed class CosmeticSettingsTests
{
    [Fact]
    public void ConfigService_RoundTripsTheSupabaseSettings()
    {
        using var dir = new TempDirectory();
        var service = new ConfigService(dir.Root);
        service.Config.CosmeticsSupabaseUrl = "https://demo.supabase.co";
        service.Config.CosmeticsSupabaseAnonKey = "anon-key";
        service.SaveConfig();

        var reloaded = new ConfigService(dir.Root);

        Assert.Equal("https://demo.supabase.co", reloaded.Config.CosmeticsSupabaseUrl);
        Assert.Equal("anon-key", reloaded.Config.CosmeticsSupabaseAnonKey);
    }

    [Fact]
    public void ConfigService_IgnoresTheLegacyJarUrl()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("config.json", """
            {
              "CosmeticsJarUrl": "https://cdn.tvlauncher.net/cosmetics/tvl-cosmetics.jar",
              "CosmeticsSupabaseUrl": "https://demo.supabase.co"
            }
            """);

        var service = new ConfigService(dir.Root);

        Assert.Equal("https://demo.supabase.co", service.Config.CosmeticsSupabaseUrl);
    }

    [Fact]
    public void SettingsViewModel_SavesTheSupabaseSettings()
    {
        using var dir = new TempDirectory();
        var config = new ConfigService(dir.Root);
        var vm = new SettingsViewModel(config)
        {
            CosmeticsSupabaseUrl = "https://demo.supabase.co",
            CosmeticsSupabaseAnonKey = "anon-key"
        };

        vm.SaveSettingsCommand.Execute(null);

        Assert.Equal("https://demo.supabase.co", config.Config.CosmeticsSupabaseUrl);
        Assert.Equal("anon-key", config.Config.CosmeticsSupabaseAnonKey);
    }
}
