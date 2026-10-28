using System;

namespace TVLauncher.Models;

public class MinecraftAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Username { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public AccountType Type { get; set; } = AccountType.Offline;
    public DateTime LastUsed { get; set; } = DateTime.Now;
    public bool IsActive { get; set; } = false;
}

public enum AccountType
{
    Offline,
    Microsoft
}
