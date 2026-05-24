using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services.BackgroundJobs;

/// <summary>
/// Removes <see cref="LocalizedAbstract"/> rows that are no longer meaningful:
/// (1) rows whose parent <see cref="MarkdownDocument"/> has been deleted,
/// (2) rows for cultures no longer in the <see cref="SettingsMap.LocalizationLanguages"/> setting.
///
/// A staleness guard prevents ping-pong with <see cref="GenerateAbstractDocumentsJob"/>.
/// </summary>
public class CleanupAbstractDocumentsJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    ILogger<CleanupAbstractDocumentsJob> logger) : IBackgroundJob
{
    internal static readonly TimeSpan StalenessThreshold = TimeSpan.FromMinutes(10);

    public string Name => "Cleanup Abstracts";

    public string Description =>
        "Removes LocalizedAbstract rows orphaned by deleted documents or by cultures removed from the localization settings.";

    public async Task ExecuteAsync()
    {
        logger.LogInformation("CleanupAbstractDocumentsJob started.");

        var languagesRaw = await settingsService.GetSettingValueAsync(SettingsMap.LocalizationLanguages);
        var configuredCultures = languagesRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();

        var staleCutoff  = DateTime.UtcNow - StalenessThreshold;
        var totalDeleted = 0;

        // 1. Delete LocalizedAbstracts whose parent document no longer exists.
        var existingDocIds = await db.MarkdownDocuments.Select(d => d.Id).ToListAsync();

        var orphaned = await db.LocalizedAbstracts
            .Where(la => !existingDocIds.Contains(la.DocumentId) && la.LastGeneratedAt < staleCutoff)
            .ExecuteDeleteAsync();

        if (orphaned > 0)
        {
            totalDeleted += orphaned;
            logger.LogInformation(
                "CleanupAbstractDocumentsJob: deleted {Count} orphaned row(s) (parent document deleted).",
                orphaned);
        }

        // 2. Delete LocalizedAbstracts for cultures no longer configured.
        if (configuredCultures.Count > 0)
        {
            var staleCulture = await db.LocalizedAbstracts
                .Where(la => !configuredCultures.Contains(la.Culture) && la.LastGeneratedAt < staleCutoff)
                .ExecuteDeleteAsync();

            if (staleCulture > 0)
            {
                totalDeleted += staleCulture;
                logger.LogInformation(
                    "CleanupAbstractDocumentsJob: deleted {Count} row(s) for removed cultures.",
                    staleCulture);
            }
        }

        logger.LogInformation(
            "CleanupAbstractDocumentsJob finished. {Total} row(s) deleted.", totalDeleted);
    }
}
