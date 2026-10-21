using Microsoft.Extensions.Logging;
using ResumeApp.Features.Profiles;
using SkiaSharp;

namespace ResumeApp.Infrastructure;

/// <summary>
/// Image validation and re-encoding with SkiaSharp (MIT). The type is detected from the content, only JPEG, PNG and
/// WebP are accepted (never SVG), dimensions are bounded before decoding, EXIF orientation is applied, and the result
/// is a fresh JPEG. Decoding runs off the request thread, at most <see cref="MaxConcurrency"/> at a time.
/// </summary>
public sealed class SkiaPhotoProcessor(ILogger<SkiaPhotoProcessor> logger) : IPhotoProcessor
{
    public const int MaxSourceSide = 4096;
    public const long MaxSourcePixels = 12_000_000;
    public const int MaxOutputSide = 1024;
    public const int MaxConcurrency = 2;
    public static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(2);
    private static readonly int[] Qualities = [85, 70, 55, 40];

    private readonly SemaphoreSlim _gate = new(MaxConcurrency);

    public async Task<PhotoResult> ProcessAsync(byte[] upload, CancellationToken ct)
    {
        if (upload.Length == 0) return new(PhotoOutcome.NotAnImage);
        if (upload.Length > IPhotoProcessor.MaxUploadBytes) return new(PhotoOutcome.TooLarge);

        if (!await _gate.WaitAsync(GateTimeout, ct)) return new(PhotoOutcome.Busy);
        try
        {
            return await Task.Run(() => Process(upload), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An infrastructure fault (native asset missing, allocation failure): surface as 500, never as a user error.
            logger.LogWarning("Photo processing failed: {ErrorType}", ex.GetType().Name);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Synchronous core. Decode failures are results; anything else propagates.</summary>
    public static PhotoResult Process(byte[] data)
    {
        if (data.Length == 0) return new(PhotoOutcome.NotAnImage);
        if (data.Length > IPhotoProcessor.MaxUploadBytes) return new(PhotoOutcome.TooLarge);

        // Inspect the header only: format and size are known before any pixel is decoded.
        using var codec = SKCodec.Create(new MemoryStream(data));
        if (codec is null) return new(PhotoOutcome.NotAnImage);
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
            return new(PhotoOutcome.NotAnImage);
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0) return new(PhotoOutcome.Corrupt);
        if (info.Width > MaxSourceSide || info.Height > MaxSourceSide || (long)info.Width * info.Height > MaxSourcePixels)
            return new(PhotoOutcome.TooLarge);

        // Strict full decode: a truncated or damaged file is rejected instead of being partly rendered.
        using var source = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(source.Info, source.GetPixels()) != SKCodecResult.Success)
            return new(PhotoOutcome.Corrupt);

        var origin = codec.EncodedOrigin;
        var transposed = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var srcW = transposed ? source.Height : source.Width; // size after orientation
        var srcH = transposed ? source.Width : source.Height;
        var scale = Math.Min(1.0, (double)MaxOutputSide / Math.Max(srcW, srcH));
        var width = Math.Max(1, (int)Math.Round(srcW * scale));
        var height = Math.Max(1, (int)Math.Round(srcH * scale));

        // Draw on an opaque white canvas: JPEG has no alpha, and this drops any embedded metadata.
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque))
            ?? throw new InvalidOperationException("Could not create a drawing surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Scale((float)width / srcW, (float)height / srcH);
        canvas.Concat(OrientationMatrix(origin, source.Width, source.Height));
        using var image = SKImage.FromBitmap(source);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);

        using var snapshot = surface.Snapshot();
        foreach (var quality in Qualities)
        {
            using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, quality)
                ?? throw new InvalidOperationException("JPEG encoding failed.");
            if (encoded.Size <= IPhotoProcessor.MaxUploadBytes)
                return new(PhotoOutcome.Ok, encoded.ToArray(), "image/jpeg");
        }
        return new(PhotoOutcome.TooLarge);
    }

    /// <summary>Maps stored pixels to display orientation (EXIF origin), for an image of size w x h.</summary>
    private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity,
    };
}
