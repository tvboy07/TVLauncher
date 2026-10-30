using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TVLauncher.ViewModels;

namespace TVLauncher.Views;

public partial class CreateInstanceWindow : Window
{
    public CreateInstanceWindow(CreateInstanceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
