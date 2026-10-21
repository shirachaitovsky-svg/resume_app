using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Text;
using ResumeApp.Features.Email;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Infrastructure;

/// <summary>SMTP through MailKit (MIT). Plain text only; every failure is a retryable "Failed" with a category, never the server message.</summary>
public sealed class SmtpEmailSender(EmailOptions options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>Builds a text/plain; charset=utf-8 message. Header values pass through <see cref="EmailHeaderSanitizer"/>.</summary>
    public static MimeMessage Build(EmailMessage message, string from, string idempotencyKey)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(EmailHeaderSanitizer.Sanitize(from)));
        mime.To.Add(MailboxAddress.Parse(EmailHeaderSanitizer.Sanitize(message.To)));
        mime.Subject = EmailHeaderSanitizer.Sanitize(message.Subject);
        mime.Headers.Add("X-Idempotency-Key", EmailHeaderSanitizer.Sanitize(idempotencyKey));
        var body = new TextPart(TextFormat.Plain) { ContentTransferEncoding = ContentEncoding.Base64 };
        body.SetText(System.Text.Encoding.UTF8, message.TextBody);
        mime.Body = body;
        return mime;
    }

    /// <summary>TLS choice: not secure means none, port 465 means implicit TLS, anything else upgrades with STARTTLS.</summary>
    public static SecureSocketOptions SocketOptions(bool secure, int port)
        => !secure ? SecureSocketOptions.None : port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

    public async Task<EmailSendResult> SendAsync(EmailMessage message, string idempotencyKey, CancellationToken ct)
    {
        var smtp = options.Smtp ?? throw new InvalidOperationException("SMTP is not configured.");
        using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        try
        {
            var mime = Build(message, options.From, idempotencyKey);
            await client.ConnectAsync(smtp.Host, smtp.Port, SocketOptions(smtp.Secure, smtp.Port), ct);
            if (!string.IsNullOrEmpty(smtp.User)) await client.AuthenticateAsync(smtp.User, smtp.Password ?? "", ct);
            await client.SendAsync(mime, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // On shutdown (cancelled token) the exception propagates and no failed attempt is recorded.
            var (result, category) = ex switch
            {
                ParseException => (EmailSendResult.Permanent("smtp-bad-address"), "smtp-bad-address"),
                SmtpCommandException { StatusCode: >= (SmtpStatusCode)500 } => (EmailSendResult.Permanent("smtp-rejected"), "smtp-rejected"),
                SmtpCommandException => (EmailSendResult.Failed("smtp-deferred"), "smtp-deferred"),
                AuthenticationException => (EmailSendResult.Failed("smtp-auth"), "smtp-auth"),
                SslHandshakeException => (EmailSendResult.Failed("smtp-tls"), "smtp-tls"),
                _ => (EmailSendResult.Failed("smtp-unavailable"), "smtp-unavailable"),
            };
            logger.LogWarning("SMTP send failed: {Category} ({ErrorType})", category, ex.GetType().Name);
            return result;
        }

        // The server accepted the message. A failure to say goodbye must never turn this into a retry (a duplicate mail).
        try
        {
            await client.DisconnectAsync(true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogDebug("SMTP disconnect failed after a successful send: {ErrorType}", ex.GetType().Name);
        }
        return EmailSendResult.Sent;
    }
}
