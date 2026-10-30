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

    /// <summary>Devuelve el JSON con los valores sensibles sustituidos por <c>***</c>.</summary>
    public static string MaskJson(string? json)
        => string.IsNullOrEmpty(json)
            ? string.Empty
            : SensitiveJsonFields.Replace(json, "${prefix}***${suffix}");

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
        => Truncate(MaskJson(json), maxLength);
}
