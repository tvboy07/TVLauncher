using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using TVLauncher.Models;
using TVLauncher.ViewModels;

namespace TVLauncher.Views;

public partial class InstanceEditWindow : Window
{
    public InstanceEditWindow(InstanceEditViewModel viewModel)
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
