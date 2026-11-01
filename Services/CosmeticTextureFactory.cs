using System;
using SkiaSharp;

namespace TVLauncher.Services;

/// <summary>
/// Genera las texturas PNG de los cosméticos TVLauncher. Es la única fuente
/// de verdad para el visor 3D del launcher y para los assets del mod Fabric.
/// </summary>
public static class CosmeticTextureFactory
{
    /// <summary>
    /// Textura de la capa TVLauncher. El visor muestrea la región estándar de capa
    /// (x 0..22, y 0..17 en una textura de 64x64), así que todo el diseño —incluido
    /// el logo TVL— debe dibujarse dentro de ese rectángulo.
    /// </summary>
    public static byte[] CreateCapePng()
    {
        var info = new SKImageInfo(64, 32, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap(info);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint { IsAntialias = false };

        // Fondo: degradado vertical oscuro -> violeta en toda la zona de la capa.
        var colors = new[] { SKColor.Parse("#0F1115"), SKColor.Parse("#3B2E9B"), SKColor.Parse("#6C5CE7") };
        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, 0), new SKPoint(0, 17), colors, null, SKShaderTileMode.Clamp))
        {
            paint.Shader = shader;
            canvas.DrawRect(new SKRect(0, 0, 22, 17), paint);
            paint.Shader = null;
        }

        // Marco neón dentro de la ventana que ve la cámara (la capa se gira 180°
        // y la cara exterior muestrea x 3..13, y 1..17).
        paint.Color = SKColor.Parse("#6C5CE7");
        canvas.DrawRect(new SKRect(3, 0.6f, 3.7f, 16.4f), paint);
        canvas.DrawRect(new SKRect(12.3f, 0.6f, 13, 16.4f), paint);

        // Bandas de acento.
        paint.Color = SKColor.Parse("#8B7BFF");
        canvas.DrawRect(new SKRect(3.7f, 2, 12.3f, 2.7f), paint);
        canvas.DrawRect(new SKRect(3.7f, 14.3f, 12.3f, 15), paint);

        // Logo TVL: centrado en la cara exterior (x 3..13) y con sombra para contrastar.
        paint.Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
        paint.TextSize = 4.8f;
        paint.TextAlign = SKTextAlign.Center;
        float logoY = 11.2f;
        paint.Color = SKColor.Parse("#0B0C10");
        canvas.DrawText("TVL", 8, logoY + 0.6f, paint);
        paint.Color = SKColors.White;
        canvas.DrawText("TVL", 8, logoY, paint);
        paint.TextAlign = SKTextAlign.Left;

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    /// <summary>
    /// Textura del sombrero TVLauncher. El atlas se divide en tres bandas que cubren
    /// todo el UV que muestrean las cajas (no solo el centro de cada cara):
    /// copa en 0..32 x 0..12, ala en 0..40 x 16..28 y cinta en 0..32 x 32..40.
    /// </summary>
    public static byte[] CreateHatPng()
    {
        var info = new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap(info);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint { IsAntialias = false };

        // Copa: degradado violeta en toda la banda. La caja es 6.2 x 2.8 x 6.2,
        // así que las caras laterales muestrean v = 6.2..9.0.
        var crownColors = new[] { SKColor.Parse("#B7AAFF"), SKColor.Parse("#8B7BFF"), SKColor.Parse("#6C5CE7") };
        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, 0), new SKPoint(0, 9f), crownColors, null, SKShaderTileMode.Clamp))
        {
            paint.Shader = shader;
            canvas.DrawRect(new SKRect(0, 0, 32, 12), paint);
            paint.Shader = null;
        }

        // Cinta: banda oscura con filo luminoso en la base.
        paint.Color = SKColor.Parse("#2A2360");
        canvas.DrawRect(new SKRect(0, 32, 32, 40), paint);
        paint.Color = SKColor.Parse("#D8D2FF");
        canvas.DrawRect(new SKRect(0, 38.55f, 32, 38.85f), paint);

        // Ala: degradado en la banda 16..28. Las caras laterales (0.5 de alto)
        // muestrean v = 25.2..25.7, que es el filo neón; la cara superior usa 16..25.2.
        var brimColors = new[] { SKColor.Parse("#9C8CFF"), SKColor.Parse("#6C5CE7"), SKColor.Parse("#4A3BC7") };
        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, 16), new SKPoint(0, 25.2f), brimColors, null, SKShaderTileMode.Clamp))
        {
            paint.Shader = shader;
            canvas.DrawRect(new SKRect(0, 16, 40, 25.2f), paint);
            paint.Shader = null;
        }
        paint.Color = SKColor.Parse("#C9C0FF");
        canvas.DrawRect(new SKRect(0, 25.2f, 40, 25.7f), paint);
        paint.Color = SKColor.Parse("#3A2FA0");
        canvas.DrawRect(new SKRect(0, 25.7f, 40, 26.3f), paint);

        // Detalle diagonal sobre la cara superior del ala.
        paint.Color = SKColor.Parse("#B7AAFF");
        canvas.DrawLine(4, 18, 36, 24, paint);

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
