using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TVLauncher.Views;

public partial class ModrinthView : UserControl
{
    public ModrinthView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
