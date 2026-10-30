using TVLauncher.Helpers;

namespace TVLauncher.Tests;

public class SecretMaskerTests
{
    [Fact]
    public void MaskJson_OcultaLosTokensDeMicrosoft()
    {
        const string json = """
            {"token_type":"Bearer","expires_in":86399,"scope":"XboxLive.signin offline_access","access_token":"EwBwA8l6BAAU...","refresh_token":"M.C5_BAY.0.U.-Cr...","id_token":"eyJ0eXAiOiJKV1Qi..."}
            """;

        string masked = SecretMasker.MaskJson(json);

        Assert.DoesNotContain("EwBwA8l6BAAU", masked);
        Assert.DoesNotContain("M.C5_BAY.0.U.-Cr", masked);
        Assert.DoesNotContain("eyJ0eXAiOiJKV1Qi", masked);
        Assert.Contains("\"access_token\":\"***\"", masked);
        Assert.Contains("\"refresh_token\":\"***\"", masked);
        Assert.Contains("\"id_token\":\"***\"", masked);
        // El resto de la respuesta sigue siendo útil para diagnosticar.
        Assert.Contains("\"token_type\":\"Bearer\"", masked);
        Assert.Contains("\"expires_in\":86399", masked);
    }

    [Fact]
    public void MaskJson_OcultaElTokenDeXboxLiveYElUserHash()
    {
        const string json = """
            {"IssueInstant":"2026-10-30T01:00:00Z","NotAfter":"2026-11-13T01:00:00Z","Token":"eyJhbGciOiJSUzI1NiJ9.XBL","DisplayClaims":{"xui":[{"uhs":"2535438130072611"}]}}
            """;

        string masked = SecretMasker.MaskJson(json);

        Assert.DoesNotContain("eyJhbGciOiJSUzI1NiJ9.XBL", masked);
        Assert.DoesNotContain("2535438130072611", masked);
        Assert.Contains("\"Token\":\"***\"", masked);
        // NotAfter no es un secreto: debe sobrevivir al enmascarado.
        Assert.Contains("2026-11-13T01:00:00Z", masked);
    }

    [Fact]
    public void MaskJson_OcultaTokensEnJsonIndentado()
    {
        // El formato real que se filtró al log tenía saltos de línea y espacios
        // alrededor de los dos puntos.
        const string json = """
            {
              "username" : "64c7a690-8bd1-5eb4-9c2b-ec46ce722cf1",
              "access_token" : "eyJraWQiOiIwNDkxODEi",
              "token_type" : "Bearer"
            }
            """;

        string masked = SecretMasker.MaskJson(json);

        Assert.DoesNotContain("eyJraWQiOiIwNDkxODEi", masked);
        Assert.Contains("\"access_token\" : \"***\"", masked);
        // El nombre de usuario no es una credencial y debe seguir visible.
        Assert.Contains("64c7a690-8bd1-5eb4-9c2b-ec46ce722cf1", masked);
    }

    [Fact]
    public void MaskJson_NoTocaCamposQueSoloContienenLaPalabraToken()
    {
        const string json = """{"TokenType":"JWT","token_type":"Bearer","expires_in":3600}""";

        Assert.Equal(json, SecretMasker.MaskJson(json));
    }

    [Fact]
    public void MaskAndTruncate_EnmascaraAntesDeRecortar()
    {
        string json = "{\"access_token\":\"" + new string('A', 40) + "\",\"token_type\":\"Bearer\"}";

        string result = SecretMasker.MaskAndTruncate(json, 20);

        Assert.DoesNotContain("AAAAAAAAAA", result);
        Assert.EndsWith("...", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void MaskJson_ToleraEntradasVacias(string? json)
    {
        Assert.Equal(string.Empty, SecretMasker.MaskJson(json));
    }
}
