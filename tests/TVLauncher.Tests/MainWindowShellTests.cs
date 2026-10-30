using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TVLauncher.ViewModels;
using Xunit;

namespace TVLauncher.Tests;

/// <summary>
/// Estructura del shell: sidebar con las cuatro páginas, header con la tarjeta de
/// perfil y barra inferior de lanzamiento. Los tests son estructurales (leen el
/// XAML) para no depender de un servidor gráfico en CI.
/// </summary>
public sealed class MainWindowShellTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    [Fact]
    public void Window_IsResizableWithTheAgreedStartingSize()
    {
        var window = LoadWindow();

        Assert.Equal("1200", (string?)window.Attribute("Width"));
        Assert.Equal("720", (string?)window.Attribute("Height"));
        Assert.Equal("1024", (string?)window.Attribute("MinWidth"));
        Assert.Equal("640", (string?)window.Attribute("MinHeight"));
        Assert.NotEqual("False", (string?)window.Attribute("CanResize"));
    }

    [Theory]
    [InlineData("ShowInstancesCommand", "Instancias")]
    [InlineData("ShowModrinthCommand", "Tienda Modrinth")]
    [InlineData("ShowCosmeticsCommand", "Cosméticos")]
    [InlineData("ShowSettingsCommand", "Ajustes")]
    public void Sidebar_HasANavigationButtonForEveryPage(string command, string label)
    {
        var window = LoadWindow();

        var button = window.Descendants(Avalonia + "Button")
            .FirstOrDefault(b => ((string?)b.Attribute("Command"))?.Contains(command) == true);

        Assert.True(button is not null, $"falta el botón de navegación con {command}");
        Assert.Contains(label, button!.Descendants(Avalonia + "TextBlock")
            .Select(t => (string?)t.Attribute("Text")));
        Assert.Equal("nav", (string?)button.Attribute("Classes"));
    }

    [Fact]
    public void Header_ShowsTheActiveProfileWithAvatarAndGamertag()
    {
        var window = LoadWindow();

        var profile = window.Descendants(Avalonia + "Button")
            .FirstOrDefault(b => (string?)b.Attribute("Classes") == "profile");
        Assert.True(profile is not null, "falta la tarjeta de perfil del header");

        var bindings = profile!.Descendants()
            .SelectMany(e => e.Attributes().Where(a => a.Value.StartsWith("{Binding", StringComparison.Ordinal)))
            .Select(a => a.Value)
            .ToList();

        Assert.Contains(bindings, binding => binding.Contains("ActiveAccountName"));
        Assert.Contains(bindings, binding => binding.Contains("AvatarInitials"));
        Assert.Contains(bindings, binding => binding.Contains("Avatar"));
        Assert.Contains(bindings, binding => binding.Contains("AccountStatus"));
    }

    [Fact]
    public void Dashboard_ListsInstancesAsSelectableCards()
    {
        var window = LoadWindow();

        var list = window.Descendants(Avalonia + "ListBox")
            .FirstOrDefault(l => (string?)l.Attribute("Classes") == "cards");

        Assert.True(list is not null, "el dashboard debe usar ListBox.cards");
        Assert.Contains("Instances", (string?)list!.Attribute("ItemsSource"));
        Assert.Contains("SelectedInstance", (string?)list.Attribute("SelectedItem"));

        var wrapPanel = list.Descendants(Avalonia + "WrapPanel").FirstOrDefault();
        Assert.True(wrapPanel is not null, "las tarjetas deben fluir en un WrapPanel");
    }

    [Fact]
    public void LaunchBar_HasAProminentPlayButtonBoundToTheLaunchState()
    {
        var window = LoadWindow();

        var play = window.Descendants(Avalonia + "Button")
            .FirstOrDefault(b => b.Descendants(Avalonia + "TextBlock")
                .Any(t => (string?)t.Attribute("Text") == "JUGAR"));

        Assert.True(play is not null, "falta el botón JUGAR");
        Assert.Contains("primary", (string?)play!.Attribute("Classes"));
        Assert.Contains("LaunchGameCommand", (string?)play.Attribute("Command"));
        Assert.Contains("CanLaunch", (string?)play.Attribute("IsEnabled"));

        var status = window.Descendants(Avalonia + "TextBlock")
            .FirstOrDefault(t => ((string?)t.Attribute("Text"))?.Contains("StatusMessage") == true);
        Assert.True(status is not null, "la barra debe mostrar el estado del lanzamiento");
    }

    [Theory]
    [InlineData("IsInstancesPage")]
    [InlineData("IsModrinthPage")]
    [InlineData("IsCosmeticsPage")]
    [InlineData("IsSettingsPage")]
    public void Shell_EmbedsEveryPageInTheSameWindow(string visibilityProperty)
    {
        var window = LoadWindow();

        var host = window.Descendants()
            .FirstOrDefault(e => ((string?)e.Attribute("IsVisible"))?.Contains(visibilityProperty) == true);

        Assert.True(host is not null, $"la página {visibilityProperty} no está embebida en el shell");
    }

    [Theory]
    [InlineData("Notch", "N")]
    [InlineData("Steve Jobs", "SJ")]
    [InlineData("tv_boy", "TB")]
    [InlineData("a-b", "AB")]
    [InlineData("", "?")]
    [InlineData("   ", "?")]
    public void AvatarFallback_BuildsUpToTwoInitials(string username, string expected)
    {
        Assert.Equal(expected, MainWindowViewModel.BuildInitials(username));
    }

    private static XElement LoadWindow()
        => XDocument.Load(Path.Combine(RepositoryRoot(), "Views", "MainWindow.axaml")).Root!;

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TVLauncher.csproj")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException("No se encontró la raíz del repositorio (TVLauncher.csproj).");

        return directory.FullName;
    }
}
