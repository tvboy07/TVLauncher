using System;
using System.IO;
using SkiaSharp;
using TVLauncher.Services;
using Xunit;

namespace TVLauncher.Tests;

public sealed class SkinImageNormalizerTests
{
    [Fact]
    public void Normalize_KeepsAnAlreadyModern64x64Skin()
    {
        byte[] png = CreatePng(64, 64, new SKColor(0x12, 0x34, 0x56, 0xFF));

        byte[] normalized = SkinImageNormalizer.Normalize(png);

        Assert.Equal(png, normalized);
    }

    [Fact]
    public void Normalize_PadsALegacy64x32SkinTo64x64()
    {
        byte[] png = CreatePng(64, 32, new SKColor(0xAB, 0xCD, 0xEF, 0xFF));

        byte[] normalized = SkinImageNormalizer.Normalize(png);

        using var bitmap = SKBitmap.Decode(normalized);
        Assert.NotNull(bitmap);
        Assert.Equal(64, bitmap!.Width);
        Assert.Equal(64, bitmap.Height);
        Assert.Equal(new SKColor(0xAB, 0xCD, 0xEF, 0xFF), bitmap.GetPixel(0, 0));
        Assert.Equal(0, bitmap.GetPixel(0, 63).Alpha);
    }

    [Fact]
    public void Normalize_RejectsUnsupportedDimensions()
    {
        byte[] png = CreatePng(128, 128, SKColors.Red);

        Assert.Throws<InvalidDataException>(() => SkinImageNormalizer.Normalize(png));
    }

    internal static byte[] CreatePng(int width, int height, SKColor color)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
