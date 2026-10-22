using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ResumeApp.Features.Email;

namespace ResumeApp.Infrastructure;

/// <summary>Background loop over the email outbox (AD-4). Safe to run in several instances: rows are claimed with SKIP LOCKED.</summary>
public sealed class EmailOutboxWorker(IServiceScopeFactory scopes, Microsoft.Extensions.Configuration.IConfiguration configuration, ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan DefaultSweepInterval = TimeSpan.FromMinutes(1);

    private long? _lastSweep;

    /// <summary>At most one expiry sweep per interval per instance (CONTACT_SWEEP_INTERVAL_SECONDS, default 60). The first cycle sweeps at once.</summary>
    private TimeSpan SweepInterval => int.TryParse(configuration["CONTACT_SWEEP_INTERVAL_SECONDS"], out var s) && s > 0 ? TimeSpan.FromSeconds(s) : DefaultSweepInterval;


    /// <summary>Story 3.1 (AD-13): once per cycle, contact requests whose verification window passed become expired. A failure never blocks sending.</summary>
    private async Task SweepExpiredRequestsAsync(IServiceProvider services, CancellationToken ct)
    {
        if (_lastSweep is { } last && System.Diagnostics.Stopwatch.GetElapsedTime(last) < SweepInterval) return;
        _lastSweep = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await services.GetRequiredService<ResumeApp.Features.Contacts.ContactRequestService>().ExpireStaleAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Contact request expiry sweep failed: {ErrorType}", ex.GetType().Name);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = PollInterval;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await SweepExpiredRequestsAsync(scope.ServiceProvider, stoppingToken);
                var processor = scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>();
                // A full batch means there is probably more work: go again without sleeping.
                if (await processor.ProcessBatchAsync(stoppingToken) > 0) delay = TimeSpan.Zero;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Email outbox pass failed: {ErrorType}", ex.GetType().Name);
                delay = ErrorDelay;
            }

            try
            {
                if (delay > TimeSpan.Zero) await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
