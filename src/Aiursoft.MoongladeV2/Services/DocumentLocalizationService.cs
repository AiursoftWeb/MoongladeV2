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
    /// </summary>
    public async Task<(Dictionary<Guid, string> Titles, Dictionary<Guid, string> Contents)>
        LoadLocalizedStringsAsync(IEnumerable<MarkdownDocument> documents)
    {
        var list = documents as List<MarkdownDocument> ?? documents.ToList();
        if (list.Count == 0) return ([], []);

        var culture = CurrentCulture();
        if (string.IsNullOrEmpty(culture)) return ([], []);

        var ids = list.Select(d => d.Id).ToList();

        var rows = await db.LocalizedDocuments
            .Where(ld => ids.Contains(ld.DocumentId) && ld.Culture == culture)
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
    /// matching the current request culture. Falls back to empty when culture is unavailable.
    /// </summary>
    public async Task<Dictionary<Guid, string>> LoadLocalizedAbstractsAsync(
        IEnumerable<MarkdownDocument> documents)
    {
        var list = documents as List<MarkdownDocument> ?? documents.ToList();
        if (list.Count == 0) return [];

        var culture = CurrentCulture();
        if (string.IsNullOrEmpty(culture)) return [];

        var ids = list.Select(d => d.Id).ToList();

        return await db.LocalizedAbstracts
            .Where(la => ids.Contains(la.DocumentId) && la.Culture == culture && la.Abstract != "")
            .ToDictionaryAsync(la => la.DocumentId, la => la.Abstract);
    }

    private string? CurrentCulture() =>
        httpContextAccessor.HttpContext?.Features
            .Get<IRequestCultureFeature>()
            ?.RequestCulture.Culture.Name;
}
