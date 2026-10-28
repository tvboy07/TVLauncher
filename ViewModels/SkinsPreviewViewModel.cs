using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using TVLauncher.Models;
using TVLauncher.Services;

namespace TVLauncher.ViewModels;

public partial class SkinsPreviewViewModel : ViewModelBase
{
    private readonly CosmeticsService _cosmeticsService;
    private readonly AuthService _authService;
    private readonly SkinService _skinService;
    private readonly ConfigService _configService;

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private ObservableCollection<CosmeticItem> _availableCosmetics = new();

    [ObservableProperty]
    private ObservableCollection<string> _equippedCosmetics = new();

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private SkinData? _currentSkin;

    public event EventHandler<byte[]?>? SkinLoaded;

    public SkinsPreviewViewModel(CosmeticsService cosmeticsService, AuthService authService, SkinService skinService, ConfigService configService)
    {
        _cosmeticsService = cosmeticsService;
        _authService = authService;
        _skinService = skinService;
        _configService = configService;
        
        _skinService.StatusChanged += (s, msg) => StatusMessage = msg;
        
        if (_authService.ActiveAccount != null)
        {
            Username = _authService.ActiveAccount.Username;
        }
        
        LoadCosmetics();
        Log.Info($"[Armario] ViewModel listo (usuario activo: '{Username}')");
    }

    private void LoadCosmetics()
    {
        AvailableCosmetics.Clear();
        // Cosméticos de ejemplo
        AvailableCosmetics.Add(new CosmeticItem { Id = "cape_1", Name = "Capa TVLauncher", Type = "cape", IsOwned = true });
        AvailableCosmetics.Add(new CosmeticItem { Id = "hat_1", Name = "Sombrero Decorativo", Type = "hat", IsOwned = true });
        AvailableCosmetics.Add(new CosmeticItem { Id = "wings_1", Name = "Alas", Type = "wings", IsOwned = false, IsPremium = true });
        
        EquippedCosmetics.Clear();
        foreach (var id in _cosmeticsService.EquippedCosmetics)
        {
            EquippedCosmetics.Add(id);
        }
    }

    [RelayCommand]
    private async Task LoadSkin()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            StatusMessage = "Por favor, introduce un nombre de usuario";
            return;
        }
        
        IsLoading = true;
        StatusMessage = $"Cargando skin de {Username}...";
        Log.Info($"[Armario] Cargando skin de '{Username}'");
        
        try
        {
            bool preferMojang = _configService.Config.PreferMojangSessionServer;
            CurrentSkin = await _skinService.GetSkinAsync(Username, preferMojang);
            
            if (CurrentSkin?.SkinTexture != null)
            {
                Log.Info($"[Armario] Skin listo: {CurrentSkin.SkinTexture.Length} bytes ({CurrentSkin.Source}, {CurrentSkin.Model})");
                SkinLoaded?.Invoke(this, CurrentSkin.SkinTexture);
                StatusMessage = $"Skin cargado correctamente (Fuente: {CurrentSkin.Source}, Modelo: {CurrentSkin.Model})";
            }
            else
            {
                Log.Warn($"[Armario] Sin textura de skin para '{Username}'");
                StatusMessage = "No se pudo cargar el skin. Verifica el nombre de usuario.";
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[Armario] Error cargando skin: {ex}");
            StatusMessage = $"Error al cargar skin: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void EquipCosmetic(CosmeticItem cosmetic)
    {
        if (cosmetic.IsOwned)
        {
            _cosmeticsService.EquipCosmetic(cosmetic.Id);
            if (!EquippedCosmetics.Contains(cosmetic.Id))
            {
                EquippedCosmetics.Add(cosmetic.Id);
            }
        }
    }

    [RelayCommand]
    private void UnequipCosmetic(CosmeticItem cosmetic)
    {
        _cosmeticsService.UnequipCosmetic(cosmetic.Id);
        EquippedCosmetics.Remove(cosmetic.Id);
    }

    [ObservableProperty]
    private string _statusMessage = "Cargue su skin para previsualizar cosméticos";
}
