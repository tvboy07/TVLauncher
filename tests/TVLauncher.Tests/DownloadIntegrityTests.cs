using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using TVLauncher.Services;

namespace TVLauncher.Tests;

/// <summary>
/// Prueba el circuito real de descarga (servidor HTTP local + archivo temporal)
/// para comprobar que un contenido corrupto nunca llega a publicarse como definitivo.
/// </summary>
public class DownloadIntegrityTests
{
    [Fact]
    public async Task EnsureFileAsync_ConHashCorrecto_InstalaElArchivo()
    {
        byte[] body = Encoding.UTF8.GetBytes("contenido-de-un-jar-valido");
        using var temp = new TempDirectory();
        using var server = new OneShotHttpServer(body);

        var service = ServiceOver(temp);
        string path = temp.Combine("libraries", "valido.jar");

        await service.EnsureFileAsync(server.Url, path, body.Length, Sha1(body), default, fatal: true);

        Assert.True(File.Exists(path));
        Assert.Equal(body, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task EnsureFileAsync_ConHashIncorrecto_NoPublicaElArchivo()
    {
        byte[] body = Encoding.UTF8.GetBytes("contenido-corrupto");
        using var temp = new TempDirectory();
        using var server = new OneShotHttpServer(body);

        var service = ServiceOver(temp);
        string path = temp.Combine("libraries", "corrupto.jar");

        await Assert.ThrowsAsync<IOException>(() =>
            service.EnsureFileAsync(server.Url, path, body.Length, new string('0', 40), default, fatal: true));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task EnsureFileAsync_ConArchivoYaValido_NoVuelveADescargar()
    {
        byte[] body = Encoding.UTF8.GetBytes("ya-instalado");
        using var temp = new TempDirectory();
        var service = ServiceOver(temp);
        string path = temp.WriteFile("libraries/ya-instalado.jar", "ya-instalado");

        // El servidor no está levantado: si intentara descargar, fallaría.
        await service.EnsureFileAsync("http://127.0.0.1:1/nada", path, body.Length, Sha1(body), default, fatal: true);

        Assert.Equal("ya-instalado", await File.ReadAllTextAsync(path));
    }

    // ---------- utilidades ----------

    private static GameFilesService ServiceOver(TempDirectory temp)
    {
        var config = new ConfigService();
        config.Config.DataPath = temp.Root;
        return new GameFilesService(config);
    }

    private static string Sha1(byte[] data)
        => Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();

    /// <summary>
    /// Servidor HTTP mínimo sobre TCP: evita depender de permisos de HttpListener
    /// y de la red. Responde siempre el mismo cuerpo.
    /// </summary>
    private sealed class OneShotHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly byte[] _body;

        public string Url { get; }

        public OneShotHttpServer(byte[] body)
        {
            _body = body;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Url = $"http://127.0.0.1:{port}/archivo";

            _ = Task.Run(ServeAsync);
        }

        private async Task ServeAsync()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch
                {
                    return; // listener detenido
                }

                using (client)
                {
                    NetworkStream stream = client.GetStream();
                    var request = new byte[4096];
                    await stream.ReadAsync(request);

                    byte[] headers = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 200 OK\r\nContent-Length: {_body.Length}\r\nConnection: close\r\n\r\n");

                    await stream.WriteAsync(headers);
                    await stream.WriteAsync(_body);
                    await stream.FlushAsync();
                }
            }
        }

        public void Dispose() => _listener.Stop();
    }
}
