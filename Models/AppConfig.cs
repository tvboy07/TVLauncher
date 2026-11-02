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
    public string CosmeticsSupabaseUrl { get; set; } = string.Empty;
    public string CosmeticsSupabaseAnonKey { get; set; } = string.Empty;

    /// <summary>
    /// URL del antiguo CDN del jar. Se conserva solo para poder leer
    /// configuraciones antiguas; el jar ya va embebido en el launcher.
    /// </summary>
    public string CosmeticsJarUrl { get; set; } = string.Empty;

    public bool AutoInjectCosmetics { get; set; } = true;
    
    // Skin previewer configuration
    public bool PreferMojangSessionServer { get; set; } = true;
    public bool UseCrafatarFallback { get; set; } = true;

    // Persistencia de skins y cosméticos equipados
    public string? EquippedSkinPath { get; set; }
    public string? EquippedSkinUsername { get; set; }

    /// <summary>ID del cosmético de capa equipado, o <c>null</c> si no hay.</summary>
    public string? EquippedCapeId { get; set; }

    /// <summary>ID del cosmético de sombrero equipado, o <c>null</c> si no hay.</summary>
    public string? EquippedHatId { get; set; }

    public List<SavedSkinEntry> SavedSkins { get; set; } = new();
}

public class SavedSkinEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Source { get; set; } = "Local";
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

public class DefaultSmpServer
{
    /// <summary>IP pública del servidor SMP por defecto (puerto 25565 verificado abierto).</summary>
    public const string DefaultIp = "147.15.17.142";

    public string Name { get; set; } = "TVLauncher SMP";

    /// <summary>IP pública del servidor SMP por defecto (147.15.17.142:25565).</summary>
    public string Ip { get; set; } = DefaultIp;

    public int Port { get; set; } = 25565;
    public bool HiddenAddress { get; set; } = false;
}
