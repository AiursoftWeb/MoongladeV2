using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services.BackgroundJobs;

/// <summary>
/// Periodically generates AI abstracts for public blog posts.
/// Generates an en-US abstract from the full content, then translates it
/// to each other configured language.
/// </summary>
public class GenerateAbstractDocumentsJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    AbstractGenerationService generator,
    DocumentTranslationService translator,
    ILogger<GenerateAbstractDocumentsJob> logger) : IBackgroundJob
{
    private const string SourceCulture = "en-US";

    public string Name => "Generate Abstracts";

    public string Description =>
        "Generates an en-US AI abstract for each public post, then translates it to all configured languages.";

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

        if (!cultures.Contains(SourceCulture))
        {
            logger.LogWarning(
                "GenerateAbstractDocumentsJob: {SourceCulture} not in configured languages ({Languages}). Cannot generate abstracts.",
                SourceCulture, string.Join(", ", cultures));
            return;
        }

        var targetCultures = cultures.Where(c => c != SourceCulture).ToArray();

        logger.LogInformation(
            "GenerateAbstractDocumentsJob: source={Source}, targets={Targets}",
            SourceCulture, string.Join(", ", targetCultures));

        // Phase 1: Generate en-US abstracts from full content.
        var generated = await GenerateSourceAbstractsAsync();
        logger.LogInformation(
            "GenerateAbstractDocumentsJob: generated {Count} en-US abstract(s).", generated);

        // Phase 2: Translate en-US abstracts to every other language.
        var translated = 0;
        foreach (var targetCulture in targetCultures)
        {
            translated += await TranslateAbstractsAsync(targetCulture);
        }

        logger.LogInformation(
            "GenerateAbstractDocumentsJob: done. Generated {Generated}, translated {Translated}.",
            generated, translated);
    }

    private async Task<int> GenerateSourceAbstractsAsync()
    {
        var total = 0;
        var lastId = Guid.Empty;

        while (true)
        {
            var currentLastId = lastId;

            var pending = await db.MarkdownDocuments
                .Where(d => d.IsPublic &&
                            d.Id.CompareTo(currentLastId) > 0 &&
                            !db.LocalizedAbstracts.Any(la =>
                                la.DocumentId == d.Id &&
                                la.Culture == SourceCulture &&
                                la.LastGeneratedAt >= d.UpdatedAt))
                .OrderBy(d => d.Id)
                .Take(20)
                .ToListAsync();

            if (pending.Count == 0) break;

            foreach (var doc in pending)
            {
                if (await GenerateAndSaveAbstractAsync(doc))
                {
                    total++;
                    await db.SaveChangesAsync();
                }
            }

            lastId = pending.Max(d => d.Id);
            logger.LogInformation(
                "GenerateAbstractDocumentsJob: [Source] batch done. Last ID: {LastId}. Total: {Total}.",
                lastId, total);
        }

        return total;
    }

    private async Task<int> TranslateAbstractsAsync(string targetCulture)
    {
        var total = 0;
        var lastId = Guid.Empty;

        while (true)
        {
            var currentLastId = lastId;

            var pending = await db.MarkdownDocuments
                .Where(d => d.IsPublic &&
                            d.Id.CompareTo(currentLastId) > 0 &&
                            db.LocalizedAbstracts.Any(la =>
                                la.DocumentId == d.Id &&
                                la.Culture == SourceCulture &&
                                la.LastGeneratedAt >= d.UpdatedAt) &&
                            !db.LocalizedAbstracts.Any(la =>
                                la.DocumentId == d.Id &&
                                la.Culture == targetCulture &&
                                la.LastGeneratedAt >= d.UpdatedAt))
                .OrderBy(d => d.Id)
                .Take(20)
                .ToListAsync();

            if (pending.Count == 0) break;

            foreach (var doc in pending)
            {
                if (await TranslateAndSaveAsync(doc, targetCulture))
                {
                    total++;
                    await db.SaveChangesAsync();
                }
            }

            lastId = pending.Max(d => d.Id);
            logger.LogInformation(
                "GenerateAbstractDocumentsJob: [{Culture}] batch done. Last ID: {LastId}. Total: {Total}.",
                targetCulture, lastId, total);
        }

        return total;
    }

    private async Task<bool> GenerateAndSaveAbstractAsync(MarkdownDocument doc)
    {
        try
        {
            logger.LogInformation(
                "GenerateAbstractDocumentsJob: generating abstract for '{Title}' (id={Id}) → {Culture}.",
                doc.Title, doc.Id, SourceCulture);

            var abstractText = await generator.GenerateAbstractAsync(doc.Content ?? string.Empty, SourceCulture);

            var existing = await db.LocalizedAbstracts
                .FirstOrDefaultAsync(la => la.DocumentId == doc.Id && la.Culture == SourceCulture);

            if (existing == null)
            {
                db.LocalizedAbstracts.Add(new LocalizedAbstract
                {
                    DocumentId      = doc.Id,
                    Culture         = SourceCulture,
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
                doc.Title, SourceCulture);
            return false;
        }
    }

    private async Task<bool> TranslateAndSaveAsync(MarkdownDocument doc, string targetCulture)
    {
        try
        {
            var sourceAbstract = await db.LocalizedAbstracts
                .Where(la => la.DocumentId == doc.Id && la.Culture == SourceCulture)
                .Select(la => la.Abstract)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(sourceAbstract))
            {
                logger.LogWarning(
                    "GenerateAbstractDocumentsJob: no source abstract for doc {Id}, skipping translation to {Culture}.",
                    doc.Id, targetCulture);
                return false;
            }

            logger.LogInformation(
                "GenerateAbstractDocumentsJob: translating abstract for '{Title}' (id={Id}) {Source} → {Target}.",
                doc.Title, doc.Id, SourceCulture, targetCulture);

            var translatedText = await translator.TranslateAsync(sourceAbstract, targetCulture);

            var existing = await db.LocalizedAbstracts
                .FirstOrDefaultAsync(la => la.DocumentId == doc.Id && la.Culture == targetCulture);

            if (existing == null)
            {
                db.LocalizedAbstracts.Add(new LocalizedAbstract
                {
                    DocumentId      = doc.Id,
                    Culture         = targetCulture,
                    Abstract        = translatedText,
                    LastGeneratedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.Abstract        = translatedText;
                existing.LastGeneratedAt = DateTime.UtcNow;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "GenerateAbstractDocumentsJob: failed to translate abstract for '{Title}' to {Culture}.",
                doc.Title, targetCulture);
            return false;
        }
    }
}
