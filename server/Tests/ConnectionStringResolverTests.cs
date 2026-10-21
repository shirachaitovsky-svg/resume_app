using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

[Collection("env")]
public class ConnectionStringResolverTests
{
    private static NpgsqlConnectionStringBuilder Parse(string s) => new(s);

    [Fact]
    public void Percent_encoded_password_is_decoded()
    {
        var b = Parse(ConnectionStringResolver.FromUri("postgres://u:p%40ss%2Fw@h:6543/db"));
        Assert.Equal("p@ss/w", b.Password);
        Assert.Equal(6543, b.Port);
        Assert.Equal("u", b.Username);
        Assert.Equal("db", b.Database);
    }

    [Fact]
    public void Missing_port_defaults_to_5432()
    {
        Assert.Equal(5432, Parse(ConnectionStringResolver.FromUri("postgres://u:p@h/db")).Port);
    }

    [Fact]
    public void Postgresql_scheme_is_supported()
    {
        Assert.Equal("h", Parse(ConnectionStringResolver.FromUri("postgresql://u:p@h/db")).Host);
    }

    [Fact]
    public void Key_value_string_passes_through_unchanged()
    {
        const string kv = "Host=h;Database=d;Username=u;Password=p";
        Assert.Equal(kv, ConnectionStringResolver.FromUri(kv));
    }

    [Fact]
    public void Sslmode_query_is_mapped()
    {
        var b = Parse(ConnectionStringResolver.FromUri("postgres://u:p@h/db?sslmode=require"));
        Assert.Equal(SslMode.Require, b.SslMode);
    }

    [Fact]
    public void Resolve_returns_null_when_nothing_is_set()
    {
        var old1 = Environment.GetEnvironmentVariable("DATABASE_URL");
        var old2 = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
        try
        {
            Environment.SetEnvironmentVariable("DATABASE_URL", null);
            Environment.SetEnvironmentVariable("ConnectionStrings__Default", null);
            Assert.Null(ConnectionStringResolver.Resolve(new ConfigurationBuilder().Build()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DATABASE_URL", old1);
            Environment.SetEnvironmentVariable("ConnectionStrings__Default", old2);
        }
    }

    [Fact]
    public async Task Migrate_without_database_throws_InvalidOperationException()
    {
        var old1 = Environment.GetEnvironmentVariable("DATABASE_URL");
        var old2 = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
        try
        {
            Environment.SetEnvironmentVariable("DATABASE_URL", null);
            Environment.SetEnvironmentVariable("ConnectionStrings__Default", null);
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.Sources.Clear();
            builder.Services.AddInfrastructure(builder.Configuration);
            using var host = builder.Build();
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.MigrateDatabaseAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DATABASE_URL", old1);
            Environment.SetEnvironmentVariable("ConnectionStrings__Default", old2);
        }
    }
}
