using Microsoft.AspNetCore.HttpOverrides;
using ResumeApp.Api.Endpoints;
using ResumeApp.Api.Security;
using ResumeApp.Infrastructure;
using ResumeApp.Infrastructure.Identity;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) =>
{
    logger.ReadFrom.Configuration(context.Configuration).WriteTo.Console();
    // Test seam: sinks registered in DI also receive every event.
    foreach (var sink in services.GetServices<Serilog.Core.ILogEventSink>()) logger.WriteTo.Sink(sink);
});

// Secrets and connections come from environment variables only. Fail fast and clearly when missing.
var originSecret = builder.Configuration["ORIGIN_SECRET"];
if (string.IsNullOrWhiteSpace(originSecret))
{
    throw new InvalidOperationException("Missing required environment variable ORIGIN_SECRET.");
}

builder.Services.AddSingleton(new OriginSecretOptions(originSecret));
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddPublicFormProtection(builder.Configuration, builder.Environment);
builder.Services.AddEmail(builder.Configuration, builder.Environment);
builder.Services.AddGoogleSignIn(builder.Configuration, builder.Environment);
builder.Services.AddScoped<PublicFormGuard>();
builder.Services.AddAdminSecurity();
// Every error, in every environment, is problem+json with a generic title and a traceId (never a stack trace or the exception message).
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();

var app = builder.Build();

// First in the pipeline, so the headers added by everything after it (no-store included) also apply to error responses.
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<OriginSecretMiddleware>();
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
};
// Render's proxy address is not known in advance; the shared secret check above gates trust.
forwardedOptions.KnownIPNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);
app.UseNoStoreForAuth();

app.UseAuthentication();
app.UseAuthorization();
app.UseMustChangePasswordGate();
app.UseMiddleware<RequestIntegrityMiddleware>();

await app.MigrateDatabaseAsync();
await app.SeedAdminAsync();

app.MapHealth();
app.MapAuth();
app.MapAdmin();
app.MapCohorts();
app.MapProfiles();
app.MapParticipant();
app.MapGoogleAuth();
app.MapContactRequests();
app.MapContactRequestAdmin();
app.MapEmailOutbox();
app.MapAuditLog();
app.MapSubmissions();
app.MapProfileAdmin();
app.MapGallery();

app.Run();

public partial class Program;

