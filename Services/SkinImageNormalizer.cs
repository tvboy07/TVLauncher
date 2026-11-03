using System;
using System.IO;
using SkiaSharp;

namespace TVLauncher.Services;

/// <summary>
/// Prepara una skin para el formato moderno de 64x64 que esperan tanto el
/// endpoint de Mojang como <c>PlayerCapeModel</c>/<c>PlayerEntityModel</c>.
/// </summary>
public static class SkinImageNormalizer
{
    /// <summary>
    /// Devuelve la skin tal cual si ya es 64x64. Las skins legacy de 64x32 se
    /// copian sobre un lienzo transparente de 64x64; el resto de tamaños se
    /// rechaza con un mensaje accionable.
    /// </summary>
    public static byte[] Normalize(byte[] png)
    {
        if (png is null || png.Length == 0)
            throw new InvalidDataException("La skin está vacía.");

        using var source = SKBitmap.Decode(png)
            ?? throw new InvalidDataException("El archivo no es una imagen PNG válida.");

        if (source.Width == 64 && source.Height == 64)
            return png;

        if (source.Width != 64 || source.Height != 32)
            throw new InvalidDataException(
                $"La skin debe medir 64x64 o 64x32; esta mide {source.Width}x{source.Height}.");

        var info = new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var target = new SKBitmap(info);
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, 0, 0);
        }

        // Mismo remapeo que PlayerSkinTextureDownloader.remapTexture: las
        // skins viejas de 64x32 guardan brazos/piernas en otra zona; sin esto
        // se ven sin extremidades al subirlas o al renderizarlas.
        CopyRect(target, 4, 16, 16, 32, 4, 4, true, false);
        CopyRect(target, 8, 16, 16, 32, 4, 4, true, false);
        CopyRect(target, 0, 20, 24, 32, 4, 12, true, false);
        CopyRect(target, 4, 20, 16, 32, 4, 12, true, false);
        CopyRect(target, 8, 20, 8, 32, 4, 12, true, false);
        CopyRect(target, 12, 20, 16, 32, 4, 12, true, false);
        CopyRect(target, 44, 16, -8, 32, 4, 4, true, false);
        CopyRect(target, 48, 16, -8, 32, 4, 4, true, false);
        CopyRect(target, 40, 20, 0, 32, 4, 12, true, false);
        CopyRect(target, 44, 20, -8, 32, 4, 12, true, false);
        CopyRect(target, 48, 20, -16, 32, 4, 12, true, false);
        CopyRect(target, 52, 20, -8, 32, 4, 12, true, false);

        using var image = SKImage.FromBitmap(target);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void CopyRect(
        SKBitmap target,
        int sourceX,
        int sourceY,
        int offsetX,
        int offsetY,
        int width,
        int height,
        bool flipX,
        bool flipY)
    {
        int destinationX = sourceX + offsetX;
        int destinationY = sourceY + offsetY;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int readX = sourceX + (flipX ? width - 1 - x : x);
                int readY = sourceY + (flipY ? height - 1 - y : y);
                int writeX = destinationX + x;
                int writeY = destinationY + y;
                if (readX < 0 || readY < 0 || readX >= target.Width || readY >= target.Height)
                    continue;
                if (writeX < 0 || writeY < 0 || writeX >= target.Width || writeY >= target.Height)
                    continue;

                target.SetPixel(writeX, writeY, target.GetPixel(readX, readY));
            }
        }
    }
}
