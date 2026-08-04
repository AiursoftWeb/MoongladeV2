using Aiursoft.MoongladeV2.Configuration;
using Microsoft.Extensions.Options;

namespace Aiursoft.MoongladeV2.Services;

public sealed class ViewCountArchiveService(
    ViewCountService viewCounts,
    IOptions<ViewCountOptions> options,
    ILogger<ViewCountArchiveService> logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await viewCounts.InitializeAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = options.Value.ArchivePeriod;
        if (period <= TimeSpan.Zero)
        {
            logger.LogWarning("View count archiving is disabled because ArchivePeriod is not positive.");
            return;
        }

        using var timer = new PeriodicTimer(period);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await viewCounts.ArchiveAsync(stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            await viewCounts.ArchiveAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("The final view count archive was cancelled by the host shutdown timeout.");
        }
    }
}
