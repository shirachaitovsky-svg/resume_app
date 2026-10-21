namespace ResumeApp.Features.Email;

/// <summary>Request side of the personal link (FR-11): only writes outbox rows; never sends and never reveals whether the email exists.</summary>
public sealed class EditLinkService(IEmailOutbox outbox, TimeProvider clock)
{
    public async Task RequestAsync(string normalizedEmail, CancellationToken ct)
        => await outbox.EnqueueEditLinksAsync(normalizedEmail, clock.GetUtcNow().UtcDateTime, ct);
}
