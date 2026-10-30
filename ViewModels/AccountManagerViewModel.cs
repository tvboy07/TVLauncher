using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using TVLauncher.Models;
using TVLauncher.Services;
using TVLauncher.Views;

namespace TVLauncher.ViewModels;

public partial class AccountManagerViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private CancellationTokenSource? _microsoftAuthCts;

    [ObservableProperty]
    private ObservableCollection<MinecraftAccount> _accounts = new();

    [ObservableProperty]
    private MinecraftAccount? _activeAccount;

    [ObservableProperty]
    private string _offlineUsername = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isMicrosoftAuthenticating;

    [ObservableProperty]
    private string _deviceCode = string.Empty;

    [ObservableProperty]
    private string _verificationUrl = string.Empty;

    [ObservableProperty]
    private bool _showDeviceCode;

    public event EventHandler? CloseRequested;

    public AccountManagerViewModel(AuthService authService)
    {
        _authService = authService;
        _authService.AccountAdded += (_, _) => LoadAccounts();
        _authService.AccountRemoved += (_, _) => LoadAccounts();
        _authService.ActiveAccountChanged += (_, acc) => 
        {
            ActiveAccount = acc;
            LoadAccounts();
        };
        _authService.MicrosoftDeviceCodeReceived += OnMicrosoftDeviceCodeReceived;
        _authService.MicrosoftAuthStatusChanged += OnMicrosoftAuthStatusChanged;
        
        LoadAccounts();
    }

    private void LoadAccounts()
    {
        Accounts.Clear();
        foreach (var account in _authService.Accounts)
        {
            Accounts.Add(account);
        }
        ActiveAccount = _authService.ActiveAccount;
    }

    [RelayCommand]
    private async Task AddOfflineAccount()
    {
        if (string.IsNullOrWhiteSpace(OfflineUsername))
        {
            StatusMessage = "Introduce un nombre de usuario";
            return;
        }

        try
        {
            StatusMessage = "Añadiendo cuenta offline...";
            var account = _authService.AddOfflineAccount(OfflineUsername);
            OfflineUsername = string.Empty;
            StatusMessage = $"Cuenta {account.Username} añadida correctamente";
            LoadAccounts();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddMicrosoftAccount()
    {
        if (IsMicrosoftAuthenticating)
            return;

        try
        {
            IsMicrosoftAuthenticating = true;
            ShowDeviceCode = false;
            DeviceCode = string.Empty;
            VerificationUrl = string.Empty;
            _microsoftAuthCts = new CancellationTokenSource();

            StatusMessage = "Iniciando autenticación con Microsoft...";

            var account = await _authService.AddMicrosoftAccountAsync(_microsoftAuthCts.Token);
            
            ShowDeviceCode = false;
            StatusMessage = $"Cuenta {account.Username} añadida correctamente";
            LoadAccounts();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Autenticación cancelada";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            ShowDeviceCode = false;
        }
        finally
        {
            IsMicrosoftAuthenticating = false;
        }
    }

    [RelayCommand]
    private void CancelMicrosoftAuth()
    {
        _microsoftAuthCts?.Cancel();
        IsMicrosoftAuthenticating = false;
        ShowDeviceCode = false;
        StatusMessage = "Autenticación cancelada";
    }

    [RelayCommand]
    private void OpenVerificationUrl()
    {
        if (!string.IsNullOrWhiteSpace(VerificationUrl))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = VerificationUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    [RelayCommand]
    private void SetActive(MinecraftAccount account)
    {
        if (account == null) return;
        _authService.SetActiveAccount(account.Id);
        StatusMessage = $"Cuenta activa: {account.Username}";
    }

    [RelayCommand]
    private void RemoveAccount(MinecraftAccount account)
    {
        if (account == null) return;
        try
        {
            _authService.RemoveAccount(account.Id);
            StatusMessage = $"Cuenta {account.Username} eliminada";
            LoadAccounts();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnMicrosoftDeviceCodeReceived(object? sender, DeviceCodeInfo info)
    {
        Dispatcher.UIThread.Post(() =>
        {
            DeviceCode = info.UserCode;
            VerificationUrl = info.VerificationUri;
            ShowDeviceCode = true;
            StatusMessage = "Introduce el código en el navegador";
        });
    }

    private void OnMicrosoftAuthStatusChanged(object? sender, string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusMessage = status;
        });
    }
}
