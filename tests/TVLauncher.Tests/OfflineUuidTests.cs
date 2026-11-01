using System.Collections.Generic;
using TVLauncher.Models;
using TVLauncher.Services;
using Xunit;

namespace TVLauncher.Tests;

public sealed class OfflineUuidTests
{
    [Fact]
    public void ComputeOfflineUuid_MatchesJavaForNotch()
    {
        string uuid = AuthService.ComputeOfflineUuid("Notch");
        Assert.Equal("b50ad385-829d-3141-a216-7e7d7539ba7f", uuid);
    }

    [Fact]
    public void LoadAccounts_MigratesTheLegacyOfflineUuid()
    {
        using var dir = new TempDirectory();
        var config = new ConfigService(dir.Root);
        var store = new AccountsStore(config.Config.DataPath);
        store.Save(new List<MinecraftAccount>
        {
            new()
            {
                Username = "Notch",
                Uuid = "85d30ab5-9d82-4131-a216-7e7d7539ba7f",
                Type = AccountType.Offline,
                IsActive = true
            }
        });

        var auth = new AuthService(config);

        Assert.Equal(
            "b50ad385-829d-3141-a216-7e7d7539ba7f",
            Assert.Single(auth.Accounts).Uuid);
    }
}
