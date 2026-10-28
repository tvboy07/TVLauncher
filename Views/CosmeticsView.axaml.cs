using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using TVLauncher.Controls;
using TVLauncher.Services;
using TVLauncher.ViewModels;

namespace TVLauncher.Views;

/// <summary>
/// Armario de cosméticos embebido en la ventana principal (sin ventanas emergentes).
/// Conecta los eventos del <see cref="SkinsPreviewViewModel"/> con el visor OpenGL.
/// </summary>
public partial class CosmeticsView : UserControl
{
    private SkinPreviewGLControl? _glControl;
    private SkinsPreviewViewModel? _viewModel;

    public CosmeticsView()
    {
        InitializeComponent();

        _glControl = this.FindControl<SkinPreviewGLControl>("SkinPreviewControl");
        if (_glControl == null)
        {
            Log.Error("[Armario] No se encontró SkinPreviewControl en CosmeticsView");
        }

        DataContextChanged += (_, _) => Attach(DataContext as SkinsPreviewViewModel);
        Attach(DataContext as SkinsPreviewViewModel);
    }

    private void Attach(SkinsPreviewViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
            return;

        Detach();

        _viewModel = viewModel;
        if (_viewModel == null)
            return;

        _viewModel.SkinLoaded += OnSkinLoaded;
        _viewModel.CapeLoaded += OnCapeLoaded;
        _viewModel.CapeVisibilityChanged += OnCapeVisibilityChanged;
        _viewModel.SlimChanged += OnSlimChanged;
        _viewModel.WalkAnimationChanged += OnWalkAnimationChanged;
        _viewModel.AutoRotateChanged += OnAutoRotateChanged;
        _viewModel.HatEquipped += OnHatEquipped;
        _viewModel.WingsEquipped += OnWingsEquipped;

        // Estado actual del ViewModel -> visor
        if (_viewModel.CurrentSkin?.SkinTexture is { Length: > 0 } skin)
            _glControl?.SetSkinTexture(skin);

        _glControl?.SetCapeTexture(_viewModel.CapeTextureBytes);
        _glControl?.SetCapeVisible(_viewModel.IsCapeVisible);
        _glControl?.SetSlim(_viewModel.IsSlim);
        _glControl?.SetWalking(_viewModel.WalkAnimation);
        _glControl?.SetAutoRotate(_viewModel.AutoRotate);

        Log.Info($"[Armario] Vista enlazada al ViewModel (control GL: {_glControl != null})");
    }

    private void Detach()
    {
        if (_viewModel == null)
            return;

        _viewModel.SkinLoaded -= OnSkinLoaded;
        _viewModel.CapeLoaded -= OnCapeLoaded;
        _viewModel.CapeVisibilityChanged -= OnCapeVisibilityChanged;
        _viewModel.SlimChanged -= OnSlimChanged;
        _viewModel.WalkAnimationChanged -= OnWalkAnimationChanged;
        _viewModel.AutoRotateChanged -= OnAutoRotateChanged;
        _viewModel.HatEquipped -= OnHatEquipped;
        _viewModel.WingsEquipped -= OnWingsEquipped;

        _viewModel = null;
    }

    private void OnSkinLoaded(object? sender, byte[]? skinData)
    {
        if (_glControl == null)
        {
            Log.Error("[Armario] Skin recibido pero el control GL no existe");
            return;
        }

        if (skinData == null || skinData.Length == 0)
        {
            Log.Warn("[Armario] Skin recibido vacío");
            return;
        }

        Log.Info($"[Armario] Enviando skin al visor ({skinData.Length} bytes)");
        _glControl.SetSkinTexture(skinData);
    }

    private void OnCapeLoaded(object? sender, byte[]? capeData)
    {
        if (_glControl == null)
            return;

        // Textura de capa de Mojang (o de TVLauncher). null limpia la capa.
        _glControl.SetCapeTexture(capeData);
        _glControl.SetCapeVisible(_viewModel?.IsCapeVisible ?? true);
    }

    private void OnCapeVisibilityChanged(object? sender, bool visible) => _glControl?.SetCapeVisible(visible);

    private void OnSlimChanged(object? sender, bool slim) => _glControl?.SetSlim(slim);

    private void OnWalkAnimationChanged(object? sender, bool enabled) => _glControl?.SetWalking(enabled);

    private void OnAutoRotateChanged(object? sender, bool enabled) => _glControl?.SetAutoRotate(enabled);

    // Gorro y alas aún no se renderizan en el visor local: se dejan preparados
    // para que el equipo de arte/motor los conecte sin tocar la UI.
    private void OnHatEquipped(object? sender, CosmeticItem? cosmetic)
        => Log.Info($"[Armario] Evento HatEquipped: {(cosmetic?.Id ?? "ninguno")}");

    private void OnWingsEquipped(object? sender, CosmeticItem? cosmetic)
        => Log.Info($"[Armario] Evento WingsEquipped: {(cosmetic?.Id ?? "ninguno")}");

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
