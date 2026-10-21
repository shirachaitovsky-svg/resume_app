namespace ResumeApp.Features.Email;

/// <summary>A plain-text email. There is no HTML and no attachment support by design (AD-12).</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody);

public enum EmailSendStatus { Sent, Failed, RateLimited, Permanent }

/// <param name="Status">Sent; Failed (counts as an attempt); RateLimited (the provider said 429, does not count); Permanent (dead at once).</param>
/// <param name="ErrorCategory">Short category only, never a provider message (it may contain an address).</param>
/// <param name="RetryAfter">Provider hint for RateLimited.</param>
public sealed record EmailSendResult(EmailSendStatus Status, string? ErrorCategory = null, TimeSpan? RetryAfter = null)
{
    public static EmailSendResult Sent { get; } = new(EmailSendStatus.Sent);
    public static EmailSendResult Failed(string category) => new(EmailSendStatus.Failed, category);
    /// <summary>A failure a retry cannot fix (bad address, hard rejection): the row goes straight to dead.</summary>
    public static EmailSendResult Permanent(string category) => new(EmailSendStatus.Permanent, category);
    public static EmailSendResult RateLimited(TimeSpan? retryAfter) => new(EmailSendStatus.RateLimited, "rate-limited", retryAfter);
}

/// <summary>The only point where the email provider is swapped (AD-4). Implementations never throw for provider errors.</summary>
public interface IEmailSender
{
    /// <param name="idempotencyKey">Stable per outbox row, so a provider can drop a duplicate send.</param>
    Task<EmailSendResult> SendAsync(EmailMessage message, string idempotencyKey, CancellationToken ct);
}
