using Newtonsoft.Json.Linq;
using TVLauncher.Models;
using TVLauncher.Services;

namespace TVLauncher.Tests;

public class VersionMergeTests
{
    // ---------- LibraryKey ----------

    [Fact]
    public void LibraryKey_IgnoraLaVersion()
    {
        Assert.Equal(
            "org.apache.logging.log4j:log4j-core",
            GameFilesService.LibraryKey(Lib("org.apache.logging.log4j:log4j-core:2.8.1")));
    }

    [Fact]
    public void LibraryKey_ConservaElClasificador()
    {
        Assert.Equal(
            "org.lwjgl:lwjgl:natives-windows",
            GameFilesService.LibraryKey(Lib("org.lwjgl:lwjgl:3.2.2:natives-windows")));
    }

    [Fact]
    public void LibraryKey_NombreInvalido_SeDevuelveTalCual()
    {
        Assert.Equal("sin-coordenadas", GameFilesService.LibraryKey(Lib("sin-coordenadas")));
        Assert.Equal(string.Empty, GameFilesService.LibraryKey(Lib(string.Empty)));
    }

    // ---------- MergeVersions ----------

    [Fact]
    public void MergeVersions_ElPerfilSustituyeLaVersionDeLog4jDelPadre()
    {
        // Caso real: Forge 1.16.5 sube log4j-core 2.8.1 -> 2.15.0. Si ambas copias
        // acaban en el classpath, el crash report falla con NoSuchMethodError.
        var parent = Parent(new[]
        {
            Lib("org.apache.logging.log4j:log4j-core:2.8.1"),
            Lib("com.mojang:brigadier:1.0.18")
        });
        var child = Child(new[] { Lib("org.apache.logging.log4j:log4j-core:2.15.0") });

        var merged = GameFilesService.MergeVersions(parent, child);

        Assert.Single(merged.Libraries, l => l.Name.StartsWith("org.apache.logging.log4j:log4j-core"));
        Assert.Equal("org.apache.logging.log4j:log4j-core:2.15.0", merged.Libraries[0].Name);
        Assert.Contains(merged.Libraries, l => l.Name == "com.mojang:brigadier:1.0.18");
    }

    [Fact]
    public void MergeVersions_LasLibreriasDelPerfilVanPrimero()
    {
        var parent = Parent(new[] { Lib("padre:uno:1.0") });
        var child = Child(new[] { Lib("hijo:dos:1.0") });

        var merged = GameFilesService.MergeVersions(parent, child);

        Assert.Equal("hijo:dos:1.0", merged.Libraries[0].Name);
        Assert.Equal("padre:uno:1.0", merged.Libraries[1].Name);
    }

    [Fact]
    public void MergeVersions_ConservaLasVariantesDeArquitecturaDelMismoPadre()
    {
        // 1.16.5 declara lwjgl 3.2.1 (x86) y 3.2.2 (x86_64) con reglas distintas:
        // son legítimas y el merge no debe colapsarlas.
        var parent = Parent(new[]
        {
            Lib("org.lwjgl:lwjgl:3.2.1", arch: "x86"),
            Lib("org.lwjgl:lwjgl:3.2.2", arch: "x86_64")
        });

        var merged = GameFilesService.MergeVersions(parent, Child(Array.Empty<LibraryInfo>()));

        Assert.Equal(2, merged.Libraries.Count);
    }

    [Fact]
    public void MergeVersions_HereDaMainClassYArgumentos()
    {
        var parent = Parent(Array.Empty<LibraryInfo>());
        parent.MainClass = "padre.Main";
        parent.MinecraftArguments = "--username ${auth_player_name}";

        var child = Child(Array.Empty<LibraryInfo>());
        child.MainClass = string.Empty;

        var merged = GameFilesService.MergeVersions(parent, child);

        Assert.Equal("padre.Main", merged.MainClass);
        Assert.Equal("--username ${auth_player_name}", merged.MinecraftArguments);
        Assert.Null(merged.InheritsFrom);
    }

    [Fact]
    public void MergeVersions_ElPerfilGanaEnMainClass()
    {
        var parent = Parent(Array.Empty<LibraryInfo>());
        parent.MainClass = "padre.Main";

        var child = Child(Array.Empty<LibraryInfo>());
        child.MainClass = "cpw.mods.bootstraplauncher.BootstrapLauncher";

        var merged = GameFilesService.MergeVersions(parent, child);

        Assert.Equal("cpw.mods.bootstraplauncher.BootstrapLauncher", merged.MainClass);
    }

    [Fact]
    public void MergeVersions_ConcatenaArgumentosDelPadreYDelHijo()
    {
        var parent = Parent(Array.Empty<LibraryInfo>());
        parent.Arguments = new VersionArguments
        {
            Jvm = Tokens("""["-Dpadre=1"]"""),
            Game = Tokens("""["--padre"]""")
        };

        var child = Child(Array.Empty<LibraryInfo>());
        child.Arguments = new VersionArguments
        {
            Jvm = Tokens("""["-Dhijo=1"]"""),
            Game = Tokens("""["--hijo"]""")
        };

        var merged = GameFilesService.MergeVersions(parent, child);

        Assert.Equal(2, merged.Arguments!.Jvm.Count);
        Assert.Equal(2, merged.Arguments.Game.Count);
        Assert.Equal("-Dpadre=1", merged.Arguments.Jvm[0].ToString());
        Assert.Equal("-Dhijo=1", merged.Arguments.Jvm[1].ToString());
    }

    [Fact]
    public void MergeVersions_ClientSourceIdApuntaAQuienAportaElJar()
    {
        var parent = Parent(Array.Empty<LibraryInfo>());
        parent.Downloads = new VersionDownloads { Client = new DownloadItem { Url = "https://example/1.16.5.jar" } };

        var child = Child(Array.Empty<LibraryInfo>());
        child.Downloads = new VersionDownloads();

        var merged = GameFilesService.MergeVersions(parent, child);

        Assert.Equal("1.16.5", merged.ClientSourceId);
    }

    // ---------- utilidades ----------

    private static LibraryInfo Lib(string name, string? arch = null) => new()
    {
        Name = name,
        Rules = arch == null
            ? null
            : new List<RuleInfo>
            {
                new() { Action = "allow", Os = new RuleOs { Arch = arch } }
            }
    };

    private static MinecraftVersionJson Parent(IEnumerable<LibraryInfo> libraries) => new()
    {
        Id = "1.16.5",
        MainClass = "net.minecraft.client.main.Main",
        Type = "release",
        Libraries = libraries.ToList(),
        Downloads = new VersionDownloads()
    };

    private static MinecraftVersionJson Child(IEnumerable<LibraryInfo> libraries) => new()
    {
        Id = "1.16.5-forge-36.2.42",
        Type = "release",
        Libraries = libraries.ToList(),
        Downloads = new VersionDownloads()
    };

    private static List<JToken> Tokens(string json) => JArray.Parse(json).Cast<JToken>().ToList();
}
