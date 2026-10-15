using SkiaSharp;

namespace PassingTrace.Identity.AuthorizationServer.Profile;

public static class AvatarImage
{
    public const int MaxBytes = 5 * 1024 * 1024;

    public static byte[] Normalize(byte[] bytes)
    {
        if (bytes.Length is 0 or > MaxBytes) throw new InvalidDataException("头像不能超过 5MB。");
        using var input = new SKMemoryStream(bytes);
        using var codec = SKCodec.Create(input);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
            throw new InvalidDataException("请选择 JPG、PNG 或 WebP 图片。");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > 16_000_000)
            throw new InvalidDataException("图片尺寸过大，请裁剪或缩小后重试。");
        using var bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("图片已损坏，请重新选择。");
        var side = Math.Min(info.Width, info.Height);
        using var surface = SKSurface.Create(new SKImageInfo(512, 512));
        surface.Canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true };
        surface.Canvas.DrawBitmap(bitmap,
            SKRect.Create((info.Width - side) / 2f, (info.Height - side) / 2f, side, side),
            new SKRect(0, 0, 512, 512), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        // Re-encoding strips EXIF/GPS and all other original metadata.
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
