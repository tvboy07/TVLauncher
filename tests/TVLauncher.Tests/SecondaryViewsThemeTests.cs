using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace TVLauncher.Tests;

/// <summary>
/// Consistencia visual: ninguna vista puede volver a los colores sueltos de la
/// estética anterior. Todo debe salir de la paleta de <c>App.axaml</c>.
/// </summary>
public sealed class SecondaryViewsThemeTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    private static readonly string[] LegacyColors =
    {
        "#121212", "#1E1E1E", "#2D2D2D", "#3F3F3F", "#252525",
        "#AAAAAA", "#888888", "#666666", "#444444", "#CCCCCC",
        "#0078D4", "#107C10", "#00A4EF", "#D13438", "#7A1F1F"
    };

    [Theory]
    [InlineData("Views/MainWindow.axaml")]
    [InlineData("Views/InstanceEditWindow.axaml")]
    [InlineData("Views/CreateInstanceWindow.axaml")]
    [InlineData("Views/SetupWizardWindow.axaml")]
    [InlineData("Views/AccountManagerWindow.axaml")]
    [InlineData("Views/SettingsView.axaml")]
    [InlineData("Views/ModrinthView.axaml")]
    [InlineData("Views/CosmeticsView.axaml")]
    [InlineData("Controls/AdsBanner.axaml")]
    public void Views_DoNotUseLegacyHardcodedColors(string relativePath)
    {
        string xaml = File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));

        foreach (var color in LegacyColors)
            Assert.DoesNotContain(color, xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SecondaryScreens_AreStyledWithTheDesignSystemClasses()
    {
        foreach (var path in new[]
                 {
                     "Views/InstanceEditWindow.axaml",
                     "Views/CreateInstanceWindow.axaml",
                     "Views/SetupWizardWindow.axaml",
                     "Views/AccountManagerWindow.axaml",
                     "Views/SettingsView.axaml",
                     "Views/ModrinthView.axaml"
                 })
        {
            var document = XDocument.Load(Path.Combine(RepositoryRoot(), path));
            var classes = document.Descendants()
                .Select(e => (string?)e.Attribute("Classes"))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();

            Assert.True(classes.Any(c => c!.Contains("card") || c.Contains("primary") || c.Contains("ghost")),
                $"{path} debe usar clases del design system (card/primary/ghost)");
        }
    }

    [Fact]
    public void ModrinthAndSettings_AreEmbeddedViewsNotWindows()
    {
        var modrinth = XDocument.Load(Path.Combine(RepositoryRoot(), "Views", "ModrinthView.axaml")).Root!;
        var settings = XDocument.Load(Path.Combine(RepositoryRoot(), "Views", "SettingsView.axaml")).Root!;

        Assert.Equal("UserControl", modrinth.Name.LocalName);
        Assert.Equal("UserControl", settings.Name.LocalName);

        Assert.False(File.Exists(Path.Combine(RepositoryRoot(), "Views", "ModrinthBrowserWindow.axaml")));
        Assert.False(File.Exists(Path.Combine(RepositoryRoot(), "Views", "SettingsWindow.axaml")));
    }

    [Fact]
    public void EmbeddedViews_DoNotOwnTheirDataContext()
    {
        // El shell inyecta Modrinth/Settings como DataContext; las vistas no deben
        // asignarlo por code-behind (eso rompería la navegación embebida).
        foreach (var codeBehind in new[] { "Views/ModrinthView.axaml.cs", "Views/SettingsView.axaml.cs" })
        {
            string source = File.ReadAllText(Path.Combine(RepositoryRoot(), codeBehind));
            Assert.DoesNotContain("DataContext =", source, StringComparison.Ordinal);
        }
    }

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
