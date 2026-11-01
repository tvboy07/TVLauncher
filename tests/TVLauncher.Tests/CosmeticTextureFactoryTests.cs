using System;
using System.IO;
using System.Security.Cryptography;
using TVLauncher.Services;
using Xunit;

namespace TVLauncher.Tests;

public sealed class CosmeticTextureFactoryTests
{
    [Fact]
    public void CapePng_Is64x32()
    {
        byte[] png = CosmeticTextureFactory.CreateCapePng();
        Assert.Equal(64u, ReadBigEndianUInt32(png, 16));
        Assert.Equal(32u, ReadBigEndianUInt32(png, 20));
    }

    [Fact]
    public void HatPng_Is64x64()
    {
        byte[] png = CosmeticTextureFactory.CreateHatPng();
        Assert.Equal(64u, ReadBigEndianUInt32(png, 16));
        Assert.Equal(64u, ReadBigEndianUInt32(png, 20));
    }

    [Fact]
    public void CommittedModAssets_MatchTheFactory()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "CosmeticAssets");
        Assert.Equal(
            Sha256(CosmeticTextureFactory.CreateCapePng()),
            Sha256(File.ReadAllBytes(Path.Combine(root, "cape_tvlauncher.png"))));
        Assert.Equal(
            Sha256(CosmeticTextureFactory.CreateHatPng()),
            Sha256(File.ReadAllBytes(Path.Combine(root, "hat_tvlauncher.png"))));
    }

    private static uint ReadBigEndianUInt32(byte[] bytes, int offset)
        => ((uint)bytes[offset] << 24)
         | ((uint)bytes[offset + 1] << 16)
         | ((uint)bytes[offset + 2] << 8)
         | bytes[offset + 3];

    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));
}
