using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services.BackgroundJobs;

/// <summary>
/// Periodically translates public blog posts into the configured target languages.
/// Runs until all pending (document × culture) pairs are up-to-date, saving progress
/// after each document so a crash does not lose completed work.
/// A pair is "pending" when no <see cref="LocalizedDocument"/> row exists for it,
/// or when <see cref="MarkdownDocument.UpdatedAt"/> is newer than
/// <see cref="LocalizedDocument.LastLocalizedAt"/>.
/// </summary>
public class LocalizeDocumentsJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    DocumentTranslationService translator,
    ILogger<LocalizeDocumentsJob> logger) : IBackgroundJob
{
    public string Name => "Localize Documents";

    public string Description =>
        "Translates public blog posts into configured languages using an AI endpoint (Ollama / OpenAI-compatible).";

    public async Task ExecuteAsync()
    {
        if (!await settingsService.IsAiLocalizationEnabledAsync())
        {
            logger.LogInformation("LocalizeDocumentsJob: AI chat endpoint not configured. Skipping.");
            return;
        }

        var languagesRaw = await settingsService.GetSettingValueAsync(SettingsMap.LocalizationLanguages);
        var cultures = languagesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (cultures.Length == 0)
        {
            logger.LogInformation("LocalizeDocumentsJob: No target languages configured. Skipping.");
            return;
        }

        logger.LogInformation(
            "LocalizeDocumentsJob: starting with {Count} target language(s): {Languages}",
            cultures.Length, string.Join(", ", cultures));

        var totalProcessed = 0;

        foreach (var culture in cultures)
        {
            var lastId = Guid.Empty;
            while (true)
            {
                var currentLastId = lastId;

                // Only translate public documents that are stale for this culture.
                var pending = await db.MarkdownDocuments
                    .Where(d => d.IsPublic &&
                                string.Compare(d.Id.ToString(), currentLastId.ToString(), StringComparison.Ordinal) > 0 &&
                                !db.LocalizedDocuments.Any(ld =>
                                    ld.DocumentId == d.Id &&
                                    ld.Culture == culture &&
                                    ld.LastLocalizedAt >= d.UpdatedAt))
                    .OrderBy(d => d.Id)
                    .Take(20)
                    .ToListAsync();

                if (pending.Count == 0) break;

                foreach (var doc in pending)
                {
                    var success = await LocalizeDocumentAsync(doc, culture);
                    if (success)
                    {
                        totalProcessed++;
                        await db.SaveChangesAsync();
                    }
                }

                lastId = pending.Max(d => d.Id);
                logger.LogInformation(
                    "LocalizeDocumentsJob: [{Culture}] batch done. Last ID: {LastId}. Total so far: {Total}.",
                    culture, lastId, totalProcessed);
            }

            logger.LogInformation("LocalizeDocumentsJob: [{Culture}] all documents up-to-date.", culture);
        }

        logger.LogInformation("LocalizeDocumentsJob: done. Processed {Count} pair(s) this run.", totalProcessed);
    }

    private async Task<bool> LocalizeDocumentAsync(MarkdownDocument doc, string culture)
    {
        try
        {
            logger.LogInformation(
                "LocalizeDocumentsJob: translating '{Title}' (id={Id}) → {Culture}.",
                doc.Title, doc.Id, culture);

            // Translate title and content in parallel.
            var titleTask   = translator.TranslateAsync(doc.Title   ?? string.Empty, culture);
            var contentTask = translator.TranslateAsync(doc.Content ?? string.Empty, culture);
            await Task.WhenAll(titleTask, contentTask);

            var existing = await db.LocalizedDocuments
                .FirstOrDefaultAsync(ld => ld.DocumentId == doc.Id && ld.Culture == culture);

            if (existing == null)
            {
                db.LocalizedDocuments.Add(new LocalizedDocument
                {
                    DocumentId       = doc.Id,
                    Culture          = culture,
                    LocalizedTitle   = await titleTask,
                    LocalizedContent = await contentTask,
                    LastLocalizedAt  = DateTime.UtcNow
                });
            }
            else
            {
                existing.LocalizedTitle   = await titleTask;
                existing.LocalizedContent = await contentTask;
                existing.LastLocalizedAt  = DateTime.UtcNow;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "LocalizeDocumentsJob: failed to localize '{Title}' to {Culture}.",
                doc.Title, culture);
            return false;
        }
    }
}
