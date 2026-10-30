using TVLauncher.Helpers;
using TVLauncher.Models;

namespace TVLauncher.Tests;

public class RuleEvaluatorTests
{
    [Fact]
    public void IsAllowed_SinReglas_Permite()
    {
        Assert.True(RuleEvaluator.IsAllowed(null));
        Assert.True(RuleEvaluator.IsAllowed(new List<RuleInfo>()));
    }

    [Fact]
    public void IsAllowed_AllowDelSistemaActual_Permite()
    {
        var rules = new List<RuleInfo>
        {
            new() { Action = "allow", Os = new RuleOs { Name = RuleEvaluator.GetOsName() } }
        };

        Assert.True(RuleEvaluator.IsAllowed(rules));
    }

    [Fact]
    public void IsAllowed_AllowDeOtroSistema_NoCoincideYDeniega()
    {
        // El patrón real del metadata es {"allow"} + {"disallow", os}. Una regla
        // allow que no coincide con el SO actual no activa nada.
        var rules = new List<RuleInfo>
        {
            new() { Action = "allow", Os = new RuleOs { Name = "sistema-inexistente" } }
        };

        Assert.False(RuleEvaluator.IsAllowed(rules));
    }

    [Fact]
    public void IsAllowed_DisallowDelSistemaActual_GanaAlAllowGeneral()
    {
        var rules = new List<RuleInfo>
        {
            new() { Action = "allow" },
            new() { Action = "disallow", Os = new RuleOs { Name = RuleEvaluator.GetOsName() } }
        };

        Assert.False(RuleEvaluator.IsAllowed(rules));
    }

    [Fact]
    public void IsAllowed_DisallowDeOtroSistema_NoAfecta()
    {
        var rules = new List<RuleInfo>
        {
            new() { Action = "allow" },
            new() { Action = "disallow", Os = new RuleOs { Name = "sistema-inexistente" } }
        };

        Assert.True(RuleEvaluator.IsAllowed(rules));
    }

    [Fact]
    public void IsAllowed_FeatureActivaEnLaInstancia_DisallowCoincide()
    {
        var rules = new List<RuleInfo>
        {
            new() { Action = "allow" },
            new()
            {
                Action = "disallow",
                Features = new Dictionary<string, bool> { ["has_custom_resolution"] = true }
            }
        };

        var features = new Dictionary<string, bool> { ["has_custom_resolution"] = true };

        Assert.False(RuleEvaluator.IsAllowed(rules, features));
    }

    [Fact]
    public void IsAllowed_FeatureDesconocida_SeTrataComoFalsa()
    {
        var rules = new List<RuleInfo>
        {
            new() { Action = "allow" },
            new()
            {
                Action = "disallow",
                Features = new Dictionary<string, bool> { ["has_custom_resolution"] = true }
            }
        };

        // Sin diccionario de features la condición no se cumple: la regla se ignora.
        Assert.True(RuleEvaluator.IsAllowed(rules));
    }

    [Fact]
    public void IsAllowed_ArquitecturaDesconocida_NoCoincide()
    {
        var rules = new List<RuleInfo>
        {
            new() { Action = "allow", Os = new RuleOs { Arch = "sparc" } }
        };

        Assert.False(RuleEvaluator.IsAllowed(rules));
    }

    [Fact]
    public void IsAllowed_AccionEsCaseInsensitive()
    {
        var rules = new List<RuleInfo>
        {
            new() { Action = "ALLOW" },
            new() { Action = "DISALLOW", Os = new RuleOs { Name = RuleEvaluator.GetOsName() } }
        };

        Assert.False(RuleEvaluator.IsAllowed(rules));
    }

    [Fact]
    public void GetOsName_CoincideConLaPlataforma()
    {
        string esperado = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "osx"
            : "linux";

        Assert.Equal(esperado, RuleEvaluator.GetOsName());
    }
}
