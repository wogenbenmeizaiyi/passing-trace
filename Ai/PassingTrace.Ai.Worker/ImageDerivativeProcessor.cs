using SkiaSharp;

namespace PassingTrace.Ai.Worker;

public sealed record ImageDerivatives(byte[] AiImage, byte[] Thumbnail);

public sealed class ImageDerivativeProcessor
{
    private const int AiMaxEdge = 2048;
    private const int ThumbnailMaxEdge = 480;
    private const int AiMaxBytes = 8 * 1024 * 1024;

    public async Task<ImageDerivatives> ProcessAsync(Stream source, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        using var bitmap = SKBitmap.Decode(buffer.ToArray())
            ?? throw new InvalidDataException("图片无法解码或格式已损坏。");

        var ai = RenderJpeg(bitmap, AiMaxEdge, AiMaxBytes);
        var thumbnail = RenderJpeg(bitmap, ThumbnailMaxEdge, 512 * 1024);
        return new ImageDerivatives(ai, thumbnail);
    }

    private static byte[] RenderJpeg(SKBitmap source, int maxEdge, int maxBytes)
    {
        var scale = Math.Min(1d, maxEdge / (double)Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.DrawBitmap(
            source,
            new SKRect(0, 0, width, height),
            new SKSamplingOptions(SKCubicResampler.Mitchell),
            new SKPaint { IsAntialias = true });
        surface.Canvas.Flush();
        using var image = surface.Snapshot();

        for (var quality = 88; quality >= 45; quality -= 7)
        {
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            var bytes = data.ToArray();
            if (bytes.Length <= maxBytes || quality == 46)
            {
                return bytes;
            }
        }

        throw new InvalidOperationException("图片压缩失败。");
    }
}
