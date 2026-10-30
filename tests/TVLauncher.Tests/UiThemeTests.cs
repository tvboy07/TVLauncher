using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace TVLauncher.Tests;

/// <summary>
/// Contrato del design system: la paleta y los estilos globales viven en
/// App.axaml y no se duplican colores sueltos en las vistas. Estos tests
/// congelan la identidad visual acordada (<c>#0F1115</c>, <c>#1A1D24</c>,
/// <c>#2A2E3B</c>, acento <c>#6C5CE7</c>).
/// </summary>
public sealed class UiThemeTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("AppBackgroundBrush", "#0F1115")]
    [InlineData("SurfaceBrush", "#1A1D24")]
    [InlineData("BorderBrush", "#2A2E3B")]
    [InlineData("AccentBrush", "#6C5CE7")]
    [InlineData("AccentHoverBrush", "#7D6FF0")]
    [InlineData("AccentPressedBrush", "#5B4DFF")]
    [InlineData("TextPrimaryBrush", "#F4F5FA")]
    [InlineData("TextSecondaryBrush", "#A0A6B8")]
    [InlineData("TextMutedBrush", "#6C7385")]
    [InlineData("SuccessBrush", "#2FD170")]
    [InlineData("DangerBrush", "#E5484D")]
    public void Palette_DefinesTheAgreedColor(string key, string expectedColor)
    {
        var brushes = LoadBrushes();

        Assert.True(brushes.ContainsKey(key), $"falta el recurso {key} en App.axaml");
        Assert.Equal(expectedColor, brushes[key]);
    }

    [Fact]
    public void Palette_KeepsTheDarkBackgroundAndTheAccentAsTheOnlyBrightColor()
    {
        var brushes = LoadBrushes();

        Assert.Equal("#6C5CE7", brushes["AccentBrush"]);
        Assert.Equal("#7D6FF0", brushes["AccentHoverBrush"]);
        Assert.Equal("#5B4DFF", brushes["AccentPressedBrush"]);

        // El fondo y las superficies son oscuros: cada canal <= 0x40.
        foreach (var key in new[] { "AppBackgroundBrush", "SurfaceBrush", "BorderBrush" })
        {
            string hex = brushes[key][1..];
            for (int i = 0; i < 6; i += 2)
                Assert.True(Convert.ToInt32(hex.Substring(i, 2), 16) <= 0x40,
                    $"{key} debe ser oscuro, pero es {brushes[key]}");
        }
    }

    [Theory]
    [InlineData("Button")]
    [InlineData("TextBox")]
    [InlineData("ComboBox")]
    [InlineData("ListBoxItem")]
    [InlineData("ScrollViewer")]
    [InlineData("ProgressBar")]
    [InlineData("Button.primary")]
    [InlineData("Button.ghost")]
    [InlineData("Button.nav")]
    [InlineData("Border.card")]
    public void Styles_DefineEveryGlobalControl(string selector)
    {
        var selectors = LoadStyles().Select(style => (string?)style.Attribute("Selector")).Where(s => s is not null);

        Assert.Contains(selector, selectors!);
    }

    [Fact]
    public void Styles_UseRoundedCornersOnInteractiveControls()
    {
        var styles = LoadStyles();

        foreach (var selector in new[] { "Button", "TextBox", "ComboBox", "ListBoxItem" })
        {
            var candidates = styles.Where(s =>
            {
                string? current = (string?)s.Attribute("Selector");
                return current == selector ||
                       current?.StartsWith(selector + " /template/", StringComparison.Ordinal) == true;
            }).ToList();

            Assert.True(candidates.Count > 0, $"falta el estilo {selector}");

            var radius = candidates
                .Elements(Avalonia + "Setter")
                .Where(setter => (string?)setter.Attribute("Property") == "CornerRadius")
                .Select(setter => (string?)setter.Attribute("Value"))
                .FirstOrDefault();

            Assert.True(radius is "10" or "12",
                $"{selector} debe usar CornerRadius 10 o 12, pero usa '{radius}'");
        }
    }

    [Fact]
    public void Styles_DefineHoverAndPressedStatesForButtons()
    {
        var selectors = LoadStyles().Select(style => (string?)style.Attribute("Selector")).ToList();

        Assert.Contains("Button:pointerover /template/ ContentPresenter#PART_ContentPresenter", selectors);
        Assert.Contains("Button:pressed /template/ ContentPresenter#PART_ContentPresenter", selectors);
        Assert.Contains("Button.primary:pointerover /template/ ContentPresenter#PART_ContentPresenter", selectors);
        Assert.Contains("Button.primary:pressed /template/ ContentPresenter#PART_ContentPresenter", selectors);
    }

    [Fact]
    public void App_RequestsTheDarkThemeVariant()
    {
        var document = XDocument.Load(Path.Combine(RepositoryRoot(), "App.axaml"));

        Assert.Equal("Dark", (string?)document.Root!.Attribute("RequestedThemeVariant"));
    }

    private static Dictionary<string, string> LoadBrushes()
    {
        var document = XDocument.Load(Path.Combine(RepositoryRoot(), "App.axaml"));
        var resources = document.Root!.Element(Avalonia + "Application.Resources")!;

        return resources.Elements(Avalonia + "SolidColorBrush")
            .ToDictionary(
                brush => (string)brush.Attribute(X + "Key")!,
                brush => (string)brush.Attribute("Color")!);
    }

    private static List<XElement> LoadStyles()
        => XDocument.Load(Path.Combine(RepositoryRoot(), "App.axaml"))
            .Descendants(Avalonia + "Style")
            .ToList();

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
