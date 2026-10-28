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
    private ObservableCollection<CosmeticItem> _capes = new();

    [ObservableProperty]
    private ObservableCollection<CosmeticItem> _hats = new();

    [ObservableProperty]
    private ObservableCollection<CosmeticItem> _wings = new();

    [ObservableProperty]
    private ObservableCollection<string> _equippedCosmetics = new();

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private SkinData? _currentSkin;

    /// <summary>Hay una skin cargada en el visor.</summary>
    [ObservableProperty]
    private bool _hasSkin;

    /// <summary>Hay una textura de capa disponible (Mojang o TVLauncher).</summary>
    [ObservableProperty]
    private bool _hasCape;

    /// <summary>Visibilidad de la capa en el visor 3D.</summary>
    [ObservableProperty]
    private bool _isCapeVisible = true;

    /// <summary>Modelo Alex (brazos de 3px) en lugar de Steve.</summary>
    [ObservableProperty]
    private bool _isSlim;

    [ObservableProperty]
    private bool _walkAnimation = true;

    [ObservableProperty]
    private bool _autoRotate = true;

    [ObservableProperty]
    private bool _isHatEquipped;

    [ObservableProperty]
    private bool _isWingsEquipped;

    [ObservableProperty]
    private string _statusMessage = "Cargue su skin para previsualizar cosméticos";

    [ObservableProperty]
    private string _skinSource = "Sin skin cargado";

    /// <summary>Textura de la capa actualmente aplicada (para restaurarla al desequipar).</summary>
    public byte[]? CapeTextureBytes { get; private set; }

    // ---------- Eventos hacia la vista (visor 3D) ----------
    public event EventHandler<byte[]?>? SkinLoaded;
    public event EventHandler<byte[]?>? CapeLoaded;
    public event EventHandler<bool>? CapeVisibilityChanged;
    public event EventHandler<bool>? SlimChanged;
    public event EventHandler<bool>? WalkAnimationChanged;
    public event EventHandler<bool>? AutoRotateChanged;
    public event EventHandler<CosmeticItem?>? HatEquipped;
    public event EventHandler<CosmeticItem?>? WingsEquipped;
    public event EventHandler? CloseRequested;

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
        // Cosméticos de ejemplo (la tienda remota los sustituirá)
        AvailableCosmetics.Add(new CosmeticItem { Id = "cape_1", Name = "Capa TVLauncher", Type = "cape", IsOwned = true });
        AvailableCosmetics.Add(new CosmeticItem { Id = "hat_1", Name = "Sombrero Decorativo", Type = "hat", IsOwned = true });
        AvailableCosmetics.Add(new CosmeticItem { Id = "wings_1", Name = "Alas", Type = "wings", IsOwned = false, IsPremium = true });

        Capes.Clear();
        Hats.Clear();
        Wings.Clear();

        foreach (var cosmetic in AvailableCosmetics)
        {
            switch (cosmetic.Type.ToLowerInvariant())
            {
                case "cape":
                    Capes.Add(cosmetic);
                    break;
                case "hat":
                    Hats.Add(cosmetic);
                    break;
                case "wings":
                    Wings.Add(cosmetic);
                    break;
            }
        }

        EquippedCosmetics.Clear();
        foreach (var id in _cosmeticsService.EquippedCosmetics)
        {
            EquippedCosmetics.Add(id);
        }
    }

    /// <summary>Carga la skin del usuario sólo si aún no hay una cargada (al abrir el armario).</summary>
    public async Task EnsureSkinLoadedAsync()
    {
        if (IsLoading || CurrentSkin?.SkinTexture != null)
            return;

        if (string.IsNullOrWhiteSpace(Username))
        {
            StatusMessage = "Introduce tu nickname y pulsa «Cargar Skin»";
            return;
        }

        await LoadSkinAsync();
    }

    [RelayCommand]
    private async Task LoadSkin() => await LoadSkinAsync();

    private async Task LoadSkinAsync()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            StatusMessage = "Por favor, introduce un nombre de usuario";
            return;
        }

        if (IsLoading)
            return;

        IsLoading = true;
        StatusMessage = $"Cargando skin de {Username}...";
        Log.Info($"[Armario] Cargando skin de '{Username}'");

        try
        {
            bool preferMojang = _configService.Config.PreferMojangSessionServer;
            var skin = await _skinService.GetSkinAsync(Username, preferMojang);
            CurrentSkin = skin;

            if (skin?.SkinTexture != null)
            {
                Log.Info($"[Armario] Skin listo: {skin.SkinTexture.Length} bytes ({skin.Source}, {skin.Model})");
                SkinLoaded?.Invoke(this, skin.SkinTexture);
                HasSkin = true;

                IsSlim = string.Equals(skin.Model, "slim", StringComparison.OrdinalIgnoreCase);
                SkinSource = $"{skin.Source} · {(IsSlim ? "Alex (slim)" : "Steve (classic)")}";

                // Capa original de Mojang: se aplica automáticamente al visor
                ApplyCape(skin.CapeTexture, fromMojang: true);

                StatusMessage = skin.CapeTexture != null
                    ? $"Skin y capa cargadas correctamente (Fuente: {skin.Source})"
                    : $"Skin cargado (Fuente: {skin.Source}). Este jugador no tiene capa de Mojang.";
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

    /// <summary>Aplica (o limpia) la textura de capa en el visor 3D.</summary>
    private void ApplyCape(byte[]? capeBytes, bool fromMojang = false)
    {
        CapeTextureBytes = capeBytes;
        HasCape = capeBytes is { Length: > 0 };

        if (HasCape)
        {
            IsCapeVisible = true;
            CapeLoaded?.Invoke(this, capeBytes);
            Log.Info($"[Armario] Capa aplicada al visor ({capeBytes!.Length} bytes, origen: {(fromMojang ? "Mojang" : "TVLauncher")})");
        }
        else
        {
            CapeLoaded?.Invoke(this, null);
            Log.Info("[Armario] Sin capa: visor limpia la textura de capa");
        }
    }

    [RelayCommand]
    private void ToggleCape() => IsCapeVisible = !IsCapeVisible;

    [RelayCommand]
    private void ToggleWalk() => WalkAnimation = !WalkAnimation;

    [RelayCommand]
    private void ToggleAutoRotate() => AutoRotate = !AutoRotate;

    [RelayCommand]
    private void ToggleSlim() => IsSlim = !IsSlim;

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task EquipCosmetic(CosmeticItem? cosmetic)
    {
        if (cosmetic == null)
            return;

        if (!cosmetic.IsOwned)
        {
            StatusMessage = $"«{cosmetic.Name}» es un cosmético premium";
            return;
        }

        _cosmeticsService.EquipCosmetic(cosmetic.Id);
        if (!EquippedCosmetics.Contains(cosmetic.Id))
        {
            EquippedCosmetics.Add(cosmetic.Id);
        }

        switch (cosmetic.Type.ToLowerInvariant())
        {
            case "cape":
                if (!string.IsNullOrWhiteSpace(cosmetic.TextureUrl))
                {
                    StatusMessage = $"Descargando {cosmetic.Name}...";
                    var capeBytes = await _skinService.DownloadTextureAsync(cosmetic.TextureUrl);
                    if (capeBytes != null)
                    {
                        ApplyCape(capeBytes);
                        StatusMessage = $"{cosmetic.Name} equipada correctamente";
                    }
                    else
                    {
                        StatusMessage = $"No se pudo descargar {cosmetic.Name}; se mantiene la capa actual";
                    }
                }
                else
                {
                    // Sin textura propia: se usa la capa de Mojang del jugador
                    IsCapeVisible = true;
                    StatusMessage = HasCape
                        ? $"{cosmetic.Name} activa (capa del jugador)"
                        : $"{cosmetic.Name} equipada. Carga tu skin de Mojang para ver tu capa.";
                }
                break;

            case "hat":
                IsHatEquipped = true;
                HatEquipped?.Invoke(this, cosmetic);
                Log.Info($"[Armario] Sombrero equipado: {cosmetic.Id}");
                StatusMessage = $"{cosmetic.Name} equipado (se verá en el juego)";
                break;

            case "wings":
                IsWingsEquipped = true;
                WingsEquipped?.Invoke(this, cosmetic);
                Log.Info($"[Armario] Alas equipadas: {cosmetic.Id}");
                StatusMessage = $"{cosmetic.Name} equipadas (se verán en el juego)";
                break;
        }
    }

    [RelayCommand]
    private void UnequipCosmetic(CosmeticItem? cosmetic)
    {
        if (cosmetic == null)
            return;

        _cosmeticsService.UnequipCosmetic(cosmetic.Id);
        EquippedCosmetics.Remove(cosmetic.Id);

        switch (cosmetic.Type.ToLowerInvariant())
        {
            case "cape":
                // Volver a la capa original de Mojang (si existe)
                ApplyCape(CurrentSkin?.CapeTexture);
                StatusMessage = $"{cosmetic.Name} desequipada";
                break;

            case "hat":
                IsHatEquipped = false;
                HatEquipped?.Invoke(this, null);
                StatusMessage = $"{cosmetic.Name} desequipado";
                break;

            case "wings":
                IsWingsEquipped = false;
                WingsEquipped?.Invoke(this, null);
                StatusMessage = $"{cosmetic.Name} desequipadas";
                break;
        }
    }

    // ---------- Propagación de estado al visor ----------
    partial void OnIsCapeVisibleChanged(bool value) => CapeVisibilityChanged?.Invoke(this, value);
    partial void OnIsSlimChanged(bool value) => SlimChanged?.Invoke(this, value);
    partial void OnWalkAnimationChanged(bool value) => WalkAnimationChanged?.Invoke(this, value);
    partial void OnAutoRotateChanged(bool value) => AutoRotateChanged?.Invoke(this, value);
}
