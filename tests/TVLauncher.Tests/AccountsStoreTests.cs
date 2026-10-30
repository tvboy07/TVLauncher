using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TVLauncher.Models;
using TVLauncher.Services;
using Xunit;

namespace TVLauncher.Tests;

/// <summary>
/// Persistencia cifrada de <c>accounts.json</c>: escritura del sobre, creación de
/// la clave local, migración transparente desde JSON plano e idempotencia.
/// </summary>
public sealed class AccountsStoreTests
{
    private const string Token = "TOKEN_SUPER_SECRETO_ABC123";

    private static MinecraftAccount Sample(string username = "Notch")
        => new()
        {
            Username = username,
            Uuid = "069a79f4-44e9-4726-a5be-fca90e38aaf5",
            AccessToken = Token,
            RefreshToken = "REFRESH_SECRETO",
            Type = AccountType.Microsoft,
            IsActive = true
        };

    private static string EnvelopeAlg(string path)
        => (string)JObject.Parse(File.ReadAllText(path))["alg"]!;

    [Fact]
    public void Save_Then_Load_RoundTripsTheAccounts()
    {
        using var dir = new TempDirectory();
        var store = new AccountsStore(dir.Root);
        var account = Sample();

        store.Save(new[] { account });
        var loaded = new AccountsStore(dir.Root).Load();

        var only = Assert.Single(loaded);
        Assert.Equal(account.Username, only.Username);
        Assert.Equal(account.Uuid, only.Uuid);
        Assert.Equal(Token, only.AccessToken);
        Assert.Equal(account.RefreshToken, only.RefreshToken);
        Assert.Equal(AccountType.Microsoft, only.Type);
        Assert.True(only.IsActive);
    }

    [Fact]
    public void Save_WritesAnEncryptedEnvelopeAndTheKeyFile()
    {
        using var dir = new TempDirectory();
        var store = new AccountsStore(dir.Root);

        store.Save(new[] { Sample() });

        string raw = File.ReadAllText(store.AccountsPath);
        Assert.Equal("AES-256-GCM", EnvelopeAlg(store.AccountsPath));
        Assert.DoesNotContain(Token, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Notch", raw, StringComparison.Ordinal);
        Assert.True(File.Exists(store.KeyPath));
        Assert.Equal(32, new FileInfo(store.KeyPath).Length);
    }

    [Fact]
    public void Load_MigratesLegacyPlainJsonAndRewritesItEncrypted()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("accounts.json", JsonConvert.SerializeObject(new[] { Sample() }, Formatting.Indented));

        var store = new AccountsStore(dir.Root);
        var loaded = store.Load();

        var only = Assert.Single(loaded);
        Assert.Equal("Notch", only.Username);
        Assert.Equal(Token, only.AccessToken);

        Assert.Equal("AES-256-GCM", EnvelopeAlg(store.AccountsPath));
        Assert.DoesNotContain(Token, File.ReadAllText(store.AccountsPath), StringComparison.Ordinal);
        Assert.True(File.Exists(store.KeyPath));
    }

    [Fact]
    public void Load_OnAnAlreadyEncryptedFile_IsIdempotentAndDoesNotRewrite()
    {
        using var dir = new TempDirectory();
        var store = new AccountsStore(dir.Root);
        store.Save(new[] { Sample() });
        byte[] before = File.ReadAllBytes(store.AccountsPath);

        var loaded = new AccountsStore(dir.Root).Load();

        Assert.Equal("Notch", Assert.Single(loaded).Username);
        Assert.Equal(before, File.ReadAllBytes(store.AccountsPath));
    }

    [Fact]
    public void Save_UsesAFreshNonceSoCiphertextAlwaysChanges()
    {
        using var dir = new TempDirectory();
        var store = new AccountsStore(dir.Root);

        store.Save(new[] { Sample() });
        string first = File.ReadAllText(store.AccountsPath);
        store.Save(new[] { Sample() });
        string second = File.ReadAllText(store.AccountsPath);

        Assert.Equal(EnvelopeAlg(store.AccountsPath), EnvelopeAlg(store.AccountsPath));
        Assert.NotEqual(
            (string)JObject.Parse(first)["nonce"]!,
            (string)JObject.Parse(second)["nonce"]!);
        Assert.Equal("Notch", Assert.Single(new AccountsStore(dir.Root).Load()).Username);
    }

    [Fact]
    public void Load_WithoutTheKeyFile_ThrowsAndLeavesTheDataUntouched()
    {
        using var dir = new TempDirectory();
        var store = new AccountsStore(dir.Root);
        store.Save(new[] { Sample() });
        File.Delete(store.KeyPath);
        byte[] before = File.ReadAllBytes(store.AccountsPath);

        var error = Assert.Throws<InvalidDataException>(() => new AccountsStore(dir.Root).Load());

        Assert.Contains("accounts.key", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(store.AccountsPath));
        Assert.False(File.Exists(store.KeyPath), "no debe generarse una clave nueva: perdería las cuentas");
    }

    [Fact]
    public void Load_WithACorruptKeyFile_Throws()
    {
        using var dir = new TempDirectory();
        var store = new AccountsStore(dir.Root);
        store.Save(new[] { Sample() });
        File.WriteAllBytes(store.KeyPath, new byte[8]);

        var error = Assert.Throws<InvalidDataException>(() => new AccountsStore(dir.Root).Load());

        Assert.Contains("32", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WithoutFile_ReturnsAnEmptyList()
    {
        using var dir = new TempDirectory();

        var loaded = new AccountsStore(dir.Root).Load();

        Assert.Empty(loaded);
        Assert.False(File.Exists(new AccountsStore(dir.Root).KeyPath), "no debe crear clave si no hay nada que cifrar");
    }

    [Fact]
    public void Load_WithACorruptLegacyFile_ThrowsInsteadOfOverwriting()
    {
        using var dir = new TempDirectory();
        string path = dir.WriteFile("accounts.json", "[{\"Username\":\"Notch\",");

        Assert.Throws<InvalidDataException>(() => new AccountsStore(dir.Root).Load());

        Assert.StartsWith("[", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFilesBehind()
    {
        using var dir = new TempDirectory();
        var store = new AccountsStore(dir.Root);

        store.Save(new[] { Sample() });

        var leftovers = Directory.GetFiles(dir.Root)
            .Where(f => f.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Empty(leftovers);
    }

    [Fact]
    public void KeyFile_IsOwnerOnlyOnUnix()
    {
        if (OperatingSystem.IsWindows())
            return; // En Windows la ACL del perfil ya es exclusiva del usuario.

        using var dir = new TempDirectory();
        var store = new AccountsStore(dir.Root);

        store.Save(new[] { Sample() });

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(store.KeyPath));
    }

    [Fact]
    public void Save_WithAnEmptyList_ProducesAValidEncryptedFile()
    {
        using var dir = new TempDirectory();
        var store = new AccountsStore(dir.Root);

        store.Save(Array.Empty<MinecraftAccount>());

        Assert.Empty(new AccountsStore(dir.Root).Load());
        Assert.Equal("AES-256-GCM", EnvelopeAlg(store.AccountsPath));
    }
}
