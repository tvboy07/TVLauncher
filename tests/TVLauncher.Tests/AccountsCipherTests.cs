using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TVLauncher.Services;
using Xunit;

namespace TVLauncher.Tests;

/// <summary>
/// Sobre AES-GCM de <c>accounts.json</c>: ciclo de cifrado/descifrado, nonce
/// fresco en cada sellado y rechazo de sobres manipulados o con clave errónea.
/// </summary>
public sealed class AccountsCipherTests
{
    private const string PlainJson =
        "[{\"Username\":\"Notch\",\"AccessToken\":\"TOKEN_SECRETO_ABC123\",\"RefreshToken\":\"REFRESH_SECRETO_XYZ789\"}]";

    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(AccountsCipher.KeySizeBytes);

    [Fact]
    public void Seal_Then_Open_RoundTripsTheExactText()
    {
        byte[] key = NewKey();

        string envelope = AccountsCipher.Seal(PlainJson, key);

        Assert.Equal(PlainJson, AccountsCipher.Open(envelope, key));
    }

    [Fact]
    public void Seal_ProducesSelfDescribingEnvelopeWithCorrectFieldSizes()
    {
        string envelope = AccountsCipher.Seal(PlainJson, NewKey());
        var json = JObject.Parse(envelope);

        Assert.Equal(AccountsCipher.CurrentVersion, (int)json["version"]!);
        Assert.Equal("AES-256-GCM", (string)json["alg"]!);
        Assert.Equal(12, Convert.FromBase64String((string)json["nonce"]!).Length);
        Assert.Equal(16, Convert.FromBase64String((string)json["tag"]!).Length);
        Assert.NotEmpty(Convert.FromBase64String((string)json["data"]!));
    }

    [Fact]
    public void Seal_NeverLeaksThePlaintext()
    {
        string envelope = AccountsCipher.Seal(PlainJson, NewKey());

        Assert.DoesNotContain("TOKEN_SECRETO_ABC123", envelope, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshToken", envelope, StringComparison.Ordinal);
    }

    [Fact]
    public void Seal_UsesAFreshNonceOnEveryCall()
    {
        byte[] key = NewKey();

        var first = JObject.Parse(AccountsCipher.Seal(PlainJson, key));
        var second = JObject.Parse(AccountsCipher.Seal(PlainJson, key));

        Assert.NotEqual((string)first["nonce"]!, (string)second["nonce"]!);
        Assert.NotEqual((string)first["data"]!, (string)second["data"]!);
    }

    [Fact]
    public void Open_WithADifferentKey_Throws()
    {
        string envelope = AccountsCipher.Seal(PlainJson, NewKey());

        Assert.ThrowsAny<CryptographicException>(() => AccountsCipher.Open(envelope, NewKey()));
    }

    [Fact]
    public void Open_AfterCiphertextTampered_Throws()
    {
        byte[] key = NewKey();
        var json = JObject.Parse(AccountsCipher.Seal(PlainJson, key));
        byte[] data = Convert.FromBase64String((string)json["data"]!);
        data[0] ^= 0xFF;
        json["data"] = Convert.ToBase64String(data);

        Assert.ThrowsAny<CryptographicException>(
            () => AccountsCipher.Open(json.ToString(Formatting.None), key));
    }

    [Fact]
    public void Open_AfterTagTampered_Throws()
    {
        byte[] key = NewKey();
        var json = JObject.Parse(AccountsCipher.Seal(PlainJson, key));
        byte[] tag = Convert.FromBase64String((string)json["tag"]!);
        tag[0] ^= 0xFF;
        json["tag"] = Convert.ToBase64String(tag);

        Assert.ThrowsAny<CryptographicException>(
            () => AccountsCipher.Open(json.ToString(Formatting.None), key));
    }

    [Fact]
    public void Open_RejectsAnUnsupportedVersion()
    {
        byte[] key = NewKey();
        var json = JObject.Parse(AccountsCipher.Seal(PlainJson, key));
        json["version"] = AccountsCipher.CurrentVersion + 1;

        Assert.Throws<InvalidDataException>(
            () => AccountsCipher.Open(json.ToString(Formatting.None), key));
    }

    [Fact]
    public void Open_RejectsAnUnknownAlgorithm()
    {
        byte[] key = NewKey();
        var json = JObject.Parse(AccountsCipher.Seal(PlainJson, key));
        json["alg"] = "ROT13";

        Assert.Throws<InvalidDataException>(
            () => AccountsCipher.Open(json.ToString(Formatting.None), key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no soy json")]
    [InlineData("[]")]
    [InlineData("{\"version\":1}")]
    public void Open_RejectsMalformedEnvelopes(string envelope)
    {
        Assert.Throws<InvalidDataException>(() => AccountsCipher.Open(envelope, NewKey()));
    }

    [Fact]
    public void Seal_RejectsAKeyOfTheWrongSize()
    {
        Assert.Throws<ArgumentException>(() => AccountsCipher.Seal(PlainJson, new byte[16]));
    }
}
