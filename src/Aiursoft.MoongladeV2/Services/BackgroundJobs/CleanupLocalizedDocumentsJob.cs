using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services.BackgroundJobs;

/// <summary>
/// Removes <see cref="LocalizedDocument"/> rows that are no longer meaningful:
/// (1) rows whose parent <see cref="MarkdownDocument"/> has been deleted,
/// (2) rows for cultures no longer in the <see cref="SettingsMap.LocalizationLanguages"/> setting.
///
/// A staleness guard (<see cref="StalenessThreshold"/>) prevents ping-pong with
/// <see cref="LocalizeDocumentsJob"/>: rows created/updated within the threshold window
/// are left alone so a concurrently-running localize job can finish its current batch.
/// </summary>
public class CleanupLocalizedDocumentsJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    ILogger<CleanupLocalizedDocumentsJob> logger) : IBackgroundJob
{
    internal static readonly TimeSpan StalenessThreshold = TimeSpan.FromMinutes(10);

    public string Name => "Cleanup Localized Documents";

    public string Description =>
        "Removes LocalizedDocument rows that are no longer meaningful: " +
        "(1) rows whose parent MarkdownDocument has been deleted, " +
        "(2) rows for cultures no longer in the LocalizationLanguages setting. " +
        "A 10-minute staleness guard prevents ping-pong with LocalizeDocumentsJob.";

    public async Task ExecuteAsync()
    {
        logger.LogInformation("CleanupLocalizedDocumentsJob started.");

        var languagesRaw = await settingsService.GetSettingValueAsync(SettingsMap.LocalizationLanguages);
        var configuredCultures = languagesRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();

        var staleCutoff  = DateTime.UtcNow - StalenessThreshold;
        var totalDeleted = 0;

        // 1. Delete LocalizedDocuments whose parent MarkdownDocument no longer exists.
        var existingDocIds = await db.MarkdownDocuments
            .Select(d => d.Id)
            .ToListAsync();

        var orphaned = await db.LocalizedDocuments
            .Where(ld => !existingDocIds.Contains(ld.DocumentId) && ld.LastLocalizedAt < staleCutoff)
            .ExecuteDeleteAsync();

        if (orphaned > 0)
        {
            totalDeleted += orphaned;
            logger.LogInformation(
                "CleanupLocalizedDocumentsJob: deleted {Count} orphaned row(s) (parent document deleted).",
                orphaned);
        }

        // 2. Delete LocalizedDocuments for cultures no longer configured.
        if (configuredCultures.Count > 0)
        {
            var staleCulture = await db.LocalizedDocuments
                .Where(ld => !configuredCultures.Contains(ld.Culture) && ld.LastLocalizedAt < staleCutoff)
                .ExecuteDeleteAsync();

            if (staleCulture > 0)
            {
                totalDeleted += staleCulture;
                logger.LogInformation(
                    "CleanupLocalizedDocumentsJob: deleted {Count} row(s) for removed cultures.",
                    staleCulture);
            }
        }

        logger.LogInformation(
            "CleanupLocalizedDocumentsJob finished. {Total} row(s) deleted.", totalDeleted);
    }
}
