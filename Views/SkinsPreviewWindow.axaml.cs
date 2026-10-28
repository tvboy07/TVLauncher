using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using System;
using TVLauncher.Controls;
using TVLauncher.Services;
using TVLauncher.ViewModels;

namespace TVLauncher.Views;

public partial class SkinsPreviewWindow : Window
{
    private readonly SkinsPreviewViewModel? _viewModel;
    private readonly SkinPreviewGLControl? _glControl;

    public SkinsPreviewWindow()
    {
        InitializeComponent();

        _viewModel = App.ServiceProvider?.GetRequiredService<SkinsPreviewViewModel>();
        DataContext = _viewModel;

        if (_viewModel != null)
        {
            _viewModel.SkinLoaded += OnSkinLoaded;
        }

        _glControl = this.FindControl<SkinPreviewGLControl>("SkinPreviewControl");
        Log.Info($"[Window] Armario abierto (control GL encontrado: {_glControl != null})");
    }

    private void OnSkinLoaded(object? sender, byte[]? skinData)
    {
        if (_glControl == null)
        {
            Log.Error("[Window] Skin recibido pero el control GL no existe");
            return;
        }

        if (skinData == null || skinData.Length == 0)
        {
            Log.Warn("[Window] Skin recibido vacío");
            return;
        }

        Log.Info($"[Window] Enviando skin al visor ({skinData.Length} bytes)");
        _glControl.SetSkinTexture(skinData);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.SkinLoaded -= OnSkinLoaded;
        }

        base.OnClosed(e);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
