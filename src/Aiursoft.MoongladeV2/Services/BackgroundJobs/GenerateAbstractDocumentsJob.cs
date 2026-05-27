using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services.BackgroundJobs;

/// <summary>
/// Generates AI abstracts for public blog posts.
///
/// SourceCulture routing:
/// - SourceCulture is null     → Skip (pending detection by DetectSourceCultureJob)
/// - SourceCulture is set      → Phase 1: generate abstract in SourceCulture from full content
///                               Phase 2: translate that abstract to every other configured language
///
/// The SourceCulture abstract is always created from scratch via the AI; target-language
/// abstracts are always translations of the SourceCulture abstract.
///
/// Staleness is tracked per (document × culture) pair against
/// <see cref="MarkdownDocument.UpdatedAt"/>.
/// </summary>
public class GenerateAbstractDocumentsJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    AbstractGenerationService generator,
    DocumentTranslationService translator,
    ILogger<GenerateAbstractDocumentsJob> logger) : IBackgroundJob
{
    public string Name => "Generate Abstracts";

    public string Description =>
        "Generates AI abstracts for public blog posts. " +
        "Documents whose SourceCulture is null are skipped. " +
        "An abstract is first generated in the document's SourceCulture from the full content. " +
        "It is then translated to every other configured language via DocumentTranslationService. " +
        "The SourceCulture abstract is always created from scratch; " +
        "target-language abstracts are always translations of the SourceCulture abstract. " +
        "Staleness is tracked per (document × culture) pair against MarkdownDocument.UpdatedAt.";

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

        // Phase 1: Generate abstracts in each document's SourceCulture.
        var generated = await GenerateSourceAbstractsAsync(cultures.ToHashSet());
        logger.LogInformation(
            "GenerateAbstractDocumentsJob: generated {Count} source-culture abstract(s).", generated);

        // Phase 2: Translate source abstracts to every other configured language.
        var translated = await TranslateAbstractsAsync(cultures.ToHashSet());
        logger.LogInformation(
            "GenerateAbstractDocumentsJob: translated {Count} abstract(s) this run.", translated);
    }

    private async Task<int> GenerateSourceAbstractsAsync(HashSet<string> configuredCultures)
    {
        var total = 0;
        var lastId = Guid.Empty;

        while (true)
        {
            var currentLastId = lastId;

            var pending = await db.MarkdownDocuments
                .Where(d => d.IsPublic &&
                            d.SourceCulture != null &&
                            d.Id.CompareTo(currentLastId) > 0 &&
                            configuredCultures.Contains(d.SourceCulture!) &&
                            !db.LocalizedAbstracts.Any(la =>
                                la.DocumentId == d.Id &&
                                la.Culture == d.SourceCulture &&
                                la.LastGeneratedAt >= d.UpdatedAt))
                .OrderBy(d => d.Id)
                .Take(20)
                .ToListAsync();

            if (pending.Count == 0) break;

            foreach (var doc in pending)
            {
                if (await GenerateAndSaveAsync(doc))
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

    private async Task<int> TranslateAbstractsAsync(HashSet<string> configuredCultures)
    {
        var total = 0;

        // Get all distinct source cultures that exist in the database
        var sourceCultures = await db.MarkdownDocuments
            .Where(d => d.IsPublic && d.SourceCulture != null)
            .Select(d => d.SourceCulture)
            .Distinct()
            .ToListAsync();

        foreach (var sourceCulture in sourceCultures)
        {
            if (sourceCulture == null) continue;

            var targetCultures = configuredCultures
                .Where(c => !string.Equals(c, sourceCulture, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var targetCulture in targetCultures)
            {
                total += await TranslateAbstractsToCultureAsync(sourceCulture, targetCulture);
            }
        }

        return total;
    }

    private async Task<int> TranslateAbstractsToCultureAsync(string sourceCulture, string targetCulture)
    {
        var total = 0;
        var lastId = Guid.Empty;

        while (true)
        {
            var currentLastId = lastId;

            var pending = await db.MarkdownDocuments
                .Where(d => d.IsPublic &&
                            d.SourceCulture == sourceCulture &&
                            d.Id.CompareTo(currentLastId) > 0 &&
                            db.LocalizedAbstracts.Any(la =>
                                la.DocumentId == d.Id &&
                                la.Culture == sourceCulture &&
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
                if (await TranslateAndSaveAsync(doc, sourceCulture, targetCulture))
                {
                    total++;
                    await db.SaveChangesAsync();
                }
            }

            lastId = pending.Max(d => d.Id);
        }

        return total;
    }

    private async Task<bool> GenerateAndSaveAsync(MarkdownDocument doc)
    {
        try
        {
            var sourceCulture = doc.SourceCulture!;
            logger.LogInformation(
                "GenerateAbstractDocumentsJob: generating abstract for '{Title}' (source={SourceCulture}).",
                doc.Title, sourceCulture);

            var abstractText = await generator.GenerateAbstractAsync(doc.Content ?? string.Empty, sourceCulture);

            var existing = await db.LocalizedAbstracts
                .FirstOrDefaultAsync(la => la.DocumentId == doc.Id && la.Culture == sourceCulture);

            if (existing == null)
            {
                db.LocalizedAbstracts.Add(new LocalizedAbstract
                {
                    DocumentId      = doc.Id,
                    Culture         = sourceCulture,
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
                "GenerateAbstractDocumentsJob: failed for '{Title}'.", doc.Title);
            return false;
        }
    }

    private async Task<bool> TranslateAndSaveAsync(MarkdownDocument doc,
        string sourceCulture, string targetCulture)
    {
        try
        {
            var sourceAbstract = await db.LocalizedAbstracts
                .Where(la => la.DocumentId == doc.Id && la.Culture == sourceCulture)
                .Select(la => la.Abstract)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(sourceAbstract))
            {
                logger.LogWarning(
                    "GenerateAbstractDocumentsJob: no source abstract for doc {Id} ({Culture}), skipping translation to {Target}.",
                    doc.Id, sourceCulture, targetCulture);
                return false;
            }

            logger.LogInformation(
                "GenerateAbstractDocumentsJob: translating abstract for '{Title}' {Source} → {Target}.",
                doc.Title, sourceCulture, targetCulture);

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
                "GenerateAbstractDocumentsJob: failed to translate for '{Title}' to {Culture}.",
                doc.Title, targetCulture);
            return false;
        }
    }
}
