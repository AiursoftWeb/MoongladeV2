using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services.BackgroundJobs;

/// <summary>
/// Periodically generates AI abstracts for public blog posts in each configured language.
/// Runs until all pending (document × culture) pairs are up-to-date, saving progress
/// after each document so a crash does not lose completed work.
/// A pair is "pending" when no <see cref="LocalizedAbstract"/> row exists for it,
/// or when <see cref="MarkdownDocument.UpdatedAt"/> is newer than
/// <see cref="LocalizedAbstract.LastGeneratedAt"/>.
/// </summary>
public class GenerateAbstractDocumentsJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    AbstractGenerationService generator,
    ILogger<GenerateAbstractDocumentsJob> logger) : IBackgroundJob
{
    public string Name => "Generate Abstracts";

    public string Description =>
        "Generates AI-powered abstracts for public blog posts in each configured language.";

    public async Task ExecuteAsync()
    {
        if (!await settingsService.IsAiLocalizationEnabledAsync())
        {
            logger.LogInformation("GenerateAbstractDocumentsJob: AI endpoint not configured. Skipping.");
            return;
        }

        var languagesRaw = await settingsService.GetSettingValueAsync(SettingsMap.LocalizationLanguages);
        var cultures = languagesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (cultures.Length == 0)
        {
            logger.LogInformation("GenerateAbstractDocumentsJob: No target languages configured. Skipping.");
            return;
        }

        logger.LogInformation(
            "GenerateAbstractDocumentsJob: starting with {Count} target language(s): {Languages}",
            cultures.Length, string.Join(", ", cultures));

        var totalProcessed = 0;

        foreach (var culture in cultures)
        {
            var lastId = Guid.Empty;
            while (true)
            {
                var currentLastId = lastId;

                var pending = await db.MarkdownDocuments
                    .Where(d => d.IsPublic &&
                                d.Id.CompareTo(currentLastId) > 0 &&
                                !db.LocalizedAbstracts.Any(la =>
                                    la.DocumentId == d.Id &&
                                    la.Culture == culture &&
                                    la.LastGeneratedAt >= d.UpdatedAt))
                    .OrderBy(d => d.Id)
                    .Take(20)
                    .ToListAsync();

                if (pending.Count == 0) break;

                foreach (var doc in pending)
                {
                    var success = await GenerateAbstractAsync(doc, culture);
                    if (success)
                    {
                        totalProcessed++;
                        await db.SaveChangesAsync();
                    }
                }

                lastId = pending.Max(d => d.Id);
                logger.LogInformation(
                    "GenerateAbstractDocumentsJob: [{Culture}] batch done. Last ID: {LastId}. Total so far: {Total}.",
                    culture, lastId, totalProcessed);
            }

            logger.LogInformation("GenerateAbstractDocumentsJob: [{Culture}] all documents up-to-date.", culture);
        }

        logger.LogInformation("GenerateAbstractDocumentsJob: done. Processed {Count} pair(s) this run.", totalProcessed);
    }

    private async Task<bool> GenerateAbstractAsync(MarkdownDocument doc, string culture)
    {
        try
        {
            logger.LogInformation(
                "GenerateAbstractDocumentsJob: generating abstract for '{Title}' (id={Id}) → {Culture}.",
                doc.Title, doc.Id, culture);

            var abstractText = await generator.GenerateAbstractAsync(doc.Content ?? string.Empty, culture);

            var existing = await db.LocalizedAbstracts
                .FirstOrDefaultAsync(la => la.DocumentId == doc.Id && la.Culture == culture);

            if (existing == null)
            {
                db.LocalizedAbstracts.Add(new LocalizedAbstract
                {
                    DocumentId      = doc.Id,
                    Culture         = culture,
                    Abstract        = abstractText,
                    LastGeneratedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.Abstract        = abstractText;
                existing.LastGeneratedAt = DateTime.UtcNow;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "GenerateAbstractDocumentsJob: failed to generate abstract for '{Title}' → {Culture}.",
                doc.Title, culture);
            return false;
        }
    }
}
