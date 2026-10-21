using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ResumeApp.Features.Email;

namespace ResumeApp.Infrastructure;

/// <summary>Background loop over the email outbox (AD-4). Safe to run in several instances: rows are claimed with SKIP LOCKED.</summary>
public sealed class EmailOutboxWorker(IServiceScopeFactory scopes, ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = PollInterval;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
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
