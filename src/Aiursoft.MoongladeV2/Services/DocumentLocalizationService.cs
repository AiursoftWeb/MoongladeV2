using Aiursoft.MoongladeV2.Entities;
using Aiursoft.Scanner.Abstractions;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services;

/// <summary>
/// Resolves AI-translated title/content strings for the current request culture.
/// Returns empty dictionaries when the culture is not available, allowing the caller
/// to fall back to original (source-language) content transparently.
/// </summary>
public class DocumentLocalizationService(
    TemplateDbContext db,
    IHttpContextAccessor httpContextAccessor) : IScopedDependency
{
    /// <summary>
    /// Loads localized title and content strings for <paramref name="documents"/>
    /// matching the current request culture (from the culture cookie).
    /// Documents whose SourceCulture matches the current culture are skipped —
    /// the original content is used directly without querying the localized table.
    /// </summary>
    public async Task<(Dictionary<Guid, string> Titles, Dictionary<Guid, string> Contents)>
        LoadLocalizedStringsAsync(IEnumerable<MarkdownDocument> documents)
    {
        var list = documents as List<MarkdownDocument> ?? documents.ToList();
        if (list.Count == 0) return ([], []);

        var culture = CurrentCulture();
        if (string.IsNullOrEmpty(culture)) return ([], []);

        // Exclude documents whose SourceCulture matches the current culture —
        // they don't need translation; caller falls back to original content.
        var idsNeedingTranslation = list
            .Where(d => !string.Equals(d.SourceCulture, culture, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Id)
            .ToList();

        if (idsNeedingTranslation.Count == 0) return ([], []);

        var rows = await db.LocalizedDocuments
            .Where(ld => idsNeedingTranslation.Contains(ld.DocumentId) && ld.Culture == culture)
            .Select(ld => new { ld.DocumentId, ld.LocalizedTitle, ld.LocalizedContent })
            .ToListAsync();

        var titles = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.LocalizedTitle))
            .ToDictionary(r => r.DocumentId, r => r.LocalizedTitle);

        var contents = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.LocalizedContent))
            .ToDictionary(r => r.DocumentId, r => r.LocalizedContent);

        return (titles, contents);
    }

    /// <summary>
    /// Loads AI-generated localized abstracts for <paramref name="documents"/>
    /// matching the current request culture. Falls back to en-US when the
    /// current culture's abstract is not available.
    /// Documents whose SourceCulture matches the current culture are skipped —
    /// the original excerpt is built from the source content directly.
    /// </summary>
    public async Task<Dictionary<Guid, string>> LoadLocalizedAbstractsAsync(
        IEnumerable<MarkdownDocument> documents)
    {
        var list = documents as List<MarkdownDocument> ?? documents.ToList();
        if (list.Count == 0) return [];

        var culture = CurrentCulture();
        if (string.IsNullOrEmpty(culture)) return [];

        // Exclude documents whose SourceCulture matches the current culture —
        // they don't need a localized abstract; caller falls back to BuildExcerpt.
        var idsNeedingTranslation = list
            .Where(d => !string.Equals(d.SourceCulture, culture, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Id)
            .ToList();

        if (idsNeedingTranslation.Count == 0) return [];

        var rows = await db.LocalizedAbstracts
            .Where(la => idsNeedingTranslation.Contains(la.DocumentId) &&
                         (la.Culture == culture || la.Culture == "en-US"))
            .ToListAsync();

        // Prefer current culture, fall back to en-US.
        return idsNeedingTranslation.ToDictionary(
            id => id,
            id => rows
                .Where(r => r.DocumentId == id)
                .OrderBy(r => r.Culture == culture ? 0 : 1)
                .Select(r => r.Abstract)
                .FirstOrDefault() ?? string.Empty);
    }

    private string CurrentCulture() =>
        httpContextAccessor.HttpContext?.Features
            .Get<IRequestCultureFeature>()
            ?.RequestCulture.Culture.Name ?? string.Empty;
}
