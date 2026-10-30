using CmlLib.Core.Auth;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TVLauncher.Models;

namespace TVLauncher.Services;

public class AuthService
{
    private readonly ConfigService _configService;
    private readonly string _accountsFilePath;
    private List<MinecraftAccount> _accounts;
    private MSession? _currentSession;
    private readonly MicrosoftAuthService _microsoftAuthService;

    public List<MinecraftAccount> Accounts => _accounts;
    public MinecraftAccount? ActiveAccount => _accounts.FirstOrDefault(a => a.IsActive);
    public MSession? CurrentSession => _currentSession;
    public event EventHandler<MinecraftAccount>? AccountAdded;
    public event EventHandler<MinecraftAccount>? AccountRemoved;
    public event EventHandler<MinecraftAccount>? ActiveAccountChanged;
    public event EventHandler<DeviceCodeInfo>? MicrosoftDeviceCodeReceived;
    public event EventHandler<string>? MicrosoftAuthStatusChanged;

    public AuthService(ConfigService configService)
    {
        _configService = configService;
        _accountsFilePath = Path.Combine(_configService.Config.DataPath, "accounts.json");
        _accounts = new List<MinecraftAccount>();
        _microsoftAuthService = new MicrosoftAuthService(configService);
        
        _microsoftAuthService.DeviceCodeReceived += (_, info) => MicrosoftDeviceCodeReceived?.Invoke(this, info);
        _microsoftAuthService.StatusChanged += (_, status) => MicrosoftAuthStatusChanged?.Invoke(this, status);
        
        LoadAccounts();
        
        // Si hay una cuenta activa, crear la sesión al iniciar
        var active = ActiveAccount;
        if (active != null)
            CreateSessionForAccount(active);
    }

    public void LoadAccounts()
    {
        try
        {
            if (File.Exists(_accountsFilePath))
            {
                string json = File.ReadAllText(_accountsFilePath);
                var loadedAccounts = JsonConvert.DeserializeObject<List<MinecraftAccount>>(json);
                
                if (loadedAccounts != null)
                {
                    _accounts = loadedAccounts;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al cargar cuentas: {ex.Message}");
            _accounts = new List<MinecraftAccount>();
        }
    }

    public void SaveAccounts()
    {
        try
        {
            string json = JsonConvert.SerializeObject(_accounts, Formatting.Indented);
            File.WriteAllText(_accountsFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al guardar cuentas: {ex.Message}");
        }
    }

    public async Task SaveAccountsAsync()
    {
        try
        {
            string json = JsonConvert.SerializeObject(_accounts, Formatting.Indented);
            await File.WriteAllTextAsync(_accountsFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al guardar cuentas: {ex.Message}");
        }
    }

    public MinecraftAccount AddOfflineAccount(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("El nombre de usuario no puede estar vacío", nameof(username));
        
        if (username.Length > 16)
            throw new ArgumentException("El nombre de usuario no puede tener más de 16 caracteres", nameof(username));
        
        if (_accounts.Any(a => a.Type == AccountType.Offline && a.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
        {
            throw new Exception("Ya existe una cuenta offline con ese nombre de usuario");
        }

        var account = new MinecraftAccount
        {
            Username = username.Trim(),
            Uuid = GetOfflineUuid(username.Trim()),
            Type = AccountType.Offline,
            AccessToken = "0",
            RefreshToken = string.Empty,
            LastUsed = DateTime.Now,
            IsActive = _accounts.Count == 0
        };

        _accounts.Add(account);
        
        if (account.IsActive)
        {
            SetActiveAccount(account.Id);
        }
        
        SaveAccounts();
        AccountAdded?.Invoke(this, account);
        
        return account;
    }

    /// <summary>
    /// Autentica con cuenta Microsoft usando Device Code Flow
    /// </summary>
    public async Task<MinecraftAccount> AddMicrosoftAccountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _microsoftAuthService.AuthenticateAsync(cancellationToken);
            
            if (_accounts.Any(a => a.Type == AccountType.Microsoft && a.Uuid == result.Uuid))
            {
                throw new Exception("Ya existe una cuenta Microsoft con este perfil");
            }
            
            var account = new MinecraftAccount
            {
                Username = result.Username,
                Uuid = result.Uuid,
                AccessToken = result.AccessToken,
                RefreshToken = result.RefreshToken,
                Type = AccountType.Microsoft,
                LastUsed = DateTime.Now,
                IsActive = _accounts.Count == 0
            };
            
            _accounts.Add(account);
            
            if (account.IsActive)
            {
                SetActiveAccount(account.Id);
            }
            
            SaveAccounts();
            AccountAdded?.Invoke(this, account);
            
            return account;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error al autenticar con Microsoft: {ex.Message}", ex);
        }
    }

    public void SetActiveAccount(string accountId)
    {
        foreach (var account in _accounts)
        {
            account.IsActive = false;
        }
        
        var activeAccount = _accounts.FirstOrDefault(a => a.Id == accountId);
        if (activeAccount != null)
        {
            activeAccount.IsActive = true;
            activeAccount.LastUsed = DateTime.Now;
            CreateSessionForAccount(activeAccount);
            SaveAccounts();
            ActiveAccountChanged?.Invoke(this, activeAccount);
        }
    }

    private void CreateSessionForAccount(MinecraftAccount account)
    {
        _currentSession = new MSession
        {
            Username = account.Username,
            UUID = account.Uuid,
            AccessToken = account.AccessToken
        };
    }

    public void RemoveAccount(string accountId)
    {
        var account = _accounts.FirstOrDefault(a => a.Id == accountId);
        if (account != null)
        {
            bool wasActive = account.IsActive;
            _accounts.Remove(account);
            
            if (wasActive && _accounts.Count > 0)
            {
                SetActiveAccount(_accounts[0].Id);
            }
            else if (wasActive)
            {
                _currentSession = null;
            }
            
            SaveAccounts();
            AccountRemoved?.Invoke(this, account);
        }
    }

    /// <summary>
    /// Genera un UUID offline válido según el estándar de Mojang (UUID v3 con namespace "OfflinePlayer:")
    /// </summary>
    private string GetOfflineUuid(string username)
    {
        using var md5 = MD5.Create();
        byte[] inputBytes = Encoding.UTF8.GetBytes("OfflinePlayer:" + username);
        byte[] hashBytes = md5.ComputeHash(inputBytes);
        
        hashBytes[6] = (byte)((hashBytes[6] & 0x0f) | 0x30);
        hashBytes[8] = (byte)((hashBytes[8] & 0x3f) | 0x80);
        
        return new Guid(hashBytes).ToString();
    }
}
