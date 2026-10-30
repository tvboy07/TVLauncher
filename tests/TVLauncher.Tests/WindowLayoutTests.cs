using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace TVLauncher.Tests;

/// <summary>
/// Regresión del bug "no se ve el botón Guardar": las ventanas de instancia
/// tienen altura fija y más contenido del que cabe, así que el formulario debe
/// vivir dentro de un <c>ScrollViewer</c> y la acción principal quedar anclada
/// fuera del área desplazable.
/// </summary>
public sealed class WindowLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    [Theory]
    [InlineData("Views/InstanceEditWindow.axaml", "Guardar")]
    [InlineData("Views/CreateInstanceWindow.axaml", "Crear instancia")]
    public void InstanceWindows_ScrollTheFormAndPinTheActionButton(string relativePath, string actionLabel)
    {
        XDocument document = XDocument.Load(Path.Combine(RepositoryRoot(), relativePath));
        var scrollViewer = document.Descendants(Avalonia + "ScrollViewer").FirstOrDefault();

        Assert.True(scrollViewer is not null,
            $"{relativePath} debe envolver el formulario en un ScrollViewer para resoluciones ajustadas");

        var actionButton = document.Descendants(Avalonia + "Button")
            .FirstOrDefault(b => (string?)b.Attribute("Content") == actionLabel);

        Assert.True(actionButton is not null, $"no se encontró el botón '{actionLabel}'");
        Assert.DoesNotContain(actionButton!, scrollViewer!.Descendants(Avalonia + "Button"));
    }

    [Fact]
    public void InstanceWindows_KeepFixedHeightWithScrollableContent()
    {
        foreach (var path in new[] { "Views/InstanceEditWindow.axaml", "Views/CreateInstanceWindow.axaml" })
        {
            XDocument document = XDocument.Load(Path.Combine(RepositoryRoot(), path));
            var window = document.Root!;

            Assert.NotNull(window.Attribute("Height"));
            Assert.NotEmpty(document.Descendants(Avalonia + "ScrollViewer"));
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
