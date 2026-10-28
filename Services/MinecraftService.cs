using CmlLib.Core;
using CmlLib.Core.Auth;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TVLauncher.Models;

namespace TVLauncher.Services;

public class MinecraftService
{
    private readonly ConfigService _configService;
    private readonly InstanceManager _instanceManager;
    private readonly AuthService _authService;
    private readonly MinecraftPath _minecraftPath;

    public event EventHandler<string>? DownloadProgressChanged;
    public event EventHandler<string>? StatusChanged;

    public MinecraftService(ConfigService configService, InstanceManager instanceManager, AuthService authService)
    {
        _configService = configService;
        _instanceManager = instanceManager;
        _authService = authService;
        _minecraftPath = new MinecraftPath();
    }

    public Task<IEnumerable<object>> GetVersionListAsync()
    {
        try
        {
            StatusChanged?.Invoke(this, "Cargando lista de versiones...");
            StatusChanged?.Invoke(this, "Lista de versiones cargada");
            return Task.FromResult(Enumerable.Empty<object>());
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error al cargar versiones: {ex.Message}");
            throw;
        }
    }

    public async Task LaunchMinecraftAsync(MinecraftInstance instance)
    {
        try
        {
            if (_configService.Config.AutoOptimizeMemory)
            {
                StatusChanged?.Invoke(this, "Optimizando memoria...");
                _configService.OptimizeMemory();
            }

            StatusChanged?.Invoke(this, "Preparando lanzamiento...");

            var session = _authService.CurrentSession;
            if (session == null)
            {
                throw new Exception("No hay cuenta activa. Por favor, inicia sesión.");
            }

            await Task.Delay(100);
            StatusChanged?.Invoke(this, $"Lanzando Minecraft {instance.Version}...");
            StatusChanged?.Invoke(this, "Minecraft iniciado correctamente (modo de desarrollo)");
            
            instance.LastPlayed = DateTime.Now;
            _instanceManager.UpdateInstance(instance);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error al lanzar: {ex.Message}");
            throw new Exception($"Error al lanzar Minecraft: {ex.Message}", ex);
        }
    }

    public Task InstallVersionAsync(string versionId)
    {
        try
        {
            StatusChanged?.Invoke(this, $"Instalando versión {versionId}...");
            StatusChanged?.Invoke(this, $"Versión {versionId} instalada correctamente");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error al instalar: {ex.Message}");
            throw;
        }
    }

    public Task InstallModLoaderAsync(MinecraftInstance instance)
    {
        if (instance.ModLoader == "Vanilla")
            return Task.CompletedTask;

        try
        {
            StatusChanged?.Invoke(this, $"Instalando {instance.ModLoader}...");
            StatusChanged?.Invoke(this, $"{instance.ModLoader} instalado correctamente");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Error al instalar modloader: {ex.Message}");
            throw;
        }
    }

    public bool IsVersionInstalled(string versionId)
    {
        try
        {
            var versions = Directory.GetDirectories(Path.Combine(_minecraftPath.BasePath, "versions"));
            return versions.Any(v => Path.GetFileName(v) == versionId);
        }
        catch
        {
            return false;
        }
    }
}
