using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services.BackgroundJobs;

/// <summary>
/// **Manual-only.** Deletes ALL <see cref="LocalizedAbstract"/> rows.
/// Use this when you want to force regeneration of all AI abstracts
/// (e.g. after changing the AI model or SourceCulture detection).
/// This job does not run on a schedule — trigger it manually from the admin UI.
///
/// Behavior:
/// - All LocalizedAbstract rows → Purged. Documents re-enter the
///   abstract generation pipeline on the next GenerateAbstractDocumentsJob run.
/// </summary>
public class PurgeLocalizedAbstractsJob(
    TemplateDbContext db,
    ILogger<PurgeLocalizedAbstractsJob> logger) : IBackgroundJob
{
    public string Name => "Purge Localized Abstracts";

    public string Description =>
        "**Manual-only.** Deletes ALL LocalizedAbstract rows. " +
        "Use this when you want to force regeneration of all AI abstracts " +
        "(e.g. after changing the AI model or SourceCulture detection). " +
        "This job does not run on a schedule — trigger it manually from the admin UI.";

    public async Task ExecuteAsync()
    {
        var deleted = await db.LocalizedAbstracts.ExecuteDeleteAsync();
        logger.LogInformation(
            "PurgeLocalizedAbstractsJob: deleted {Count} LocalizedAbstract row(s).", deleted);
    }
}
