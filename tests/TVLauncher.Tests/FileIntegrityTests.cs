using System.Text;
using TVLauncher.Services;

namespace TVLauncher.Tests;

public class FileIntegrityTests
{
    // SHA1 conocido del contenido "abc".
    private const string Sha1DeAbc = "a9993e364706816aba3e25717850c26c9cd0d89d";

    [Fact]
    public void ComputeSha1_DevuelveElHashDelContenido()
    {
        using var temp = new TempDirectory();
        string path = temp.WriteFile("abc.txt", "abc");

        Assert.Equal(Sha1DeAbc, GameFilesService.ComputeSha1(path));
    }

    [Fact]
    public void FileMatches_ConTamanoYHashCorrectos_EsValido()
    {
        using var temp = new TempDirectory();
        string path = temp.WriteFile("abc.txt", "abc");

        Assert.True(GameFilesService.FileMatches(path, 3, Sha1DeAbc));
    }

    [Fact]
    public void FileMatches_AceptaElHashEnMayusculas()
    {
        using var temp = new TempDirectory();
        string path = temp.WriteFile("abc.txt", "abc");

        Assert.True(GameFilesService.FileMatches(path, 3, Sha1DeAbc.ToUpperInvariant()));
    }

    [Fact]
    public void FileMatches_ConMismoTamanoPeroHashDistinto_EsInvalido()
    {
        // El caso que la comprobación por tamaño no detecta: un jar corrupto que
        // conserva exactamente el tamaño esperado.
        using var temp = new TempDirectory();
        string path = temp.WriteFile("corrupto.txt", "abd");

        Assert.False(GameFilesService.FileMatches(path, 3, Sha1DeAbc));
    }

    [Fact]
    public void FileMatches_ConTamanoDistinto_EsInvalido()
    {
        using var temp = new TempDirectory();
        string path = temp.WriteFile("abc.txt", "abc");

        Assert.False(GameFilesService.FileMatches(path, 99, Sha1DeAbc));
    }

    [Fact]
    public void FileMatches_SinHashSoloCompruebaElTamano()
    {
        using var temp = new TempDirectory();
        string path = temp.WriteFile("abc.txt", "abc");

        Assert.True(GameFilesService.FileMatches(path, 3, null));
        Assert.False(GameFilesService.FileMatches(path, 4, null));
    }

    [Fact]
    public void FileMatches_ConTamanoDesconocido_UsaSoloElHash()
    {
        using var temp = new TempDirectory();
        string path = temp.WriteFile("abc.txt", "abc");

        Assert.True(GameFilesService.FileMatches(path, 0, Sha1DeAbc));
        Assert.False(GameFilesService.FileMatches(path, 0, new string('0', 40)));
    }

    [Fact]
    public void FileMatches_SiElArchivoNoExiste_EsInvalido()
    {
        using var temp = new TempDirectory();

        Assert.False(GameFilesService.FileMatches(temp.Combine("no-existe.txt"), 0, Sha1DeAbc));
    }

    [Fact]
    public void FileMatches_ConHashVacio_NoValidaElContenido()
    {
        using var temp = new TempDirectory();
        string path = temp.WriteFile("abc.txt", "abc");

        Assert.True(GameFilesService.FileMatches(path, 3, string.Empty));
    }

    [Fact]
    public void ComputeSha1_ArchivoInexistente_Lanza()
    {
        using var temp = new TempDirectory();

        Assert.ThrowsAny<IOException>(() => GameFilesService.ComputeSha1(temp.Combine("no-existe.txt")));
    }
}
