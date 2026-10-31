using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;
using TVLauncher.Models;
using TVLauncher.Services;

namespace TVLauncher.ViewModels;

public partial class SkinsPreviewViewModel : ViewModelBase
{
    private readonly CosmeticsService _cosmeticsService;
    private readonly AuthService _authService;
    private readonly SkinService _skinService;
    private readonly ConfigService _configService;
    private readonly string _skinsDir = string.Empty;

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
    private ObservableCollection<SavedSkinEntry> _savedSkins = new();

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
    public byte[]? HatTextureBytes { get; private set; }

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
        _skinsDir = Path.Combine(_configService.GetCachePath(), "library");
        Directory.CreateDirectory(_skinsDir);
        
        _skinService.StatusChanged += (s, msg) => StatusMessage = msg;
        
        if (_authService.ActiveAccount != null)
        {
            Username = _authService.ActiveAccount.Username;
        }
        
        LoadCosmetics();
        LoadSavedSkins();
        _ = RefreshEquippedStateAsync();
        Log.Info($"[Armario] ViewModel listo (usuario activo: '{Username}')");
    }

    private void LoadCosmetics()
    {
        AvailableCosmetics.Clear();
        // Cosméticos exclusivos TVLauncher
        AvailableCosmetics.Add(new CosmeticItem { Id = "cape_tvlauncher", Name = "Capa TVLauncher", Type = "cape", IsOwned = true });
        AvailableCosmetics.Add(new CosmeticItem { Id = "hat_tvlauncher", Name = "Sombrero TVLauncher", Type = "hat", IsOwned = true });
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

        if (!string.IsNullOrWhiteSpace(_configService.Config.EquippedCapeId) && !EquippedCosmetics.Contains(_configService.Config.EquippedCapeId))
            EquippedCosmetics.Add(_configService.Config.EquippedCapeId);
        if (!string.IsNullOrWhiteSpace(_configService.Config.EquippedHatId) && !EquippedCosmetics.Contains(_configService.Config.EquippedHatId))
            EquippedCosmetics.Add(_configService.Config.EquippedHatId);
    }

    private void LoadSavedSkins()
    {
        SavedSkins.Clear();
        if (_configService.Config.SavedSkins != null)
        {
            foreach (var s in _configService.Config.SavedSkins.OrderByDescending(x => x.AddedAt))
            {
                SavedSkins.Add(s);
            }
        }
        OnPropertyChanged(nameof(HasSavedSkins));
    }

    public bool HasSavedSkins => SavedSkins.Count > 0;

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

    [RelayCommand]
    private async Task SearchSkin()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            StatusMessage = "Introduce un nombre de usuario para buscar";
            return;
        }
        Username = Username.Trim();
        await LoadSkinAsync();
    }

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

                // Persistir la skin equipada (copia local + config) para el shell
                SaveEquippedSkinState(skin.SkinTexture, Username);

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

    /// <summary>
    /// Textura de la capa TVLauncher. El visor muestrea la región estándar de capa
    /// (x 0..22, y 0..17 en una textura de 64x64), así que todo el diseño —incluido
    /// el logo TVL— debe dibujarse dentro de ese rectángulo.
    /// </summary>
    private byte[] GenerateTvLauncherCape()
    {
        var info = new SKImageInfo(64, 32, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap(info);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint { IsAntialias = false };

        // Fondo: degradado vertical oscuro -> violeta en toda la zona de la capa.
        var colors = new[] { SKColor.Parse("#0F1115"), SKColor.Parse("#3B2E9B"), SKColor.Parse("#6C5CE7") };
        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, 0), new SKPoint(0, 17), colors, null, SKShaderTileMode.Clamp))
        {
            paint.Shader = shader;
            canvas.DrawRect(new SKRect(0, 0, 22, 17), paint);
            paint.Shader = null;
        }

        // Marco neón dentro de la ventana que ve la cámara (la capa se gira 180°
        // y la cara exterior muestrea x 3..13, y 1..17).
        paint.Color = SKColor.Parse("#6C5CE7");
        canvas.DrawRect(new SKRect(3, 0.6f, 3.7f, 16.4f), paint);
        canvas.DrawRect(new SKRect(12.3f, 0.6f, 13, 16.4f), paint);

        // Bandas de acento.
        paint.Color = SKColor.Parse("#8B7BFF");
        canvas.DrawRect(new SKRect(3.7f, 2, 12.3f, 2.7f), paint);
        canvas.DrawRect(new SKRect(3.7f, 14.3f, 12.3f, 15), paint);

        // Logo TVL: centrado en la cara exterior (x 3..13) y con sombra para contrastar.
        paint.Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
        paint.TextSize = 4.8f;
        paint.TextAlign = SKTextAlign.Center;
        float logoY = 11.2f;
        paint.Color = SKColor.Parse("#0B0C10");
        canvas.DrawText("TVL", 8, logoY + 0.6f, paint);
        paint.Color = SKColors.White;
        canvas.DrawText("TVL", 8, logoY, paint);
        paint.TextAlign = SKTextAlign.Left;

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    /// <summary>
    /// Textura del sombrero TVLauncher. Usa dos regiones del atlas: 0..21 para la
    /// copa y 32..58 para el ala (el visor muestrea esas UV concretas).
    /// </summary>
    private byte[] GenerateTvLauncherHat()
    {
        var info = new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap(info);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint { IsAntialias = false };

        // Copa: violeta brillante en toda la región para que se vea incluso de noche.
        var crownColors = new[] { SKColor.Parse("#A99BFF"), SKColor.Parse("#7D6FF0"), SKColor.Parse("#6C5CE7") };
        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, 0), new SKPoint(0, 8), crownColors, null, SKShaderTileMode.Clamp))
        {
            paint.Shader = shader;
            canvas.DrawRect(new SKRect(0, 0, 21, 8), paint);
            paint.Shader = null;
        }
        // Cinta oscura y filo luminoso en la parte baja de la copa.
        paint.Color = SKColor.Parse("#2A2360");
        canvas.DrawRect(new SKRect(0, 5.2f, 21, 6.4f), paint);
        paint.Color = SKColor.Parse("#C9C0FF");
        canvas.DrawRect(new SKRect(0, 6.4f, 21, 7.2f), paint);
        paint.Color = SKColor.Parse("#6C5CE7");
        canvas.DrawRect(new SKRect(0, 7.2f, 21, 8), paint);

        // Ala: violeta con borde neón y detalle diagonal.
        var brimColors = new[] { SKColor.Parse("#8B7BFF"), SKColor.Parse("#6C5CE7"), SKColor.Parse("#4A3BC7") };
        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, 0), new SKPoint(0, 6.4f), brimColors, null, SKShaderTileMode.Clamp))
        {
            paint.Shader = shader;
            canvas.DrawRect(new SKRect(32, 0, 58, 6.4f), paint);
            paint.Shader = null;
        }
        paint.Color = SKColor.Parse("#C9C0FF");
        canvas.DrawRect(new SKRect(32, 6.4f, 58, 7.2f), paint);
        paint.Color = SKColor.Parse("#3A2FA0");
        canvas.DrawRect(new SKRect(32, 7.2f, 58, 8), paint);
        paint.Color = SKColor.Parse("#A99BFF");
        canvas.DrawLine(36, 1.5f, 52, 5f, paint);

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
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
    public async Task SaveToLibrary()
    {
        if (CurrentSkin?.SkinTexture == null || string.IsNullOrWhiteSpace(Username))
        {
            StatusMessage = "Carga una skin antes de guardarla en tu biblioteca";
            return;
        }

        try
        {
            Username = Username.Trim();
            string fileName = $"{Username.ToLowerInvariant()}_{DateTime.UtcNow:yyyyMMddHHmmss}.png";
            string fullPath = Path.Combine(_skinsDir, fileName);
            await File.WriteAllBytesAsync(fullPath, CurrentSkin.SkinTexture);

            var entry = new SavedSkinEntry
            {
                Name = Username,
                FileName = fileName,
                Source = CurrentSkin.Source ?? "Mojang",
                AddedAt = DateTime.UtcNow
            };

            if (_configService.Config.SavedSkins == null)
                _configService.Config.SavedSkins = new List<SavedSkinEntry>();
            _configService.Config.SavedSkins.Add(entry);
            _configService.SaveConfig();

            SavedSkins.Insert(0, entry);
            OnPropertyChanged(nameof(HasSavedSkins));
            StatusMessage = $"«{Username}» guardado en Mis Skins";
        }
        catch (Exception ex)
        {
            Log.Error($"[Armario] Error guardando en biblioteca: {ex}");
            StatusMessage = $"Error al guardar: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task EquipSavedSkin(SavedSkinEntry? entry)
    {
        if (entry == null) return;
        string path = Path.Combine(_skinsDir, entry.FileName);
        if (!File.Exists(path))
        {
            StatusMessage = "El archivo de la skin guardada no existe";
            return;
        }

        try
        {
            byte[] data = await File.ReadAllBytesAsync(path);
            CurrentSkin = new SkinData { SkinTexture = data, Source = entry.Source, Model = "classic" };
            SkinLoaded?.Invoke(this, data);
            HasSkin = true;
            Username = entry.Name;
            IsSlim = false;
            SkinSource = $"Biblioteca · {entry.Name}";
            ApplyCape(null);
            SaveEquippedSkinState(data, entry.Name);
            StatusMessage = $"Skin «{entry.Name}» equipada desde Mis Skins";
        }
        catch (Exception ex)
        {
            Log.Error($"[Armario] Error equipando skin guardada: {ex}");
            StatusMessage = $"Error al equipar: {ex.Message}";
        }
    }

    [RelayCommand]
    public void DeleteSavedSkin(SavedSkinEntry? entry)
    {
        if (entry == null)
            return;

        try
        {
            string path = Path.Combine(_skinsDir, entry.FileName);
            if (File.Exists(path))
                File.Delete(path);

            _configService.Config.SavedSkins?.RemoveAll(s => s.Id == entry.Id);

            // Si esa skin era la equipada, se limpia también ese estado.
            if (!string.IsNullOrWhiteSpace(_configService.Config.EquippedSkinPath) &&
                string.Equals(Path.GetFullPath(_configService.Config.EquippedSkinPath),
                              Path.GetFullPath(path),
                              StringComparison.OrdinalIgnoreCase))
            {
                _configService.Config.EquippedSkinPath = null;
                _configService.Config.EquippedSkinUsername = null;
            }

            _configService.SaveConfig();

            SavedSkins.Remove(entry);
            OnPropertyChanged(nameof(HasSavedSkins));
            StatusMessage = $"«{entry.Name}» eliminada de Mis Skins";
            Log.Info($"[Armario] Skin eliminada de la biblioteca: {entry.FileName}");
        }
        catch (Exception ex)
        {
            Log.Error($"[Armario] Error eliminando skin guardada: {ex}");
            StatusMessage = $"No se pudo eliminar «{entry.Name}»: {ex.Message}";
        }
    }

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
                if (cosmetic.Id == "cape_tvlauncher")
                {
                    var capeBytes = GenerateTvLauncherCape();
                    ApplyCape(capeBytes);
                    _configService.Config.EquippedCapeId = "cape_tvlauncher";
                    _configService.SaveConfig();
                    StatusMessage = $"{cosmetic.Name} equipada correctamente";
                    break;
                }
                if (!string.IsNullOrWhiteSpace(cosmetic.TextureUrl))
                {
                    StatusMessage = $"Descargando {cosmetic.Name}...";
                    var capeBytes = await _skinService.DownloadTextureAsync(cosmetic.TextureUrl);
                    if (capeBytes != null)
                    {
                        ApplyCape(capeBytes);
                        _configService.Config.EquippedCapeId = cosmetic.Id;
                        _configService.SaveConfig();
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
                    _configService.Config.EquippedCapeId = cosmetic.Id;
                    _configService.SaveConfig();
                    StatusMessage = HasCape
                        ? $"{cosmetic.Name} activa (capa del jugador)"
                        : $"{cosmetic.Name} equipada. Carga tu skin de Mojang para ver tu capa.";
                }
                break;

            case "hat":
                if (cosmetic.Id == "hat_tvlauncher")
                {
                    HatTextureBytes = GenerateTvLauncherHat();
                }
                else if (!string.IsNullOrWhiteSpace(cosmetic.TextureUrl))
                {
                    HatTextureBytes = await _skinService.DownloadTextureAsync(cosmetic.TextureUrl);
                }
                IsHatEquipped = true;
                HatEquipped?.Invoke(this, cosmetic);
                _configService.Config.EquippedHatId = cosmetic.Id;
                _configService.SaveConfig();
                Log.Info($"[Armario] Sombrero equipado: {cosmetic.Id}");
                StatusMessage = $"{cosmetic.Name} equipado";
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
                if (_configService.Config.EquippedCapeId == cosmetic.Id)
                {
                    _configService.Config.EquippedCapeId = null;
                    _configService.SaveConfig();
                }
                StatusMessage = $"{cosmetic.Name} desequipada";
                break;

            case "hat":
                IsHatEquipped = false;
                HatTextureBytes = null;
                HatEquipped?.Invoke(this, null);
                if (_configService.Config.EquippedHatId == cosmetic.Id)
                {
                    _configService.Config.EquippedHatId = null;
                    _configService.SaveConfig();
                }
                StatusMessage = $"{cosmetic.Name} desequipado";
                break;

            case "wings":
                IsWingsEquipped = false;
                WingsEquipped?.Invoke(this, null);
                StatusMessage = $"{cosmetic.Name} desequipadas";
                break;
        }
    }

    private void SaveEquippedSkinState(byte[] skinData, string username)
    {
        try
        {
            // Un único archivo: se sobrescribe en cada equipado y no acumula copias.
            string fileName = "equipped.png";
            string path = Path.Combine(_skinsDir, fileName);
            File.WriteAllBytes(path, skinData);
            _configService.Config.EquippedSkinPath = path;
            _configService.Config.EquippedSkinUsername = username;
            _configService.SaveConfig();
        }
        catch (Exception ex)
        {
            Log.Warn($"[Armario] No se pudo persistir la skin equipada: {ex.Message}");
        }
    }

    /// <summary>
    /// Recarga desde <see cref="AppConfig"/> la skin, la capa y el sombrero equipados.
    /// Se llama al crear el armario y cada vez que el shell vuelve a primer plano,
    /// de modo que el header y el visor reflejan los cambios sin reiniciar la app.
    /// </summary>
    public async Task RefreshEquippedStateAsync()
    {
        try
        {
            LoadSavedSkins();

            // 1) Skin equipada (copia local persistida)
            string? skinPath = _configService.Config.EquippedSkinPath;
            if (!string.IsNullOrWhiteSpace(skinPath) && File.Exists(skinPath))
            {
                byte[] data = await File.ReadAllBytesAsync(skinPath);
                if (data.Length > 0)
                {
                    bool sameTexture = CurrentSkin?.SkinTexture is { Length: > 0 } current
                                       && current.AsSpan().SequenceEqual(data);

                    Username = _configService.Config.EquippedSkinUsername ?? Username;
                    IsSlim = false;
                    HasSkin = true;

                    if (!sameTexture)
                    {
                        CurrentSkin = new SkinData { SkinTexture = data, Source = "Guardado", Model = "classic" };
                        SkinSource = $"Guardado · {Username}";
                    }

                    SkinLoaded?.Invoke(this, data);
                }
            }

            // 2) Capa equipada
            if (_configService.Config.EquippedCapeId == "cape_tvlauncher")
            {
                ApplyCape(GenerateTvLauncherCape());
            }
            else if (string.IsNullOrWhiteSpace(_configService.Config.EquippedCapeId)
                     && CurrentSkin?.CapeTexture != null)
            {
                // Sin capa cosmética: se conserva la capa de Mojang del jugador.
                ApplyCape(CurrentSkin.CapeTexture);
            }

            // 3) Sombrero equipado
            if (_configService.Config.EquippedHatId == "hat_tvlauncher")
            {
                HatTextureBytes = GenerateTvLauncherHat();
                IsHatEquipped = true;
                HatEquipped?.Invoke(this, Hats.FirstOrDefault(h => h.Id == "hat_tvlauncher"));
            }
            else if (string.IsNullOrWhiteSpace(_configService.Config.EquippedHatId))
            {
                HatTextureBytes = null;
                IsHatEquipped = false;
                HatEquipped?.Invoke(this, null);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[Armario] No se pudo refrescar el estado equipado: {ex.Message}");
        }
    }

    // ---------- Propagación de estado al visor ----------
    partial void OnIsCapeVisibleChanged(bool value) => CapeVisibilityChanged?.Invoke(this, value);
    partial void OnIsSlimChanged(bool value) => SlimChanged?.Invoke(this, value);
    partial void OnWalkAnimationChanged(bool value) => WalkAnimationChanged?.Invoke(this, value);
    partial void OnAutoRotateChanged(bool value) => AutoRotateChanged?.Invoke(this, value);
}
