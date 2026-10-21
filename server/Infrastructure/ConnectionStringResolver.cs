using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ResumeApp.Infrastructure;

public static class ConnectionStringResolver
{
    /// <summary>
    /// Reads DATABASE_URL (postgres:// URI, as Render provides) or ConnectionStrings:Default
    /// (env var ConnectionStrings__Default). Returns null when neither is set.
    /// </summary>
    public static string? Resolve(IConfiguration? configuration)
    {
        var url = configuration?["DATABASE_URL"] ?? Environment.GetEnvironmentVariable("DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(url))
        {
            return FromUri(url);
        }

        return configuration?.GetConnectionString("Default")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default");
    }

    public static string FromUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            return url; // already a key/value connection string
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port < 0 ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
        };
        var sslmode = System.Web.HttpUtility.ParseQueryString(uri.Query)["sslmode"];
        if (!string.IsNullOrEmpty(sslmode) && Enum.TryParse<SslMode>(sslmode.Replace("-", ""), true, out var mode))
        {
            builder.SslMode = mode;
        }
        return builder.ConnectionString;
    }
}

