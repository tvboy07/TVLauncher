using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using TVLauncher.Services;
using Xunit;

namespace TVLauncher.Tests;

/// <summary>
/// Construcción de las peticiones OAuth2 contra Microsoft para Minecraft:
/// scopes obligatorios, PKCE S256 en aplicación pública (sin client_secret) y
/// mensajes de error accionables cuando Mojang rechaza la app de Azure.
/// </summary>
public sealed class MicrosoftAuthRequestTests
{
    private const string ClientId = "38c4f9ed-0484-4a22-a192-9be8b172ab16";
    private const string RedirectUri = "http://localhost:1337/";

    [Fact]
    public void DefaultScopes_IncludeMinecraftAndOidcScopesExactlyOnce()
    {
        var scopes = MicrosoftAuthService.DefaultScopes;

        Assert.Equal(new[] { "openid", "profile", "offline_access", "XboxLive.signin" }, scopes);
        Assert.Equal(scopes.Count, scopes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Scope_IsASingleSpaceSeparatedString()
    {
        Assert.Equal("openid profile offline_access XboxLive.signin", MicrosoftAuthService.Scope);
        Assert.DoesNotContain(",", MicrosoftAuthService.Scope);
    }

    [Fact]
    public void BuildAuthorizeUrl_RequestsEveryScopeUrlEncoded()
    {
        string url = MicrosoftAuthService.BuildAuthorizeUrl(
            ClientId, RedirectUri, "state-123", "challenge-456");

        Assert.StartsWith("https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize?", url);
        Assert.Contains("scope=openid%20profile%20offline_access%20XboxLive.signin", url);

        var query = ParseQuery(url);
        Assert.Equal("openid profile offline_access XboxLive.signin", query["scope"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("query", query["response_mode"]);
        Assert.Equal(ClientId, query["client_id"]);
        Assert.Equal(RedirectUri, query["redirect_uri"]);
        Assert.Equal("state-123", query["state"]);
    }

    [Fact]
    public void BuildAuthorizeUrl_UsesPkceS256AndNoSecret()
    {
        string url = MicrosoftAuthService.BuildAuthorizeUrl(
            ClientId, RedirectUri, "state-123", "challenge-456");

        var query = ParseQuery(url);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("challenge-456", query["code_challenge"]);
        Assert.DoesNotContain("client_secret", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateCodeChallenge_IsBase64UrlSha256OfTheVerifier()
    {
        // Valor fijo calculado con SHA-256("verifier-de-prueba") en Base64URL.
        string challenge = MicrosoftAuthService.CreateCodeChallenge("verifier-de-prueba");

        Assert.Equal("7fnyUe1P9H9SulQT0Brt8KkYSqB7tz8iet-YODhLX5A", challenge);
        Assert.DoesNotContain("=", challenge);
        Assert.DoesNotContain("+", challenge);
        Assert.DoesNotContain("/", challenge);
    }

    [Fact]
    public void BuildAuthorizationCodeTokenForm_SendsVerifierAndNoSecret()
    {
        var form = MicrosoftAuthService.BuildAuthorizationCodeTokenForm(
            ClientId, "the-code", RedirectUri, "the-verifier");

        Assert.Equal(ClientId, form["client_id"]);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("the-code", form["code"]);
        Assert.Equal(RedirectUri, form["redirect_uri"]);
        Assert.Equal("the-verifier", form["code_verifier"]);
        Assert.Equal("openid profile offline_access XboxLive.signin", form["scope"]);
        Assert.DoesNotContain("client_secret", form.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDeviceCodeForm_RequestsTheSameScopesAndNoSecret()
    {
        var form = MicrosoftAuthService.BuildDeviceCodeForm(ClientId);

        Assert.Equal(ClientId, form["client_id"]);
        Assert.Equal("openid profile offline_access XboxLive.signin", form["scope"]);
        Assert.DoesNotContain("client_secret", form.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDeviceCodeTokenForm_QueriesTheGrantWithTheSameClient()
    {
        var form = MicrosoftAuthService.BuildDeviceCodeTokenForm(ClientId, "device-code");

        Assert.Equal("urn:ietf:params:oauth:grant-type:device_code", form["grant_type"]);
        Assert.Equal(ClientId, form["client_id"]);
        Assert.Equal("device-code", form["device_code"]);
        Assert.DoesNotContain("client_secret", form.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void MinecraftAuthError_InvalidAppRegistration_ExplainsTheApprovalForm()
    {
        const string body = """
            {
              "path" : "/authentication/login_with_xbox",
              "errorMessage" : "Invalid app registration, see https://aka.ms/AppRegInfo for more information"
            }
            """;

        string message = MicrosoftAuthService.DescribeMinecraftAuthError(403, body, ClientId);

        Assert.Contains("403", message, StringComparison.Ordinal);
        Assert.Contains("38c4f9ed", message, StringComparison.Ordinal); // incluye el client id en uso
        Assert.Contains("aka.ms/mce-reviewappid", message, StringComparison.Ordinal);
    }

    [Fact]
    public void MinecraftAuthError_KeepsTheRawBodyForOtherFailures()
    {
        string message = MicrosoftAuthService.DescribeMinecraftAuthError(500, "boom", ClientId);

        Assert.Contains("500", message, StringComparison.Ordinal);
        Assert.Contains("boom", message, StringComparison.Ordinal);
    }

    // ---------- utilidades ----------

    private static Dictionary<string, string> ParseQuery(string url)
    {
        int index = url.IndexOf('?');
        var query = HttpUtility.ParseQueryString(url[(index + 1)..]);
        return query.AllKeys
            .Where(key => key is not null)
            .ToDictionary(key => key!, key => query[key] ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }
}
