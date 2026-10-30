using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TVLauncher.Services;

/// <summary>
/// Sobre AES-256-GCM autocontenido para <c>accounts.json</c>. Usa
/// <see cref="AesGcm"/> nativo de .NET, disponible en Windows, Linux y macOS
/// (a diferencia de DPAPI, que solo existe en Windows).
/// </summary>
/// <remarks>
/// Formato en disco:
/// <code>
/// { "version": 1, "alg": "AES-256-GCM", "nonce": "…", "tag": "…", "data": "…" }
/// </code>
/// El nonce es aleatorio (12 bytes) en cada sellado y nunca se reutiliza; el tag
/// de autenticación (16 bytes) detecta cualquier manipulación del cifrado. La
/// cabecera del sobre se autentica como datos asociados, así que cambiar la
/// versión o el algoritmo también invalida el tag.
/// </remarks>
internal static class AccountsCipher
{
    internal const int CurrentVersion = 1;
    internal const string AlgorithmName = "AES-256-GCM";
    internal const int KeySizeBytes = 32;
    internal const int NonceSizeBytes = 12;
    internal const int TagSizeBytes = 16;

    /// <summary>Datos asociados autenticados (dominio de uso del cifrado).</summary>
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("TVLauncher:accounts:v1");

    /// <summary>Cifra <paramref name="plaintext"/> y devuelve el sobre JSON listo para disco.</summary>
    internal static string Seal(string plaintext, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ValidateKey(key);

        byte[] plain = Encoding.UTF8.GetBytes(plaintext);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        byte[] ciphertext = new byte[plain.Length];
        byte[] tag = new byte[TagSizeBytes];

        using (var aes = new AesGcm(key, TagSizeBytes))
        {
            aes.Encrypt(nonce, plain, ciphertext, tag, AssociatedData);
        }

        var envelope = new JObject
        {
            ["version"] = CurrentVersion,
            ["alg"] = AlgorithmName,
            ["nonce"] = Convert.ToBase64String(nonce),
            ["tag"] = Convert.ToBase64String(tag),
            ["data"] = Convert.ToBase64String(ciphertext)
        };

        return envelope.ToString(Formatting.None);
    }

    /// <summary>
    /// Descifra un sobre producido por <see cref="Seal"/>. Lanza
    /// <see cref="CryptographicException"/> si la clave es incorrecta o el
    /// contenido fue manipulado, e <see cref="InvalidDataException"/> si el
    /// sobre no tiene el formato esperado.
    /// </summary>
    internal static string Open(string envelopeJson, byte[] key)
    {
        ValidateKey(key);

        if (string.IsNullOrWhiteSpace(envelopeJson))
            throw new InvalidDataException("El sobre cifrado está vacío.");

        JObject envelope;
        try
        {
            envelope = JObject.Parse(envelopeJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("accounts.json no es un sobre JSON válido.", ex);
        }

        int version = ReadVersion(envelope);
        if (version != CurrentVersion)
            throw new InvalidDataException(
                $"Versión de cifrado no soportada: {version} (se esperaba {CurrentVersion}).");

        string algorithm = (string?)envelope["alg"]
            ?? throw new InvalidDataException("El sobre cifrado no declara 'alg'.");
        if (!string.Equals(algorithm, AlgorithmName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Algoritmo de cifrado no soportado: '{algorithm}'.");

        byte[] nonce = ReadBytes(envelope, "nonce", NonceSizeBytes);
        byte[] tag = ReadBytes(envelope, "tag", TagSizeBytes);
        byte[] ciphertext = ReadBytes(envelope, "data", expectedSize: null);

        byte[] plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(key, TagSizeBytes))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    private static void ValidateKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != KeySizeBytes)
            throw new ArgumentException(
                $"La clave de cifrado debe tener {KeySizeBytes} bytes (tiene {key.Length}).", nameof(key));
    }

    private static int ReadVersion(JObject envelope)
    {
        JToken? token = envelope["version"];
        if (token == null || token.Type != JTokenType.Integer)
            throw new InvalidDataException("El sobre cifrado no declara una 'version' numérica.");

        return token.Value<int>();
    }

    private static byte[] ReadBytes(JObject envelope, string name, int? expectedSize)
    {
        JToken? token = envelope[name];
        if (token == null || token.Type != JTokenType.String)
            throw new InvalidDataException($"El sobre cifrado no incluye '{name}'.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String((string)token!);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException($"'{name}' no es Base64 válido.", ex);
        }

        if (expectedSize.HasValue && bytes.Length != expectedSize.Value)
            throw new InvalidDataException(
                $"'{name}' debe tener {expectedSize.Value} bytes (tiene {bytes.Length}).");

        return bytes;
    }
}
