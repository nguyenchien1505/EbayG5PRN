using EbayGroup5.Application.Disputes;
using Microsoft.Extensions.Options;

namespace EbayGroup5.Web.Disputes;

/// <summary>E04 (DISPUTE_AUTOMATION.md): runs the dispute automation pass at start-up and then every IntervalSeconds on each replica.</summary>
public sealed class DisputeAutomationWorker(IServiceScopeFactory scopes, IOptions<DisputeAutomationOptions> options, ILogger<DisputeAutomationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Validated();
        if (!settings.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));
        try
        {
            do await RunAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IDisputeAutomation>().RunOnceAsync(ct);
            // Counts only: request content never goes to logs.
            if (result.Any) logger.LogInformation("Dispute automation: {Escalated} escalated, {Expired} expired, {Notices} notices.", result.Escalated, result.Expired, result.Notices);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dispute automation pass failed; retrying at the next interval.");
        }
    }
}
