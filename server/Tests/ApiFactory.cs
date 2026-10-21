using Microsoft.AspNetCore.Mvc.Testing;

namespace ResumeApp.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Secret = "test-secret-value";

    public ApiFactory()
    {
        // Minimal-hosting reads configuration before test overrides apply, so use environment variables.
        Environment.SetEnvironmentVariable("ORIGIN_SECRET", Secret);
        Environment.SetEnvironmentVariable("MIGRATE_ON_STARTUP", "false");
    }
}
