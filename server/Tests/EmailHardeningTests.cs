using System.Net;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ResumeApp.Features.Email;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

[Collection("env")]
public class EmailHardeningTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static readonly Env Prod = new("Production");

    private static (string, string?)[] Smtp(params (string, string?)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["EMAIL_PROVIDER"] = "smtp", ["EMAIL_FROM"] = "no-reply@app.test", ["PUBLIC_BASE_URL"] = "https://app.test", ["ADMIN_NOTIFICATION_EMAIL"] = "manager@app.test",
            ["SMTP_HOST"] = "smtp.test", ["SMTP_SECURE"] = "true",
        };
        foreach (var (k, v) in overrides) values[k] = v;
        return values.Select(p => (p.Key, p.Value)).ToArray();
    }

    private static EmailOptions Resolve((string Key, string? Value)[] values, IHostEnvironment? env = null)
        => EmailOptions.Resolve(new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build(), env ?? Prod);

    [Fact]
    public void The_baseline_production_configuration_is_accepted()
        => Assert.Equal("https://app.test", Resolve(Smtp()).PublicBaseUrl);

    [Theory]
    [InlineData("http://app.test")]
    [InlineData("https://app.test/?x=1")]
    [InlineData("https://app.test/#frag")]
    [InlineData("https://app.test?")]
    public void Outside_development_the_base_url_must_be_https_without_query_or_fragment(string url)
        => Assert.Contains("PUBLIC_BASE_URL", Assert.Throws<InvalidOperationException>(() => Resolve(Smtp(("PUBLIC_BASE_URL", url)))).Message);

    [Fact]
    public void Development_still_allows_a_plain_http_base_url()
        => Assert.Equal("http://localhost:4200", Resolve([], new Env("Development")).PublicBaseUrl);

    [Theory]
    [InlineData("not an address")]
    [InlineData("two@a.test, three@b.test")]
    [InlineData("@nope")]
    public void An_unparseable_email_from_fails_at_startup(string from)
        => Assert.Contains("EMAIL_FROM", Assert.Throws<InvalidOperationException>(() => Resolve(Smtp(("EMAIL_FROM", from)))).Message);

    [Fact]
    public void A_display_name_in_email_from_is_accepted()
        => Assert.Equal("App <no-reply@app.test>", Resolve(Smtp(("EMAIL_FROM", "App <no-reply@app.test>"))).From);

    [Fact]
    public void Insecure_smtp_with_credentials_is_rejected_outside_development_but_not_without_credentials()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve(Smtp(("SMTP_SECURE", "false"), ("SMTP_USER", "u"), ("SMTP_PASSWORD", "p"))));
        Assert.Contains("SMTP_SECURE", ex.Message);
        Assert.False(Resolve(Smtp(("SMTP_SECURE", "false"))).Smtp!.Secure);
        Assert.True(Resolve(Smtp(("SMTP_USER", "u"), ("SMTP_PASSWORD", "p"))).Smtp!.Secure);
        Assert.Null(Record.Exception(() => Resolve([("SMTP_USER", "u")], new Env("Development"))));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    public void The_worker_flag_parses_booleans(string? raw, bool expected) => Assert.Equal(expected, EmailOptions.ParseWorkerFlag(raw));

    [Theory]
    [InlineData("flase")]
    [InlineData("2")]
    [InlineData("disabled")]
    public void Any_other_worker_flag_value_fails_startup(string raw)
        => Assert.Contains("EMAIL_WORKER", Assert.Throws<InvalidOperationException>(() => EmailOptions.ParseWorkerFlag(raw)).Message);

    [Theory]
    [InlineData(false, 25, SecureSocketOptions.None)]
    [InlineData(false, 465, SecureSocketOptions.None)]
    [InlineData(true, 465, SecureSocketOptions.SslOnConnect)]
    [InlineData(true, 587, SecureSocketOptions.StartTls)]
    [InlineData(true, 25, SecureSocketOptions.StartTls)]
    public void The_tls_mode_follows_secure_and_port(bool secure, int port, SecureSocketOptions expected)
        => Assert.Equal(expected, SmtpEmailSender.SocketOptions(secure, port));

    [Fact]
    public async Task A_bad_recipient_address_is_a_permanent_smtp_failure()
    {
        var sender = new SmtpEmailSender(
            new EmailOptions(EmailProvider.Smtp, "no-reply@app.test", "https://app.test", new SmtpSettings("127.0.0.1", 1, null, null, false), null),
            NullLogger<SmtpEmailSender>.Instance);

        var result = await sender.SendAsync(new EmailMessage("not an address", "s", "b"), "k", CancellationToken.None);

        Assert.Equal(EmailSendStatus.Permanent, result.Status);
        Assert.Equal("smtp-bad-address", result.ErrorCategory);
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, EmailSendStatus.Permanent)]
    [InlineData(HttpStatusCode.Unauthorized, EmailSendStatus.Permanent)]
    [InlineData(HttpStatusCode.UnprocessableEntity, EmailSendStatus.Permanent)]
    [InlineData(HttpStatusCode.RequestTimeout, EmailSendStatus.Failed)]
    [InlineData(HttpStatusCode.TooManyRequests, EmailSendStatus.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, EmailSendStatus.Failed)]
    [InlineData(HttpStatusCode.OK, EmailSendStatus.Sent)]
    public async Task Resend_maps_status_codes_to_results(HttpStatusCode status, EmailSendStatus expected)
    {
        var sender = new ResendEmailSender(new HttpClient(new StubHandler(status)),
            new EmailOptions(EmailProvider.Resend, "no-reply@app.test", "https://app.test", null, "k"), NullLogger<ResendEmailSender>.Instance);

        var result = await sender.SendAsync(new EmailMessage("a@b.co", "s", "b"), "k", CancellationToken.None);

        Assert.Equal(expected, result.Status);
    }
}
