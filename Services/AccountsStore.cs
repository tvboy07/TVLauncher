using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TVLauncher.Models;

namespace TVLauncher.Services;

/// <summary>
/// Persistencia de las cuentas en el directorio de datos (<c>accounts.json</c>),
/// cifrada con AES-256-GCM y una clave local propia (<c>accounts.key</c>).
/// </summary>
/// <remarks>
/// <para>
/// La clave es un bloque aleatorio de 32 bytes que se crea la primera vez y se
/// guarda en el mismo directorio de configuración. En Unix se le aplica
/// <c>chmod 600</c> (<see cref="File.SetUnixFileMode"/>); en Windows hereda la ACL
/// del perfil, que ya es exclusiva del usuario.
/// </para>
/// <para>
/// Si <c>accounts.json</c> está en el formato antiguo (un array JSON plano), se
/// lee, se reescribe cifrado y se devuelve sin intervención del usuario.
/// </para>
/// </remarks>
public sealed class AccountsStore
{
    public const string KeyFileName = "accounts.key";

    private const string AccountsFileName = "accounts.json";
    private const string DpapiProtection = "dpapi";
    private const string PlainProtection = "plain";
    private static readonly byte[] KeyEntropy =
        Encoding.UTF8.GetBytes("TVLauncher:accounts-key:v1");

    private readonly string _directory;

    public AccountsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _directory = directory;
        AccountsPath = Path.Combine(directory, AccountsFileName);
        KeyPath = Path.Combine(directory, KeyFileName);
    }

    /// <summary>Ruta del archivo de cuentas (cifrado).</summary>
    public string AccountsPath { get; }

    /// <summary>Ruta de la clave local.</summary>
    public string KeyPath { get; }

    /// <summary>
    /// Carga las cuentas, migrando de forma transparente el formato plano antiguo.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// El archivo no es legible: sobre corrupto, clave ausente o de tamaño
    /// incorrecto. En ese caso <b>no</b> se toca el archivo en disco.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// El archivo fue manipulado o la clave no corresponde.
    /// </exception>
    public List<MinecraftAccount> Load()
    {
        if (!File.Exists(AccountsPath))
            return new List<MinecraftAccount>();

        string raw = File.ReadAllText(AccountsPath);
        if (string.IsNullOrWhiteSpace(raw))
            return new List<MinecraftAccount>();

        if (IsLegacyPlainJson(raw))
        {
            var legacy = Deserialize(raw);

            // Migración transparente: mismos datos, ahora cifrados.
            Save(legacy);
            Log.Info($"[Accounts] accounts.json migrado de JSON plano a {AccountsCipher.AlgorithmName}");

            return legacy;
        }

        var accounts = Deserialize(AccountsCipher.Open(raw, LoadKey()));
        Log.Info($"[Accounts] {accounts.Count} cuenta(s) descifrada(s) desde {AccountsPath}");
        return accounts;
    }

    /// <summary>Guarda las cuentas cifradas, de forma atómica.</summary>
    public void Save(IReadOnlyList<MinecraftAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        string json = JsonConvert.SerializeObject(accounts, Formatting.Indented);
        byte[] envelope = Encoding.UTF8.GetBytes(AccountsCipher.Seal(json, LoadOrCreateKey()));

        Directory.CreateDirectory(_directory);
        WriteAtomic(AccountsPath, envelope, ownerOnly: false);
    }

    /// <summary>El formato antiguo era un array JSON; el nuevo es un objeto (sobre).</summary>
    private static bool IsLegacyPlainJson(string raw)
        => raw.TrimStart().StartsWith('[');

    private static List<MinecraftAccount> Deserialize(string json)
    {
        try
        {
            return JsonConvert.DeserializeObject<List<MinecraftAccount>>(json)
                   ?? new List<MinecraftAccount>();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "El contenido de accounts.json no tiene el formato esperado.", ex);
        }
    }

    private byte[] LoadKey()
    {
        if (!File.Exists(KeyPath))
            throw new InvalidDataException(
                $"No se encontró la clave de cifrado '{KeyPath}'. Sin ella no se pueden descifrar " +
                "las cuentas guardadas; restaura el archivo para no perderlas.");

        byte[] stored = File.ReadAllBytes(KeyPath);

        // Formato histórico: 32 bytes planos. Se migra a DPAPI en Windows.
        if (stored.Length == AccountsCipher.KeySizeBytes)
        {
            if (OperatingSystem.IsWindows())
                TryProtectExistingKey(stored);
            return stored;
        }

        JObject envelope;
        try
        {
            envelope = JObject.Parse(Encoding.UTF8.GetString(stored));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"La clave '{KeyPath}' no es válida: se esperaban {AccountsCipher.KeySizeBytes} bytes " +
                "o un sobre JSON de clave.", ex);
        }

        int version = envelope.Value<int?>("version") ?? 0;
        if (version != 1)
            throw new InvalidDataException($"Versión de clave no soportada: {version}.");

        string protection = envelope.Value<string>("protection") ?? string.Empty;
        byte[] data = ReadKeyData(envelope);

        if (string.Equals(protection, DpapiProtection, StringComparison.OrdinalIgnoreCase))
        {
            if (!OperatingSystem.IsWindows())
                throw new InvalidDataException(
                    "La clave fue protegida con DPAPI en Windows y no se puede descifrar en este sistema.");

            try
            {
                byte[] key = ProtectedData.Unprotect(
                    data, KeyEntropy, DataProtectionScope.CurrentUser);
                if (key.Length != AccountsCipher.KeySizeBytes)
                    throw new InvalidDataException(
                        $"La clave descifrada debe tener {AccountsCipher.KeySizeBytes} bytes.");
                return key;
            }
            catch (CryptographicException ex)
            {
                throw new InvalidDataException(
                    "La clave de cifrado no pertenece a este usuario/equipo (DPAPI). " +
                    "Restaura el accounts.key original.", ex);
            }
        }

        if (string.Equals(protection, PlainProtection, StringComparison.OrdinalIgnoreCase))
        {
            if (data.Length != AccountsCipher.KeySizeBytes)
                throw new InvalidDataException(
                    $"La clave debe tener {AccountsCipher.KeySizeBytes} bytes (tiene {data.Length}).");

            if (OperatingSystem.IsWindows())
                TryProtectExistingKey(data);
            return data;
        }

        throw new InvalidDataException($"Protección de clave desconocida: '{protection}'.");
    }

    private byte[] LoadOrCreateKey()
    {
        if (File.Exists(KeyPath))
            return LoadKey();

        Directory.CreateDirectory(_directory);

        byte[] key = RandomNumberGenerator.GetBytes(AccountsCipher.KeySizeBytes);

        try
        {
            // overwrite:false -> si otra instancia ganó la carrera, no se pisa su clave.
            WriteKey(key, overwrite: false);
        }
        catch (IOException)
        {
            return LoadKey();
        }

        Log.Info($"[Accounts] Clave de cifrado creada en {KeyPath}");
        return key;
    }

    private void WriteKey(byte[] key, bool overwrite)
    {
        JObject envelope;
        if (OperatingSystem.IsWindows())
        {
            byte[] protectedData = ProtectedData.Protect(
                key, KeyEntropy, DataProtectionScope.CurrentUser);
            envelope = new JObject
            {
                ["version"] = 1,
                ["protection"] = DpapiProtection,
                ["data"] = Convert.ToBase64String(protectedData)
            };
        }
        else
        {
            envelope = new JObject
            {
                ["version"] = 1,
                ["protection"] = PlainProtection,
                ["data"] = Convert.ToBase64String(key)
            };
        }

        WriteAtomic(
            KeyPath,
            Encoding.UTF8.GetBytes(envelope.ToString(Formatting.None)),
            ownerOnly: true,
            overwrite: overwrite);
    }

    private void TryProtectExistingKey(byte[] key)
    {
        try
        {
            WriteKey(key, overwrite: true);
            Log.Info("[Accounts] Clave de cuentas migrada a DPAPI (usuario actual)");
        }
        catch (Exception ex)
        {
            // Si falla la migración, la clave plana sigue siendo válida.
            Log.Warn($"[Accounts] No se pudo proteger la clave con DPAPI: {ex.Message}");
        }
    }

    private static byte[] ReadKeyData(JObject envelope)
    {
        string? encoded = envelope.Value<string>("data");
        if (string.IsNullOrWhiteSpace(encoded))
            throw new InvalidDataException("La clave no incluye el campo 'data'.");

        byte[] data;
        try
        {
            data = Convert.FromBase64String(encoded);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("El campo 'data' de la clave no es Base64 válido.", ex);
        }

        return data;
    }

    /// <summary>
    /// Escribe el archivo completo en un temporal y lo renombra: nunca queda un
    /// <c>accounts.json</c> a medias si el proceso muere a mitad de escritura.
    /// </summary>
    private static void WriteAtomic(string path, byte[] bytes, bool ownerOnly, bool overwrite = true)
    {
        string temp = path + ".part";

        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            if (ownerOnly && !OperatingSystem.IsWindows())
            {
                // chmod 600: solo el propietario puede leer o escribir la clave.
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temp, path, overwrite);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Limpieza best-effort de un temporal.
        }
    }
}
