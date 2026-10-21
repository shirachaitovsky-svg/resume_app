using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ResumeApp.Infrastructure;

/// <summary>Used by `dotnet ef` only. Connection comes from the environment, never from code.</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = ConnectionStringResolver.Resolve(null)
            ?? "Host=localhost;Database=design_time_only";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options;
        return new AppDbContext(options);
    }
}
