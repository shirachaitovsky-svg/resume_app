using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Infrastructure;

/// <summary>Secret key for hashing rate-limit subjects, so raw IPs and emails are never stored.</summary>
public sealed record RateLimitPepper(byte[] Key);

/// <summary>Row shape of <c>rate_limit_counters</c> (schema only; reads and writes use raw SQL).</summary>
public sealed class RateLimitCounter
{
    public string Scope { get; set; } = "";
    public string KeyHash { get; set; } = "";
    public DateTime WindowStart { get; set; }
    public int Count { get; set; }
}

/// <summary>Fixed-window counters in Postgres, updated atomically with INSERT ... ON CONFLICT DO UPDATE ... RETURNING.</summary>
public sealed class RateLimiter(AppDbContext db, TimeProvider clock, RateLimitPepper pepper) : IRateLimiter
{
    public static string HashSubject(string scope, string subject, RateLimitPepper pepper)
        => Convert.ToHexString(HMACSHA256.HashData(pepper.Key, Encoding.UTF8.GetBytes(scope + "\n" + subject))).ToLowerInvariant();

    public async Task<RateLimitResult> HitAsync(string scope, string subject, int limit, TimeSpan window, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var ticks = window.Ticks;
        var windowStart = new DateTime(now.Ticks / ticks * ticks, DateTimeKind.Utc);
        var retryAfter = windowStart + window - now;
        var keyHash = HashSubject(scope, subject, pepper);

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        var opened = false;
        if (conn.State != System.Data.ConnectionState.Open) { await conn.OpenAsync(ct); opened = true; }
        try
        {
            int count;
            await using (var cmd = new NpgsqlCommand(@"
                INSERT INTO rate_limit_counters (scope, key_hash, window_start, count)
                VALUES (@scope, @key, @ws, 1)
                ON CONFLICT (scope, key_hash, window_start) DO UPDATE SET count = rate_limit_counters.count + 1
                RETURNING count", conn))
            {
                cmd.Parameters.AddWithValue("scope", scope);
                cmd.Parameters.AddWithValue("key", keyHash);
                cmd.Parameters.Add(new NpgsqlParameter("ws", NpgsqlDbType.TimestampTz) { Value = windowStart });
                count = (int)(await cmd.ExecuteScalarAsync(ct))!;
            }

            // A new window row was just created: drop this scope's expired windows (keeps the table small).
            // Best effort: a cleanup failure must never turn an already counted request into an error.
            if (count == 1)
            {
                try
                {
                    await using var cleanup = new NpgsqlCommand("DELETE FROM rate_limit_counters WHERE scope = @scope AND window_start < @cutoff", conn);
                    cleanup.Parameters.AddWithValue("scope", scope);
                    cleanup.Parameters.Add(new NpgsqlParameter("cutoff", NpgsqlDbType.TimestampTz) { Value = windowStart - window });
                    await cleanup.ExecuteNonQueryAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Expired rows are removed on a later window rollover.
                }
            }

            return new(count <= limit, count, retryAfter);
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }
}
