using System;
using System.Collections.Generic;

namespace TVLauncher.Models;

public class MinecraftInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Nueva Instancia";
    public string Version { get; set; } = "1.20.1";
    public string VersionType { get; set; } = "release"; // release, snapshot, old_beta, old_alpha
    public string ModLoader { get; set; } = "Vanilla"; // Vanilla, Forge, Fabric, NeoForge, Quilt
    public string ModLoaderVersion { get; set; } = string.Empty;
    public int MaxRamGB { get; set; } = 4;
    public int MinRamGB { get; set; } = 2;
    public string GameDirectory { get; set; } = string.Empty;
    public string JavaPath { get; set; } = string.Empty;
    public List<string> JVMArgs { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastPlayed { get; set; } = DateTime.Now;
    public string IconPath { get; set; } = string.Empty;
    public bool ResolutionEnabled { get; set; } = false;
    public int ResolutionWidth { get; set; } = 854;
    public int ResolutionHeight { get; set; } = 480;
    public bool AutoConnectServer { get; set; } = true;
}
