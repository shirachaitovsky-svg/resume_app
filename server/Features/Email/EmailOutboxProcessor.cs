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
    TimeSpan? MaxDeferralAge = null,
    IReadOnlyList<string>? AdminRecipients = null)
{
    /// <summary>The manager's addresses for the contact-request notification (story 3.2); empty means such rows are dead with no-recipient.</summary>
    public IReadOnlyList<string> AdminRecipientsOrEmpty => AdminRecipients ?? [];
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
    TimeProvider clock, ILogger<EmailOutboxProcessor> logger, IOutboxMonitor? monitor = null)
{
    public const string CategoryNoRecipient = "no-recipient";
    public const string CategoryRevisionMissing = "revision-missing";
    public const string CategoryRevisionNotReturned = "revision-not-returned";
    public const string CategoryEmptyNote = "empty-note";
    public const string CategoryUnknownTemplate = "unknown-template";
    public const string CategoryDeferredTooLong = "deferred-too-long";
    public const string CategoryRequestNotAwaiting = "request-not-awaiting";
    public const string CategoryProfileNotActive = "profile-not-active";
    public const string CategoryRequestNotVerified = "request-not-verified";
    public const string CategoryRequestNotSent = "request-not-sent";
    public const string CategoryConsentExpired = "consent-expired";
    public const string CategoryContactChanged = "contact-changed";
    public const string CategoryDeliveryNotApproved = "delivery-not-approved";
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
        if (row.TemplateKey == EmailOutboxEntry.ContactVerificationTemplate)
        {
            await ProcessContactVerificationAsync(row, ct);
            return;
        }
        if (row.TemplateKey == EmailOutboxEntry.ContactDecisionRequestTemplate)
        {
            await ProcessContactDecisionAsync(row, ct);
            return;
        }
        if (row.TemplateKey == EmailOutboxEntry.ContactDeliveryTemplate)
        {
            await ProcessContactDeliveryAsync(row, ct);
            return;
        }
        if (row.TemplateKey == EmailOutboxEntry.ContactRequestNotificationTemplate)
        {
            await ProcessContactNotificationAsync(row, ct);
            return;
        }
        if (row.TemplateKey == EmailOutboxEntry.OutboxAlertTemplate)
        {
            await ProcessOutboxAlertAsync(row, ct);
            return;
        }

        // Template registry: the only remaining non-contact template is "submission-returned". Any other key (including the retired
        // "edit-link" rows of story 2.1) and rows missing their ids are dead with unknown-template, and nothing is sent.
        if (row.TemplateKey != EmailOutboxEntry.SubmissionReturnedTemplate || row.ProfileId is not { } profileId || row.RevisionId is not { } revisionId)
        {
            await FailAsync(row, CategoryUnknownTemplate, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }

        var recipient = await outbox.GetReturnedMailRecipientAsync(profileId, ct);
        if (recipient is null)
        {
            // The profile was removed (or the private row is gone): nothing may be sent.
            await FailAsync(row, CategoryNoRecipient, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }

        // The note lives on the revision, never in the outbox; it is read now.
        var found = await outbox.GetReturnNoteAsync(revisionId, ct);
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

        var message = new EmailMessage(EmailHeaderSanitizer.Sanitize(recipient.Email), SubmissionReturnedEmail.Subject,
            SubmissionReturnedEmail.Body(settings.PublicBaseUrl, recipient.CohortName, found.Note));

        await SendAndRecordAsync(row, message, ct);
    }

    /// <summary>Story 3.1: the verification mail to a visitor. Recipient and name are read from the request now; the token is issued now.</summary>
    private async Task ProcessContactVerificationAsync(ClaimedEmail row, CancellationToken ct)
    {
        if (row.RequestId is not { } requestId)
        {
            await FailAsync(row, CategoryUnknownTemplate, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }

        var recipient = await outbox.GetContactVerificationRecipientAsync(requestId, clock.GetUtcNow().UtcDateTime, ct);
        if (recipient is null)
        {
            // Removed, expired or already verified before the send: nothing may be sent.
            await FailAsync(row, CategoryRequestNotAwaiting, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }
        if (!recipient.ProfileActive)
        {
            // The profile was removed (or hidden) after the request: nothing is sent and no token is issued. Dead also closes the request.
            await FailAsync(row, CategoryProfileNotActive, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }

        var token = await tokens.IssueContactVerificationTokenAsync(requestId, recipient.ProfileId, clock.GetUtcNow().UtcDateTime, ct);
        if (token is null)
        {
            // Closed (or the profile stopped being Active) between the read and the locked check.
            await FailAsync(row, CategoryRequestNotAwaiting, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }

        var message = new EmailMessage(EmailHeaderSanitizer.Sanitize(recipient.Email), ContactVerificationEmail.Subject,
            ContactVerificationEmail.Body(settings.PublicBaseUrl, token, recipient.Name));
        await SendAndRecordAsync(row, message, ct);
    }

    /// <summary>Story 3.3: the mail to the participant. Her address is read from the private row now; the token is issued now.</summary>
    private async Task ProcessContactDecisionAsync(ClaimedEmail row, CancellationToken ct)
    {
        if (row.RequestId is not { } requestId)
        {
            await FailAsync(row, CategoryUnknownTemplate, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }

        var info = await outbox.GetContactDecisionRecipientAsync(requestId, ct);
        if (info is null || !info.RequestSent)
        {
            await FailAsync(row, CategoryRequestNotSent, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }
        if (!info.ProfileActive)
        {
            await FailAsync(row, CategoryProfileNotActive, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }
        if (string.IsNullOrWhiteSpace(info.ParticipantEmail))
        {
            await FailAsync(row, CategoryNoRecipient, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }

        var token = await tokens.IssueContactDecisionTokenAsync(requestId, info.ProfileId, clock.GetUtcNow().UtcDateTime, ct);
        if (token is null)
        {
            // Closed (or the profile stopped being Active) between the read and the locked check: no token, nothing sent.
            await FailAsync(row, CategoryRequestNotSent, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }

        var message = new EmailMessage(EmailHeaderSanitizer.Sanitize(info.ParticipantEmail), ContactDecisionEmail.Subject,
            ContactDecisionEmail.Body(settings.PublicBaseUrl, token, info.VisitorName, info.Reason));
        await SendAndRecordAsync(row, message, ct);
    }

    /// <summary>
    /// Story 3.4: the delivery of the participant's details to the visitor's verified address. Every condition is re-checked now; a failed check is
    /// final (dead, nothing sent). The address and phone are read now and live only in the mail. The provider key is the row id, stable across retries.
    /// <c>delivered</c> is set only in the sent path (<see cref="IEmailOutbox.MarkDeliverySentAsync"/>).
    /// </summary>
    private async Task ProcessContactDeliveryAsync(ClaimedEmail row, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (row.RequestId is not { } requestId)
        {
            await FailAsync(row, CategoryUnknownTemplate, true, true, now, ct);
            return;
        }

        var info = await outbox.GetContactDeliveryAsync(requestId, ct);
        if (info is null)
        {
            await FailAsync(row, CategoryDeliveryNotApproved, true, true, now, ct);
            return;
        }
        if (info.Status == ResumeApp.Domain.Contacts.ContactRequestStatus.Delivered)
        {
            // Claimed again after the details already went out: close the row without a second mail.
            await MarkSentWithRetryAsync(row, ct);
            return;
        }
        if (!info.ProfileActive)
        {
            await FailAsync(row, CategoryProfileNotActive, true, true, now, ct);
            return;
        }
        if (info.Status != ResumeApp.Domain.Contacts.ContactRequestStatus.ParticipantApproved || info.ManagerApprovedAt is null)
        {
            await FailAsync(row, CategoryDeliveryNotApproved, true, true, now, ct);
            return;
        }
        if (info.ParticipantDecidedAt is not { } decidedAt || now - decidedAt > ResumeApp.Domain.Contacts.ContactRequest.DecisionLifetime)
        {
            await FailAsync(row, CategoryConsentExpired, true, true, now, ct);
            return;
        }
        if (info.ParticipantContactVersion is null || info.ParticipantContactVersion != info.ContactVersion)
        {
            await FailAsync(row, CategoryContactChanged, true, true, now, ct);
            return;
        }
        if (string.IsNullOrWhiteSpace(info.ParticipantEmail) || string.IsNullOrWhiteSpace(info.ParticipantPhone) || string.IsNullOrWhiteSpace(info.VisitorEmail))
        {
            await FailAsync(row, CategoryNoRecipient, true, true, now, ct);
            return;
        }

        var message = new EmailMessage(EmailHeaderSanitizer.Sanitize(info.VisitorEmail), ContactDeliveryEmail.Subject,
            ContactDeliveryEmail.Body(info.ParticipantName ?? string.Empty, info.ParticipantEmail, info.ParticipantPhone));
        var result = await sender.SendAsync(message, row.Id.ToString("D"), ct);
        await RecordResultAsync(row, result, ct);
    }

    /// <summary>
    /// Story 3.2: the notification to the manager's configured addresses, one mail each. The row is sent only when every mail went out;
    /// otherwise the whole row is retried (at-least-once: an address that already got the mail may get it again).
    /// </summary>
    private async Task ProcessContactNotificationAsync(ClaimedEmail row, CancellationToken ct)
    {
        if (row.RequestId is not { } requestId)
        {
            await FailAsync(row, CategoryUnknownTemplate, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }

        var recipients = settings.AdminRecipientsOrEmpty;
        if (recipients.Count == 0)
        {
            await FailAsync(row, CategoryNoRecipient, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }

        var info = await outbox.GetContactNotificationAsync(requestId, ct);
        if (info is null)
        {
            await FailAsync(row, CategoryRequestNotVerified, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }

        var body = ContactRequestNotificationEmail.Body(settings.PublicBaseUrl, info);
        var messages = recipients.Select(r => new EmailMessage(EmailHeaderSanitizer.Sanitize(r), ContactRequestNotificationEmail.Subject, body)).ToList();
        await RecordResultAsync(row, await SendToAllAsync(row, messages, ct), ct);
    }

    /// <summary>Story 4.2: the alert to the manager's addresses about dead or stuck rows. The counts are read now; the mail carries numbers and a link only.</summary>
    private async Task ProcessOutboxAlertAsync(ClaimedEmail row, CancellationToken ct)
    {
        var recipients = settings.AdminRecipientsOrEmpty;
        if (monitor is null || recipients.Count == 0)
        {
            await FailAsync(row, monitor is null ? CategoryUnknownTemplate : CategoryNoRecipient, true, true, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }

        var health = await monitor.GetHealthAsync(clock.GetUtcNow().UtcDateTime, ct);
        if (health.Dead + health.StuckPending == 0)
        {
            // The fault was cleared since the alert was queued: close the row without a misleading "0 and 0" mail.
            await MarkSentWithRetryAsync(row, ct);
            return;
        }
        var body = OutboxAlertEmail.Body(settings.PublicBaseUrl, health);
        var messages = recipients.Select(r => new EmailMessage(EmailHeaderSanitizer.Sanitize(r), OutboxAlertEmail.Subject, body)).ToList();
        await RecordResultAsync(row, await SendToAllAsync(row, messages, ct), ct);
    }

    private async Task SendAndRecordAsync(ClaimedEmail row, EmailMessage message, CancellationToken ct)
    {
        // A retry sends a different body (a new token), so the provider key changes with every claim.
        var result = await sender.SendAsync(message, $"{row.Id:D}:{row.ClaimCount}", ct);
        await RecordResultAsync(row, result, ct);
    }

    /// <summary>
    /// Sends one message per recipient and folds the outcomes into one result for the row. Each send is isolated: an exception becomes a
    /// transient failure of that address only. The provider idempotency key is stable per (row, address) across retries, so a provider that
    /// honours it drops the resend to an address that already got the mail (the body of this template never changes between retries).
    /// Aggregation, in priority order:
    ///   1. a rate-limited outcome stops the loop (the provider is throttling; the rest would be refused too) and defers the row with that
    ///      retry-after, without counting an attempt;
    ///   2. otherwise any transient failure retries the whole row with backoff (at-least-once);
    ///   3. otherwise, if every address failed permanently the row is dead (a retry cannot help); if at least one address was delivered the
    ///      row is sent, because retrying would only repeat mail to the addresses that work;
    ///   4. otherwise (all delivered) the row is sent.
    /// </summary>
    private async Task<EmailSendResult> SendToAllAsync(ClaimedEmail row, IReadOnlyList<EmailMessage> messages, CancellationToken ct)
    {
        EmailSendResult? transient = null, permanent = null;
        var delivered = 0;
        foreach (var message in messages)
        {
            var key = $"{row.Id:D}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(message.To)))[..16].ToLowerInvariant()}";
            EmailSendResult one;
            try
            {
                one = await sender.SendAsync(message, key, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Outbox row {OutboxId}: a send threw {ErrorType}", row.Id, ex.GetType().Name); // type only, never the address
                one = EmailSendResult.Failed("send-exception");
            }
            switch (one.Status)
            {
                case EmailSendStatus.Sent: delivered++; break;
                case EmailSendStatus.RateLimited: return one;
                case EmailSendStatus.Permanent: permanent ??= one; break;
                default: transient ??= one; break;
            }
        }
        if (transient is not null) return transient;
        if (permanent is not null && delivered == 0) return permanent;
        return EmailSendResult.Sent;
    }

    private async Task RecordResultAsync(ClaimedEmail row, EmailSendResult result, CancellationToken ct)
    {
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
                var marked = row.TemplateKey == EmailOutboxEntry.ContactDeliveryTemplate
                    ? await outbox.MarkDeliverySentAsync(row, clock.GetUtcNow().UtcDateTime, ct)
                    : await outbox.MarkSentAsync(row, clock.GetUtcNow().UtcDateTime, ct);
                if (marked == 0)
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
