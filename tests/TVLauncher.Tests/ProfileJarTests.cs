using Newtonsoft.Json.Linq;
using TVLauncher.Models;
using TVLauncher.Services;

namespace TVLauncher.Tests;

public class ProfileJarTests
{
    [Fact]
    public void EnsureProfileJar_SinVersionNameEnLosArgs_NoCopiaNada()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteFile("versions/1.20.1/1.20.1.jar", "client-jar");

        var version = Version("1.20.1-forge-47.1.106", jvm: """["-Dfoo=bar"]""");

        string result = GameFilesService.EnsureProfileJar(version, source, "1.20.1", temp.Combine("versions"));

        Assert.Equal(source, result);
        Assert.False(File.Exists(temp.Combine("versions", version.Id, $"{version.Id}.jar")));
    }

    [Fact]
    public void EnsureProfileJar_ConVersionName_CopiaElJarAlDirectorioDelPerfil()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteFile("versions/1.20.1/1.20.1.jar", "client-jar");

        var version = Version("1.20.1-forge-47.1.106",
            jvm: """["-DignoreList=bootstraplauncher-1.0.0.jar,${version_name}.jar"]""");

        string result = GameFilesService.EnsureProfileJar(version, source, "1.20.1", temp.Combine("versions"));

        string expected = temp.Combine("versions", version.Id, $"{version.Id}.jar");
        Assert.Equal(expected, result);
        Assert.Equal("client-jar", File.ReadAllText(expected));
        // La copia es atómica: no debe quedar el archivo temporal.
        Assert.False(File.Exists(expected + ".part"));
    }

    [Fact]
    public void EnsureProfileJar_RespetaUnJarPropioYaInstalado()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteFile("versions/1.20.1/1.20.1.jar", "client-jar");
        string own = temp.WriteFile("versions/1.20.1-forge-47.1.106/1.20.1-forge-47.1.106.jar", "optifine-jar");

        var version = Version("1.20.1-forge-47.1.106", jvm: """["-DignoreList=${version_name}.jar"]""");

        string result = GameFilesService.EnsureProfileJar(version, source, "1.20.1", temp.Combine("versions"));

        Assert.Equal(own, result);
        Assert.Equal("optifine-jar", File.ReadAllText(own));
    }

    [Fact]
    public void EnsureProfileJar_CuandoElPerfilEsElOrigen_DevuelveElJarOriginal()
    {
        using var temp = new TempDirectory();
        string source = temp.WriteFile("versions/1.20.1/1.20.1.jar", "client-jar");

        var version = Version("1.20.1", jvm: """["-DignoreList=${version_name}.jar"]""");

        string result = GameFilesService.EnsureProfileJar(version, source, "1.20.1", temp.Combine("versions"));

        Assert.Equal(source, result);
    }

    private static MinecraftVersionJson Version(string id, string jvm) => new()
    {
        Id = id,
        MainClass = "cpw.mods.bootstraplauncher.BootstrapLauncher",
        Libraries = new List<LibraryInfo>(),
        Arguments = new VersionArguments
        {
            Jvm = JArray.Parse(jvm).Cast<JToken>().ToList()
        }
    };
}
