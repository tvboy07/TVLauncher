using System;
using System.Text.RegularExpressions;

namespace TVLauncher.Helpers;

/// <summary>
/// Enmascara credenciales antes de escribirlas en un log. Las respuestas de los
/// endpoints de autenticación (Microsoft, Xbox Live, XSTS, Minecraft Services)
/// contienen tokens de sesión que no deben acabar en <c>tvlauncher.log</c>: el
/// archivo se comparte a menudo en informes de error.
/// </summary>
public static class SecretMasker
{
    /// <summary>
    /// Campos JSON cuyo valor es una credencial. Se sustituye solo el valor para
    /// que la respuesta siga siendo útil al diagnosticar (tipo de token, caducidad,
    /// errores, etc.).
    /// </summary>
    private static readonly Regex SensitiveJsonFields = new(
        @"(?<prefix>""(?:access_token|refresh_token|id_token|authorizationtoken|user_hash|uhs|token)""\s*:\s*"")[^""]*(?<suffix>"")",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex BearerToken = new(
        @"(?<prefix>\bBearer\s+)[A-Za-z0-9\-._~+/]{8,}=*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex Jwt = new(
        @"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex AccessTokenArgument = new(
        @"(?<prefix>--accessToken(?:=|\s+))[^\s""]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex QueryToken = new(
        @"(?<prefix>(?:access_token|refresh_token|id_token|authorizationtoken|uhs|user_hash)=)[^&\s""]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>Devuelve el JSON con los valores sensibles sustituidos por <c>***</c>.</summary>
    public static string MaskJson(string? json)
        => string.IsNullOrEmpty(json)
            ? string.Empty
            : SensitiveJsonFields.Replace(json, "${prefix}***${suffix}");

    /// <summary>
    /// Enmascara credenciales en texto libre (logs, mensajes de error, líneas de
    /// comando). Es defensa en profundidad: no dependemos de que cada llamada
    /// recuerde usar <see cref="MaskJson"/>.
    /// </summary>
    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        string masked = MaskJson(text);
        masked = BearerToken.Replace(masked, "${prefix}***");
        masked = Jwt.Replace(masked, "***");
        masked = AccessTokenArgument.Replace(masked, "${prefix}***");
        masked = QueryToken.Replace(masked, "${prefix}***");
        return masked;
    }

    /// <summary>Recorta un texto largo para que el log no crezca sin control.</summary>
    public static string Truncate(string? value, int maxLength = 300)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length > maxLength ? value[..maxLength] + "..." : value;
    }

    /// <summary>
    /// Enmascara y después recorta: recortar primero podría dejar un fragmento
    /// del token visible al final del texto.
    /// </summary>
    public static string MaskAndTruncate(string? json, int maxLength = 300)
        => Truncate(Scrub(json), maxLength);
}
