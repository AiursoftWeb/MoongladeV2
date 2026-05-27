using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services.BackgroundJobs;

/// <summary>
/// **Manual-only.** Deletes ALL <see cref="LocalizedDocument"/> rows.
/// Use this when you have changed the LocalizationLanguages setting
/// and want to force a clean-slate retranslation of every document.
/// This job does not run on a schedule — trigger it manually from the admin UI.
///
/// Behavior:
/// - All LocalizedDocument rows → Purged. Documents re-enter the
///   localization pipeline on the next LocalizeDocumentsJob run.
/// </summary>
public class PurgeLocalizedDocumentsJob(
    TemplateDbContext db,
    ILogger<PurgeLocalizedDocumentsJob> logger) : IBackgroundJob
{
    public string Name => "Purge Localized Documents";

    public string Description =>
        "**Manual-only.** Deletes ALL LocalizedDocument rows. " +
        "Use this when you have changed the LocalizationLanguages setting " +
        "and want to force a clean-slate retranslation of every document. " +
        "This job does not run on a schedule — trigger it manually from the admin UI.";

    public async Task ExecuteAsync()
    {
        var deleted = await db.LocalizedDocuments.ExecuteDeleteAsync();
        logger.LogInformation(
            "PurgeLocalizedDocumentsJob: deleted {Count} LocalizedDocument row(s).", deleted);
    }
}
