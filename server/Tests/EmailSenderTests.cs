using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ResumeApp.Features.Email;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

[Collection("env")]
public class EmailSenderTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration Config(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    private static readonly EmailMessage Message = new("dana@example.com", EditLinkEmail.Subject, "שלום\nhttps://app.example.test/edit#token=abc\n");

    // ---- configuration ---------------------------------------------------------------------------------------------

    [Fact]
    public void Development_defaults_to_smtp_on_localhost_1025()
    {
        var o = EmailOptions.Resolve(Config(), new Env("Development"));
        Assert.Equal(EmailProvider.Smtp, o.Provider);
        Assert.Equal("localhost", o.Smtp!.Host);
        Assert.Equal(1025, o.Smtp.Port);
        Assert.False(o.Smtp.Secure);
        Assert.Equal("http://localhost:4200", o.PublicBaseUrl);
    }

    [Fact]
    public void Outside_development_a_missing_provider_or_setting_fails_startup_with_a_clear_message()
    {
        var prod = new Env("Production");
        var ex = Assert.Throws<InvalidOperationException>(() => EmailOptions.Resolve(Config(), prod));
        Assert.Contains("EMAIL_PROVIDER", ex.Message);
        Assert.Contains("EMAIL_FROM", Assert.Throws<InvalidOperationException>(() => EmailOptions.Resolve(Config(("EMAIL_PROVIDER", "smtp")), prod)).Message);
        Assert.Contains("PUBLIC_BASE_URL", Assert.Throws<InvalidOperationException>(() => EmailOptions.Resolve(Config(("EMAIL_PROVIDER", "smtp"), ("EMAIL_FROM", "a@b.co")), prod)).Message);
        Assert.Contains("SMTP_HOST", Assert.Throws<InvalidOperationException>(() => EmailOptions.Resolve(
            Config(("EMAIL_PROVIDER", "smtp"), ("EMAIL_FROM", "a@b.co"), ("PUBLIC_BASE_URL", "https://app.test"), ("ADMIN_NOTIFICATION_EMAIL", "m@b.co")), prod)).Message);
        Assert.Contains("RESEND_API_KEY", Assert.Throws<InvalidOperationException>(() => EmailOptions.Resolve(
            Config(("EMAIL_PROVIDER", "resend"), ("EMAIL_FROM", "a@b.co"), ("PUBLIC_BASE_URL", "https://app.test"), ("ADMIN_NOTIFICATION_EMAIL", "m@b.co")), prod)).Message);
        Assert.Throws<InvalidOperationException>(() => EmailOptions.Resolve(Config(("EMAIL_PROVIDER", "carrier-pigeon")), new Env("Development")));
    }

    [Fact]
    public void A_complete_production_configuration_resolves_for_both_providers()
    {
        var prod = new Env("Production");
        var smtp = EmailOptions.Resolve(Config(("EMAIL_PROVIDER", "smtp"), ("EMAIL_FROM", "a@b.co"), ("PUBLIC_BASE_URL", "https://app.test/"), ("ADMIN_NOTIFICATION_EMAIL", "m@b.co"),
            ("SMTP_HOST", "smtp.test"), ("SMTP_PORT", "465"), ("SMTP_USER", "u"), ("SMTP_PASSWORD", "p"), ("SMTP_SECURE", "true")), prod);
        Assert.Equal("https://app.test", smtp.PublicBaseUrl);
        Assert.Equal(new SmtpSettings("smtp.test", 465, "u", "p", true), smtp.Smtp);

        var resend = EmailOptions.Resolve(Config(("EMAIL_PROVIDER", "Resend"), ("EMAIL_FROM", "a@b.co"), ("PUBLIC_BASE_URL", "https://app.test"), ("ADMIN_NOTIFICATION_EMAIL", "m@b.co"), ("RESEND_API_KEY", "k")), prod);
        Assert.Equal(EmailProvider.Resend, resend.Provider);
        Assert.Equal("k", resend.ResendApiKey);
    }

    [Theory]
    [InlineData("ftp://app.test")]
    [InlineData("not a url")]
    [InlineData("https://user:pw@app.test")]
    public void A_bad_public_base_url_is_rejected(string url)
        => Assert.Throws<InvalidOperationException>(() => EmailOptions.Resolve(Config(("PUBLIC_BASE_URL", url)), new Env("Development")));

    // ---- SMTP message ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_smtp_message_is_plain_text_utf8_with_no_html_and_a_sanitised_subject()
    {
        var mime = SmtpEmailSender.Build(Message with { Subject = "נושא\r\nBcc: evil@x.com" }, "no-reply@app.test", "id-1");

        var text = Assert.IsType<MimeKit.TextPart>(mime.Body);
        Assert.Equal("text/plain", text.ContentType.MimeType);
        Assert.Equal("utf-8", text.ContentType.Charset);
        Assert.DoesNotContain('\r', mime.Subject);
        Assert.DoesNotContain('\n', mime.Subject);
        Assert.Null(mime.Bcc.Mailboxes.FirstOrDefault());
        Assert.False(mime.Headers.Contains("Bcc"));
        Assert.Equal("id-1", mime.Headers["X-Idempotency-Key"]);
        Assert.Contains("https://app.example.test/edit#token=abc", text.Text);
    }

    [Fact]
    public async Task The_smtp_sender_delivers_a_plain_text_message_to_a_real_smtp_endpoint()
    {
        using var server = new MiniSmtpServer();
        var sender = new SmtpEmailSender(
            new EmailOptions(EmailProvider.Smtp, "no-reply@app.test", "https://app.test", new SmtpSettings("127.0.0.1", server.Port, null, null, false), null),
            NullLogger<SmtpEmailSender>.Instance);

        var result = await sender.SendAsync(Message, "row-1", CancellationToken.None);

        Assert.Equal(EmailSendStatus.Sent, result.Status);
        var data = await server.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("Content-Type: text/plain; charset=utf-8", data);
        Assert.DoesNotContain("text/html", data);
        Assert.DoesNotContain("multipart", data, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("To: dana@example.com", data);
        Assert.Contains("X-Idempotency-Key: row-1", data);
    }

    [Fact]
    public async Task The_smtp_sender_reports_an_unreachable_server_as_a_retryable_failure()
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; }
        var sender = new SmtpEmailSender(
            new EmailOptions(EmailProvider.Smtp, "no-reply@app.test", "https://app.test", new SmtpSettings("127.0.0.1", port, null, null, false), null),
            NullLogger<SmtpEmailSender>.Instance);

        var result = await sender.SendAsync(Message, "row-1", CancellationToken.None);

        Assert.Equal(EmailSendStatus.Failed, result.Status);
        Assert.Equal("smtp-unavailable", result.ErrorCategory);
    }

    /// <summary>Just enough SMTP to accept one message and hand back the DATA section.</summary>
    private sealed class MiniSmtpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public TaskCompletionSource<string> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MiniSmtpServer()
        {
            _listener.Start();
            _ = Task.Run(Serve);
        }

        private async Task Serve()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
                await writer.WriteLineAsync("220 mini ESMTP");
                var data = new StringBuilder();
                while (await reader.ReadLineAsync() is { } line)
                {
                    var verb = line.Split(' ')[0].ToUpperInvariant();
                    switch (verb)
                    {
                        case "EHLO": await writer.WriteLineAsync("250 mini"); break;
                        case "MAIL" or "RCPT" or "RSET": await writer.WriteLineAsync("250 OK"); break;
                        case "DATA":
                            await writer.WriteLineAsync("354 go");
                            while (await reader.ReadLineAsync() is { } d && d != ".") data.AppendLine(d);
                            await writer.WriteLineAsync("250 queued");
                            Received.TrySetResult(data.ToString());
                            break;
                        case "QUIT": await writer.WriteLineAsync("221 bye"); return;
                        default: await writer.WriteLineAsync("250 OK"); break;
                    }
                }
            }
            catch (Exception ex)
            {
                Received.TrySetException(ex);
            }
        }

        public void Dispose() => _listener.Stop();
    }

    // ---- Resend ----------------------------------------------------------------------------------------------------

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return respond(request);
        }
    }

    private static ResendEmailSender Resend(StubHandler handler)
        => new(new HttpClient(handler), new EmailOptions(EmailProvider.Resend, "no-reply@app.test", "https://app.test", null, "re_key"), NullLogger<ResendEmailSender>.Instance);

    [Fact]
    public async Task Resend_posts_plain_text_with_an_idempotency_key_and_bearer_auth()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var result = await Resend(handler).SendAsync(Message, "row-7", CancellationToken.None);

        Assert.Equal(EmailSendStatus.Sent, result.Status);
        Assert.Equal(ResendEmailSender.EndpointUrl, handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer re_key", handler.Request.Headers.Authorization!.ToString());
        Assert.Equal("row-7", handler.Request.Headers.GetValues("Idempotency-Key").Single());
        var json = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal("dana@example.com", json.GetProperty("to")[0].GetString());
        Assert.True(json.TryGetProperty("text", out _));
        Assert.False(json.TryGetProperty("html", out _));
    }

    [Fact]
    public async Task Resend_treats_429_as_a_deferral_with_retry_after_and_5xx_as_a_transient_failure()
    {
        var limited = new StubHandler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
            return r;
        });
        var deferred = await Resend(limited).SendAsync(Message, "k", CancellationToken.None);
        Assert.Equal(EmailSendStatus.RateLimited, deferred.Status);
        Assert.Equal(TimeSpan.FromSeconds(42), deferred.RetryAfter);

        var failed = await Resend(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway))).SendAsync(Message, "k", CancellationToken.None);
        Assert.Equal(EmailSendStatus.Failed, failed.Status);
        Assert.Equal("resend-5xx", failed.ErrorCategory);

        var network = await Resend(new StubHandler(_ => throw new HttpRequestException("down for dana@example.com"))).SendAsync(Message, "k", CancellationToken.None);
        Assert.Equal(EmailSendStatus.Failed, network.Status);
        Assert.DoesNotContain("dana", network.ErrorCategory);
    }
}
