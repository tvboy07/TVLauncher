using Newtonsoft.Json.Linq;
using TVLauncher.Helpers;
using TVLauncher.Models;

namespace TVLauncher.Tests;

public class LaunchArgumentsBuilderTests
{
    [Fact]
    public void ToLogString_OcultaElTokenEnFormaSeparada()
    {
        var plan = new LaunchPlan
        {
            JavaPath = "java.exe",
            Arguments = new List<string>
            {
                "-Xmx4G", "net.minecraft.client.main.Main",
                "--username", "Steve", "--accessToken", "secreto-de-verdad"
            }
        };

        string log = plan.ToLogString();

        Assert.DoesNotContain("secreto-de-verdad", log);
        Assert.Contains("--accessToken ***", log);
    }

    [Fact]
    public void ToLogString_OcultaElTokenEnFormaUnida()
    {
        var plan = new LaunchPlan
        {
            JavaPath = "java.exe",
            Arguments = new List<string> { "--accessToken=secreto-de-verdad", "--version", "1.20.1" }
        };

        string log = plan.ToLogString();

        Assert.DoesNotContain("secreto-de-verdad", log);
        Assert.Contains("--accessToken=***", log);
    }

    [Fact]
    public void Build_ResuelveVersionNameAlIdDelPerfilNoAlDeLaInstancia()
    {
        using var temp = new TempDirectory();

        var version = Version(
            id: "1.20.1-forge-47.1.106",
            jvm: """["-DignoreList=bootstraplauncher-1.0.0.jar,${version_name}.jar"]""");

        var instance = new MinecraftInstance { Version = "1.20.1", GameDirectory = temp.Combine("game") };

        var plan = new LaunchArgumentsBuilder().Build(Context(instance, version, temp));

        Assert.Contains("-DignoreList=bootstraplauncher-1.0.0.jar,1.20.1-forge-47.1.106.jar", plan.Arguments);
        Assert.DoesNotContain(plan.Arguments, a => a.Contains("${version_name}"));
    }

    [Fact]
    public void Build_AplicaRamDelLauncherYProteccionLog4j()
    {
        using var temp = new TempDirectory();

        var instance = new MinecraftInstance
        {
            GameDirectory = temp.Combine("game"),
            MinRamGB = 2,
            MaxRamGB = 6
        };

        var plan = new LaunchArgumentsBuilder().Build(Context(instance, Version("1.20.1"), temp));

        Assert.Contains("-Xms2G", plan.Arguments);
        Assert.Contains("-Xmx6G", plan.Arguments);
        Assert.Contains("-Dlog4j2.formatMsgNoLookups=true", plan.Arguments);
        Assert.Contains("-Dminecraft.launcher.brand=TVLauncher", plan.Arguments);
    }

    [Fact]
    public void Build_ResuelveLosPlaceholdersDelJuego()
    {
        using var temp = new TempDirectory();

        var version = Version(
            "1.20.1",
            game: """["--username","${auth_player_name}","--uuid","${auth_uuid}","--gameDir","${game_directory}"]""");

        var instance = new MinecraftInstance { GameDirectory = temp.Combine("game") };

        var plan = new LaunchArgumentsBuilder().Build(Context(instance, version, temp));

        Assert.Contains("Alex", plan.Arguments);
        Assert.Contains("00000000000000000000000000000001", plan.Arguments);
        Assert.Contains(temp.Combine("game"), plan.Arguments);
    }

    [Fact]
    public void Build_VersionLegacyUsaMinecraftArgumentsYAnadeClasspathAMano()
    {
        using var temp = new TempDirectory();

        var version = Version("1.8.9");
        version.Arguments = null;
        version.MinecraftArguments = "--username ${auth_player_name} --version ${version_name}";

        var instance = new MinecraftInstance { GameDirectory = temp.Combine("game") };

        var plan = new LaunchArgumentsBuilder().Build(Context(instance, version, temp));

        int cp = plan.Arguments.IndexOf("-cp");
        Assert.True(cp >= 0);
        Assert.Equal(plan.Classpath, plan.Arguments[cp + 1]);
        Assert.Contains("1.8.9", plan.Arguments);
    }

    [Fact]
    public void Build_OmiteArgumentosConReglasQueNoSeCumplen()
    {
        using var temp = new TempDirectory();

        var version = Version(
            "1.20.1",
            game: """[{"rules":[{"action":"allow","features":{"is_demo_user":true}}],"value":"--demo"},{"rules":[{"action":"allow"}],"value":"--normal"}]""");

        var plan = new LaunchArgumentsBuilder().Build(
            Context(new MinecraftInstance { GameDirectory = temp.Combine("game") }, version, temp));

        Assert.DoesNotContain("--demo", plan.Arguments);
        Assert.Contains("--normal", plan.Arguments);
    }

    // ---------- utilidades ----------

    private static MinecraftVersionJson Version(string id, string? jvm = null, string? game = null) => new()
    {
        Id = id,
        MainClass = "net.minecraft.client.main.Main",
        Type = "release",
        Libraries = new List<LibraryInfo>(),
        Arguments = new VersionArguments
        {
            Jvm = Tokens(jvm ?? """["-Djava.library.path=${natives_directory}"]"""),
            Game = Tokens(game ?? """["--username","${auth_player_name}"]""")
        }
    };

    private static List<JToken> Tokens(string json)
        => JArray.Parse(json).Cast<JToken>().ToList();

    private static LaunchContext Context(MinecraftInstance instance, MinecraftVersionJson version, TempDirectory temp) => new()
    {
        Instance = instance,
        Username = "Alex",
        Uuid = "00000000-0000-0000-0000-000000000001",
        AccessToken = "token-de-prueba",
        JavaPath = temp.Combine("java.exe"),
        Installation = new GameInstallation
        {
            VersionId = version.Id,
            Version = version,
            VersionJarPath = temp.Combine("versions", version.Id, $"{version.Id}.jar"),
            NativesDirectory = temp.Combine("natives"),
            AssetsDirectory = temp.Combine("assets"),
            LibrariesDirectory = temp.Combine("libraries"),
            AssetIndexName = "5",
            RequiredJavaMajor = 17
        }
    };
}
