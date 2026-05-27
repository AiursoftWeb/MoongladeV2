using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services.BackgroundJobs;

/// <summary>
/// Translates public blog posts into configured target languages.
///
/// SourceCulture routing:
/// - SourceCulture is null            → Skip (pending detection by DetectSourceCultureJob)
/// - targetCulture == SourceCulture   → Pass-through (copy original content, no AI call)
/// - targetCulture != SourceCulture   → Translate (call AI to translate)
///
/// Staleness is tracked per (document × culture) pair against
/// <see cref="MarkdownDocument.UpdatedAt"/>.
/// </summary>
public class LocalizeDocumentsJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    DocumentTranslationService translator,
    ILogger<LocalizeDocumentsJob> logger) : IBackgroundJob
{
    public string Name => "Localize Documents";

    public string Description =>
        "Translates public blog posts into all configured languages. " +
        "Documents whose SourceCulture is null are skipped (pending language detection). " +
        "When the target language matches the document's SourceCulture, " +
        "the original content is copied through without calling AI. " +
        "When they differ, the content is translated via the configured AI endpoint. " +
        "Staleness is tracked per (document × culture) pair against MarkdownDocument.UpdatedAt.";

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

                var pending = await db.MarkdownDocuments
                    .Where(d => d.IsPublic &&
                                d.SourceCulture != null &&
                                d.Id.CompareTo(currentLastId) > 0 &&
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
            // Pass-through: same language — copy original content, no AI call.
            if (string.Equals(doc.SourceCulture, culture, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(
                    "LocalizeDocumentsJob: pass-through '{Title}' (source={SourceCulture}, target={TargetCulture}).",
                    doc.Title, doc.SourceCulture, culture);

                await SaveLocalizedAsync(doc, culture,
                    doc.Title ?? string.Empty, doc.Content ?? string.Empty);
                return true;
            }

            // Translate: different language — call AI.
            logger.LogInformation(
                "LocalizeDocumentsJob: translating '{Title}' (source={SourceCulture} → {TargetCulture}).",
                doc.Title, doc.SourceCulture, culture);

            var titleTask   = translator.TranslateAsync(doc.Title   ?? string.Empty, culture);
            var contentTask = translator.TranslateAsync(doc.Content ?? string.Empty, culture);
            await Task.WhenAll(titleTask, contentTask);

            await SaveLocalizedAsync(doc, culture, await titleTask, await contentTask);
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

    private async Task SaveLocalizedAsync(MarkdownDocument doc, string culture,
        string title, string content)
    {
        var existing = await db.LocalizedDocuments
            .FirstOrDefaultAsync(ld => ld.DocumentId == doc.Id && ld.Culture == culture);

        if (existing == null)
        {
            db.LocalizedDocuments.Add(new LocalizedDocument
            {
                DocumentId       = doc.Id,
                Culture          = culture,
                LocalizedTitle   = title,
                LocalizedContent = content,
                LastLocalizedAt  = DateTime.UtcNow
            });
        }
        else
        {
            existing.LocalizedTitle   = title;
            existing.LocalizedContent = content;
            existing.LastLocalizedAt  = DateTime.UtcNow;
        }
    }
}
