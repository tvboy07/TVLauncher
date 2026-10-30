using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TVLauncher.Models;
using TVLauncher.Services;
using TVLauncher.ViewModels;
using Xunit;

namespace TVLauncher.Tests;

/// <summary>
/// Circuito completo de la tienda: el ViewModel elige el archivo primario, lo
/// descarga en <c>{instancia}/mods</c>, verifica el SHA-512 publicado por
/// Modrinth y permite cancelar una transferencia en curso sin dejar restos.
/// </summary>
public sealed class ModrinthBrowserViewModelTests
{
    [Fact]
    public async Task DownloadMod_WritesTheFileIntoTheInstanceModsFolder()
    {
        byte[] body = Payload(64 * 1024);
        using var temp = new TempDirectory();
        using var server = new ChunkedHttpServer(body);
        var (vm, instance) = CreateViewModel(temp, server.Url, body, Sha512(body));

        await vm.DownloadModCommand.ExecuteAsync(null);

        string expected = Path.Combine(instance.GameDirectory, "mods", "ejemplo.jar");
        Assert.True(File.Exists(expected));
        Assert.Equal(body, await File.ReadAllBytesAsync(expected));
        Assert.False(File.Exists(expected + ".part"));
        Assert.False(vm.IsDownloading);
        Assert.Contains("descargado correctamente", vm.StatusMessage);
    }

    [Fact]
    public async Task DownloadMod_WithoutHash_StillWritesTheFile()
    {
        byte[] body = Payload(16 * 1024);
        using var temp = new TempDirectory();
        using var server = new ChunkedHttpServer(body);
        var (vm, instance) = CreateViewModel(temp, server.Url, body, sha512: null);

        await vm.DownloadModCommand.ExecuteAsync(null);

        string expected = Path.Combine(instance.GameDirectory, "mods", "ejemplo.jar");
        Assert.Equal(body, await File.ReadAllBytesAsync(expected));
    }

    [Fact]
    public async Task DownloadMod_WithWrongHash_ShowsErrorAndLeavesNoFile()
    {
        byte[] body = Payload(32 * 1024);
        using var temp = new TempDirectory();
        using var server = new ChunkedHttpServer(body);
        var (vm, instance) = CreateViewModel(temp, server.Url, body, new string('b', 128));

        await vm.DownloadModCommand.ExecuteAsync(null);

        string expected = Path.Combine(instance.GameDirectory, "mods", "ejemplo.jar");
        Assert.False(File.Exists(expected));
        Assert.False(File.Exists(expected + ".part"));
        Assert.Contains("SHA-512", vm.StatusMessage, StringComparison.Ordinal);
        Assert.False(vm.IsDownloading);
    }

    [Fact]
    public async Task CancelDownload_StopsTheTransferAndCleansUp()
    {
        // 400 KB en trozos de 4 KB con 15 ms entre envíos: la transferencia
        // tarda varios segundos, así que hay margen de sobra para cancelar.
        byte[] body = Payload(400 * 1024);
        using var temp = new TempDirectory();
        using var server = new ChunkedHttpServer(body, chunkSize: 4 * 1024, chunkDelayMs: 15);
        var (vm, instance) = CreateViewModel(temp, server.Url, body, Sha512(body));

        Task download = vm.DownloadModCommand.ExecuteAsync(null);
        await server.FirstBytesSent.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(vm.IsDownloading, "la descarga debería estar en curso");

        vm.CancelDownloadCommand.Execute(null);
        await download.WaitAsync(TimeSpan.FromSeconds(10));

        string expected = Path.Combine(instance.GameDirectory, "mods", "ejemplo.jar");
        Assert.False(File.Exists(expected));
        Assert.False(File.Exists(expected + ".part"));
        Assert.False(vm.IsDownloading);
        Assert.Contains("cancelada", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- utilidades ----------

    private static (ModrinthBrowserViewModel Vm, MinecraftInstance Instance) CreateViewModel(
        TempDirectory temp, string url, byte[] body, string? sha512)
    {
        var config = new ConfigService(temp.Root);
        var instances = new InstanceManager(config);
        MinecraftInstance instance = instances.GetSelectedInstance()
            ?? throw new InvalidOperationException("InstanceManager no creó la instancia por defecto.");

        var file = new ModrinthFile
        {
            Url = url,
            Filename = "ejemplo.jar",
            Primary = true,
            Size = body.Length
        };

        if (sha512 is not null)
            file.Hashes["sha512"] = sha512;

        var vm = new ModrinthBrowserViewModel(
            new ModrinthService(config), instances, autoLoadRemoteData: false)
        {
            SelectedVersion = new ModrinthVersion
            {
                Id = "v1",
                Name = "Ejemplo 1.0",
                VersionNumber = "1.0",
                GameVersions = new List<string> { "1.20.1" },
                Loaders = new List<string> { "fabric" },
                Files = new List<ModrinthFile> { file }
            },
            SelectedProject = new ModrinthProject { ProjectId = "ejemplo", Title = "Ejemplo" }
        };

        return (vm, instance);
    }

    private static string Sha512(byte[] data)
        => Convert.ToHexString(SHA512.HashData(data)).ToLowerInvariant();

    private static byte[] Payload(int size)
    {
        var bytes = new byte[size];
        for (int i = 0; i < size; i++)
            bytes[i] = (byte)(i % 251);
        return bytes;
    }

    /// <summary>
    /// Servidor HTTP local que envía el cuerpo por trozos con una pausa entre
    /// ellos. Permite comprobar progreso y cancelación reales (un cuerpo que se
    /// entrega de golpe no da tiempo a interrumpirlo).
    /// </summary>
    private sealed class ChunkedHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly byte[] _body;
        private readonly int _chunkSize;
        private readonly int _chunkDelayMs;
        private readonly TaskCompletionSource _firstBytesSent =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Url { get; }

        public Task FirstBytesSent => _firstBytesSent.Task;

        public ChunkedHttpServer(byte[] body, int chunkSize = int.MaxValue, int chunkDelayMs = 0)
        {
            _body = body;
            _chunkSize = chunkSize;
            _chunkDelayMs = chunkDelayMs;

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
                    try
                    {
                        NetworkStream stream = client.GetStream();
                        await stream.ReadAsync(new byte[4096]);

                        byte[] headers = Encoding.ASCII.GetBytes(
                            "HTTP/1.1 200 OK\r\n" +
                            $"Content-Length: {_body.Length}\r\n" +
                            "Content-Type: application/java-archive\r\n" +
                            "Connection: close\r\n\r\n");

                        await stream.WriteAsync(headers);

                        for (int offset = 0; offset < _body.Length; offset += _chunkSize)
                        {
                            int count = Math.Min(_chunkSize, _body.Length - offset);
                            await stream.WriteAsync(_body.AsMemory(offset, count));
                            await stream.FlushAsync();

                            _firstBytesSent.TrySetResult();

                            if (_chunkDelayMs > 0 && offset + count < _body.Length)
                                await Task.Delay(_chunkDelayMs);
                        }
                    }
                    catch (IOException)
                    {
                        // El cliente canceló y cerró el socket: el test ya lo controla.
                    }
                    catch (ObjectDisposedException)
                    {
                        // Idem.
                    }
                    finally
                    {
                        _firstBytesSent.TrySetResult();
                    }
                }
            }
        }

        public void Dispose() => _listener.Stop();
    }
}
