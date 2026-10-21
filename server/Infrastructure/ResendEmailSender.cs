using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using ResumeApp.Features.Email;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Infrastructure;

/// <summary>Resend over HTTP. 429 is a deferral (not a failed attempt); 5xx, timeouts and network errors are transient failures.</summary>
public sealed class ResendEmailSender(HttpClient http, EmailOptions options, ILogger<ResendEmailSender> logger) : IEmailSender
{
    public const string EndpointUrl = "https://api.resend.com/emails";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public async Task<EmailSendResult> SendAsync(EmailMessage message, string idempotencyKey, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, EndpointUrl)
            {
                Content = JsonContent.Create(new
                {
                    from = EmailHeaderSanitizer.Sanitize(options.From),
                    to = new[] { EmailHeaderSanitizer.Sanitize(message.To) },
                    subject = EmailHeaderSanitizer.Sanitize(message.Subject),
                    text = message.TextBody,
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ResendApiKey);
            request.Headers.Add("Idempotency-Key", EmailHeaderSanitizer.Sanitize(idempotencyKey));

            using var response = await http.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode) return EmailSendResult.Sent;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return EmailSendResult.RateLimited(RetryAfterOf(response));

            var code = (int)response.StatusCode;
            // 4xx other than 408 (timeout) and 429 is a rejection a retry cannot fix.
            if (code is >= 400 and < 500 && code != 408)
            {
                logger.LogWarning("Resend rejected the message (status {Status})", code);
                return EmailSendResult.Permanent("resend-rejected");
            }
            var category = code >= 500 ? "resend-5xx" : "resend-unexpected";
            logger.LogWarning("Resend send failed: {Category} (status {Status})", category, (int)response.StatusCode);
            return EmailSendResult.Failed(category);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning("Resend send failed: network ({ErrorType})", ex.GetType().Name);
            return EmailSendResult.Failed("resend-unavailable");
        }
    }

    private static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta) return delta;
        if (header?.Date is { } date) return date - DateTimeOffset.UtcNow;
        return null;
    }
}
