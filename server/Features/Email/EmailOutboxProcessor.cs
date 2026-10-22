using Microsoft.Extensions.Logging;
using ResumeApp.Domain.Email;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Features.Email;

public sealed record EmailOutboxSettings(
    string PublicBaseUrl,
    int MaxAttempts = 5,
    TimeSpan? Lease = null,
    TimeSpan? BaseDelay = null,
    TimeSpan? MaxDelay = null,
    TimeSpan? MaxDeferralAge = null)
{
    public TimeSpan LeaseOrDefault => Lease ?? TimeSpan.FromMinutes(2);
    public TimeSpan BaseDelayOrDefault => BaseDelay ?? TimeSpan.FromSeconds(30);
    public TimeSpan MaxDelayOrDefault => MaxDelay ?? TimeSpan.FromHours(1);
    /// <summary>A row still being deferred by provider 429s this long after creation becomes dead.</summary>
    public TimeSpan MaxDeferralAgeOrDefault => MaxDeferralAge ?? TimeSpan.FromHours(24);
}

/// <summary>
/// One pass over the outbox: claim ONE row (short, atomic), send it outside any transaction, mark the result, repeat until nothing is due
/// (AD-4, AD-16). One row per claim keeps the lease honest while sends are slow. The token is issued at send time and the recipient is
/// read at send time, so neither is stored in the outbox (AD-15). Delivery is at-least-once: see the README.
/// </summary>
public sealed class EmailOutboxProcessor(IEmailOutbox outbox, IPersonalTokenIssuer tokens, IEmailSender sender, EmailOutboxSettings settings,
    TimeProvider clock, ILogger<EmailOutboxProcessor> logger)
{
    public const string CategoryNoRecipient = "no-recipient";
    public const string CategoryRevisionMissing = "revision-missing";
    public const string CategoryRevisionNotReturned = "revision-not-returned";
    public const string CategoryEmptyNote = "empty-note";
    public const string CategoryUnknownTemplate = "unknown-template";
    public const string CategoryDeferredTooLong = "deferred-too-long";
    private const int MarkSentTries = 3;

    /// <summary>Returns how many rows were claimed (0 means the worker may sleep). Cancellation propagates and records nothing.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        var count = 0;
        while (await outbox.ClaimNextAsync(settings.LeaseOrDefault, clock.GetUtcNow().UtcDateTime, ct) is { } row)
        {
            count++;
            try
            {
                await ProcessOneAsync(row, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log the type only: the message could carry an address. The failure is recorded and retried with backoff.
                logger.LogWarning("Outbox row {OutboxId} failed unexpectedly: {ErrorType}", row.Id, ex.GetType().Name);
                await SafeFailAsync(row, "unexpected-error", ct);
            }
        }
        return count;
    }

    private async Task ProcessOneAsync(ClaimedEmail row, CancellationToken ct)
    {
        // Template registry: key -> (subject, body builder given the freshly issued token). Unknown keys and rows missing their ids are dead.
        var returned = row.TemplateKey == EmailOutboxEntry.SubmissionReturnedTemplate;
        if ((row.TemplateKey != EmailOutboxEntry.EditLinkTemplate && !returned) || row.ProfileId is not { } profileId || (returned && row.RevisionId is null))
        {
            await FailAsync(row, CategoryUnknownTemplate, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }

        var recipient = await outbox.GetEditLinkRecipientAsync(profileId, ct);
        if (recipient is null)
        {
            // The profile was removed (or the private row is gone): nothing may be sent.
            await FailAsync(row, CategoryNoRecipient, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }

        string? note = null;
        if (returned)
        {
            // The note lives on the revision, never in the outbox; it is read now, before a token is issued.
            var found = await outbox.GetReturnNoteAsync(row.RevisionId!.Value, ct);
            if (found.Status != ReturnNoteStatus.Ok)
            {
                var category = found.Status switch
                {
                    ReturnNoteStatus.RevisionMissing => CategoryRevisionMissing,
                    ReturnNoteStatus.NotReturned => CategoryRevisionNotReturned,
                    _ => CategoryEmptyNote,
                };
                await FailAsync(row, category, true, true, clock.GetUtcNow().UtcDateTime, ct);
                return;
            }
            note = found.Note;
        }

        var token = await tokens.IssueEditTokenAsync(profileId, clock.GetUtcNow().UtcDateTime, ct);
        var message = returned
            ? new EmailMessage(EmailHeaderSanitizer.Sanitize(recipient.Email), SubmissionReturnedEmail.Subject,
                SubmissionReturnedEmail.Body(settings.PublicBaseUrl, token, recipient.CohortName, note))
            : new EmailMessage(EmailHeaderSanitizer.Sanitize(recipient.Email), EditLinkEmail.Subject,
                EditLinkEmail.Body(settings.PublicBaseUrl, token, recipient.CohortName));

        // A retry sends a different body (a new token), so the provider key changes with every claim.
        var result = await sender.SendAsync(message, $"{row.Id:D}:{row.ClaimCount}", ct);
        var now = clock.GetUtcNow().UtcDateTime;
        switch (result.Status)
        {
            case EmailSendStatus.Sent:
                // The mail is out: never route this row to the failure path (that would resend it).
                await MarkSentWithRetryAsync(row, ct);
                break;
            case EmailSendStatus.RateLimited when now - row.CreatedAt > settings.MaxDeferralAgeOrDefault:
                await FailAsync(row, CategoryDeferredTooLong, false, true, now, ct);
                break;
            case EmailSendStatus.RateLimited:
                // A provider 429 is a deferral, not a failed attempt.
                await FailAsync(row, result.ErrorCategory ?? "rate-limited", false, false,
                    now + Clamp(result.RetryAfter ?? settings.BaseDelayOrDefault, TimeSpan.FromSeconds(1), settings.MaxDelayOrDefault), ct);
                break;
            case EmailSendStatus.Permanent:
                await FailAsync(row, result.ErrorCategory ?? "send-rejected", true, true, now, ct);
                break;
            default:
                await FailAsync(row, result.ErrorCategory ?? "send-failed", true, false, now + Backoff(row.Attempts + 1), ct);
                break;
        }
    }

    private async Task FailAsync(ClaimedEmail row, string category, bool countAttempt, bool terminal, DateTime nextAttemptAt, CancellationToken ct)
    {
        var affected = await outbox.RecordFailureAsync(row, category, countAttempt, terminal, settings.MaxAttempts, nextAttemptAt, ct);
        if (affected == 0) logger.LogWarning("Outbox row {OutboxId}: result not recorded, the claim was lost", row.Id);
    }

    private async Task MarkSentWithRetryAsync(ClaimedEmail row, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MarkSentTries; attempt++)
        {
            try
            {
                if (await outbox.MarkSentAsync(row, clock.GetUtcNow().UtcDateTime, ct) == 0)
                    logger.LogWarning("Outbox row {OutboxId}: sent, but the claim was lost before it could be marked", row.Id);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Outbox row {OutboxId}: marking sent failed (try {Try}): {ErrorType}", row.Id, attempt, ex.GetType().Name);
                if (attempt < MarkSentTries) await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), ct);
            }
        }
        logger.LogWarning("Outbox row {OutboxId}: sent but could not be marked; it may be sent again after the lease expires", row.Id);
    }

    /// <summary>Exponential backoff with a fixed ceiling: base * 2^(attempt-1), at most the maximum delay.</summary>
    public TimeSpan Backoff(int failedAttempts)
    {
        var factor = Math.Pow(2, Math.Max(0, Math.Min(failedAttempts, 30) - 1));
        var seconds = Math.Min(settings.BaseDelayOrDefault.TotalSeconds * factor, settings.MaxDelayOrDefault.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) => value < min ? min : value > max ? max : value;

    private async Task SafeFailAsync(ClaimedEmail row, string category, CancellationToken ct)
    {
        try
        {
            await FailAsync(row, category, true, false, clock.GetUtcNow().UtcDateTime + Backoff(row.Attempts + 1), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Could not record the failure of outbox row {OutboxId}: {ErrorType}", row.Id, ex.GetType().Name);
        }
    }
}
