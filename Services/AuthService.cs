using CmlLib.Core.Auth;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TVLauncher.Models;

namespace TVLauncher.Services;

public class AuthService
{
    private readonly ConfigService _configService;
    private readonly string _accountsFilePath;
    private List<MinecraftAccount> _accounts;
    private MSession? _currentSession;

    public List<MinecraftAccount> Accounts => _accounts;
    public MinecraftAccount? ActiveAccount => _accounts.FirstOrDefault(a => a.IsActive);
    public MSession? CurrentSession => _currentSession;
    public event EventHandler<MinecraftAccount>? AccountAdded;
    public event EventHandler<MinecraftAccount>? AccountRemoved;
    public event EventHandler<MinecraftAccount>? ActiveAccountChanged;

    public AuthService(ConfigService configService)
    {
        _configService = configService;
        _accountsFilePath = Path.Combine(_configService.Config.DataPath, "accounts.json");
        _accounts = new List<MinecraftAccount>();
        
        LoadAccounts();
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
        // Verificar si ya existe una cuenta offline con ese nombre
        if (_accounts.Any(a => a.Type == AccountType.Offline && a.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
        {
            throw new Exception("Ya existe una cuenta offline con ese nombre de usuario");
        }

        var account = new MinecraftAccount
        {
            Username = username,
            Uuid = GetOfflineUuid(username),
            Type = AccountType.Offline,
            AccessToken = "0",
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

    public async Task<MinecraftAccount> AddMicrosoftAccountAsync()
    {
        try
        {
            await Task.Delay(100);
            throw new Exception("Autenticación Microsoft no disponible en esta versión simplificada");
        }
        catch (Exception ex)
        {
            throw new Exception($"Error al autenticar con Microsoft: {ex.Message}", ex);
        }
    }

    public void SetActiveAccount(string accountId)
    {
        // Desactivar todas las cuentas
        foreach (var account in _accounts)
        {
            account.IsActive = false;
        }
        
        // Activar la cuenta seleccionada
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

    private string GetOfflineUuid(string username)
    {
        // Generar UUID offline basado en el nombre de usuario
        // Minecraft usa UUID v3 basado en "OfflinePlayer:" + username
        System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create();
        byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes("OfflinePlayer:" + username);
        byte[] hashBytes = md5.ComputeHash(inputBytes);
        
        // Formatear como UUID
        hashBytes[6] = (byte)(hashBytes[6] & 0x0f | 0x30);
        hashBytes[8] = (byte)(hashBytes[8] & 0x3f | 0x80);
        
        return new Guid(hashBytes).ToString();
    }
}
