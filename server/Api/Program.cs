using Microsoft.AspNetCore.HttpOverrides;
using ResumeApp.Api.Endpoints;
using ResumeApp.Api.Security;
using ResumeApp.Infrastructure;
using ResumeApp.Infrastructure.Identity;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console());

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
builder.Services.AddScoped<PublicFormGuard>();
builder.Services.AddAdminSecurity();
builder.Services.AddProblemDetails();

var app = builder.Build();

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
app.MapEditLink();
app.MapEmailOutbox();
app.MapSubmissions();
app.MapGallery();

app.Run();

public partial class Program;

