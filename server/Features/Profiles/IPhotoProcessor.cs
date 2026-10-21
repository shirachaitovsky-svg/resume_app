namespace ResumeApp.Features.Profiles;

public enum PhotoOutcome { Ok, NotAnImage, TooLarge, Corrupt, Busy }

/// <param name="Bytes">Re-encoded image (JPEG or WebP); null unless <see cref="Outcome"/> is Ok.</param>
public sealed record PhotoResult(PhotoOutcome Outcome, byte[]? Bytes = null, string? ContentType = null);

/// <summary>Validates an uploaded image by content and re-encodes it (AD-19). Never writes to disk.</summary>
public interface IPhotoProcessor
{
    /// <summary>The largest upload accepted, in bytes (about 300KB, the size the browser downsizes to).</summary>
    const int MaxUploadBytes = 300 * 1024;

    /// <summary>Runs off the request thread behind a small concurrency gate; <see cref="PhotoOutcome.Busy"/> when the gate stays full.</summary>
    Task<PhotoResult> ProcessAsync(byte[] upload, CancellationToken ct);
}
