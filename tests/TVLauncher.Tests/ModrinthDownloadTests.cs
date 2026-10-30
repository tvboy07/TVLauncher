using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TVLauncher.Services;
using Xunit;

namespace TVLauncher.Tests;

/// <summary>
/// Descarga de mods a la instancia: streaming a disco con progreso, archivo
/// temporal y limpieza cuando algo falla. Los archivos reales de Modrinth pesan
/// cientos de MB (Pixelmon 1.20.1 = 392 MB), así que el circuito debe aguantar
/// sin cargar todo en memoria ni depender del timeout por defecto de HttpClient.
/// </summary>
public sealed class ModrinthDownloadTests
{
    [Fact]
    public async Task DownloadFileAsync_StreamsToDiskAndReportsProgress()
    {
        byte[] body = Payload(256 * 1024);
        using var temp = new TempDirectory();
        using var server = new StreamingHttpServer(body);
        var service = ServiceOver(temp);
        string path = temp.Combine("mods", "pixelmon.jar");
        var progress = new RecordingProgress();

        await service.DownloadFileAsync(server.Url, path, progress, default);

        Assert.Equal(body, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part"));

        var values = progress.Values;
        Assert.NotEmpty(values);
        Assert.Equal(100d, values[^1]);
        for (int i = 1; i < values.Count; i++)
            Assert.True(values[i] >= values[i - 1], "el progreso nunca debe retroceder");
    }

    [Fact]
    public async Task DownloadFileAsync_MultiChunkBody_ArrivesComplete()
    {
        byte[] body = Payload(1024 * 1024);
        using var temp = new TempDirectory();
        using var server = new StreamingHttpServer(body);
        var service = ServiceOver(temp);
        string path = temp.Combine("mods", "grande.jar");

        await service.DownloadFileAsync(server.Url, path, progress: null, default);

        Assert.Equal(body.Length, new FileInfo(path).Length);
        Assert.Equal(body[0], (await File.ReadAllBytesAsync(path))[0]);
        Assert.Equal(body[^1], (await File.ReadAllBytesAsync(path))[^1]);
    }

    [Fact]
    public async Task DownloadFileAsync_OnHttpError_LeavesNoPartialFiles()
    {
        using var temp = new TempDirectory();
        using var server = new StreamingHttpServer(Encoding.UTF8.GetBytes("no"), statusCode: 404);
        var service = ServiceOver(temp);
        string path = temp.Combine("mods", "inexistente.jar");

        await Assert.ThrowsAnyAsync<HttpRequestException>(
            () => service.DownloadFileAsync(server.Url, path, progress: null, default));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task DownloadFileAsync_OnTruncatedBody_DoesNotPublishAFile()
    {
        byte[] body = Payload(64 * 1024);
        using var temp = new TempDirectory();
        using var server = new StreamingHttpServer(body, declaredLength: body.Length + 4096);
        var service = ServiceOver(temp);
        string path = temp.Combine("mods", "cortado.jar");

        await Assert.ThrowsAnyAsync<IOException>(
            () => service.DownloadFileAsync(server.Url, path, progress: null, default));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task DownloadFileAsync_OverwritesAPreviousVersionAtomically()
    {
        byte[] body = Payload(32 * 1024);
        using var temp = new TempDirectory();
        using var server = new StreamingHttpServer(body);
        var service = ServiceOver(temp);
        string path = temp.WriteFile("mods/pixelmon.jar", "version-antigua");

        await service.DownloadFileAsync(server.Url, path, progress: null, default);

        Assert.Equal(body, await File.ReadAllBytesAsync(path));
    }

    // ---------- utilidades ----------

    private static ModrinthService ServiceOver(TempDirectory temp)
        => new(new ConfigService(temp.Root));

    private static byte[] Payload(int size)
    {
        var bytes = new byte[size];
        for (int i = 0; i < size; i++)
            bytes[i] = (byte)(i % 251);
        return bytes;
    }

    private sealed class RecordingProgress : IProgress<double>
    {
        public List<double> Values { get; } = new();

        public void Report(double value)
        {
            lock (Values)
                Values.Add(value);
        }
    }

    /// <summary>Servidor HTTP local que responde un cuerpo binario y controla el status y el Content-Length declarado.</summary>
    private sealed class StreamingHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly byte[] _body;
        private readonly int _statusCode;
        private readonly int _declaredLength;

        public string Url { get; }

        public StreamingHttpServer(byte[] body, int statusCode = 200, int? declaredLength = null)
        {
            _body = body;
            _statusCode = statusCode;
            _declaredLength = declaredLength ?? body.Length;

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Url = $"http://127.0.0.1:{port}/mod.jar";

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
                    return;
                }

                using (client)
                {
                    NetworkStream stream = client.GetStream();
                    await stream.ReadAsync(new byte[4096]);

                    string statusText = _statusCode == 200 ? "OK" : "Not Found";
                    byte[] headers = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 {_statusCode} {statusText}\r\n" +
                        $"Content-Length: {_declaredLength}\r\n" +
                        "Content-Type: application/java-archive\r\n" +
                        "Connection: close\r\n\r\n");

                    await stream.WriteAsync(headers);
                    await stream.WriteAsync(_body);
                    await stream.FlushAsync();
                }
            }
        }

        public void Dispose() => _listener.Stop();
    }
}
