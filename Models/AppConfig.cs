using System;
using System.Collections.Generic;

namespace TVLauncher.Models;

public class AppConfig
{
    public string JavaPath { get; set; } = string.Empty;
    public int MaxRamGB { get; set; } = 4;
    public int MinRamGB { get; set; } = 2;
    public string SelectedInstance { get; set; } = "default";
    public bool EnableModrinthStore { get; set; } = true;
    public bool EnableCosmetics { get; set; } = true;
    public bool AutoOptimizeMemory { get; set; } = true;
    public DefaultSmpServer DefaultSmpServer { get; set; } = new();
    public string Theme { get; set; } = "Dark";
    public bool FirstRun { get; set; } = true;
    public string DataPath { get; set; } = string.Empty;
    public string Language { get; set; } = "es-ES";
    public bool CloseOnLaunch { get; set; } = false;
    public bool KeepLauncherOpen { get; set; } = true;

    /// <summary>
    /// Client ID de la app pública de Azure usada para el login de Microsoft.
    /// Se puede sustituir por una app propia (con redirect http://localhost) sin
    /// recompilar el launcher.
    /// </summary>
    public string MicrosoftClientId { get; set; } = string.Empty;
    
    // Cosmetics configuration
    public string CosmeticsJarUrl { get; set; } = "https://cdn.tvlauncher.net/cosmetics/tvl-cosmetics.jar";
    public bool AutoInjectCosmetics { get; set; } = true;
    
    // Skin previewer configuration
    public bool PreferMojangSessionServer { get; set; } = true;
    public bool UseCrafatarFallback { get; set; } = true;
}

public class DefaultSmpServer
{
    public string Name { get; set; } = "TVLauncher SMP";
    public string Ip { get; set; } = "play.tvlauncher.net";
    public int Port { get; set; } = 25565;
    public bool HiddenAddress { get; set; } = false;
}
