using System.Data;
using System.Text.RegularExpressions;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services;

public partial class PostUrlService(TemplateDbContext db) : IScopedDependency
{
    public const int MaxSlugLength = 200;
    public const int AliasLifetimeDays = 180;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidSlugRegex();

    public static string BuildUrl(MarkdownDocument document) =>
        string.IsNullOrWhiteSpace(document.Slug)
            ? $"/post/{document.Id}"
            : $"/post/{document.CreationTime:yyyy/MM/dd}/{document.Slug}";

    public static string? Normalize(string? slug) =>
        string.IsNullOrWhiteSpace(slug) ? null : slug.Trim().ToLowerInvariant();

    public static bool IsValid(string? slug) =>
        slug is { Length: > 0 and <= MaxSlugLength } && ValidSlugRegex().IsMatch(slug);

    public async Task<SlugChangeResult> ChangeAsync(
        MarkdownDocument document,
        string? requestedSlug,
        bool confirmHistoricalReuse,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(requestedSlug);
        if (normalized != null && !IsValid(normalized))
            return SlugChangeResult.Invalid;
        if (normalized == document.Slug)
            return SlugChangeResult.Success;

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        var date = document.CreationTime.Date;
        if (normalized != null)
        {
            var occupiedByCurrent = await db.MarkdownDocuments.AnyAsync(
                d => d.Id != document.Id && d.CreationTime.Date == date && d.Slug == normalized,
                cancellationToken);
            var occupiedByAlias = await db.PostSlugAliases.AnyAsync(
                a => a.DocumentId != document.Id && a.PublishedDate == date && a.Slug == normalized && a.ExpiresAt > DateTime.UtcNow,
                cancellationToken);
            if (occupiedByCurrent || occupiedByAlias)
                return SlugChangeResult.Occupied;

            var ownAlias = await db.PostSlugAliases.FirstOrDefaultAsync(
                a => a.DocumentId == document.Id && a.PublishedDate == date && a.Slug == normalized && a.ExpiresAt > DateTime.UtcNow,
                cancellationToken);
            if (ownAlias != null && !confirmHistoricalReuse)
                return SlugChangeResult.ConfirmationRequired;
            if (ownAlias != null)
                db.PostSlugAliases.Remove(ownAlias);
        }

        if (!string.IsNullOrWhiteSpace(document.Slug))
        {
            var retiredAt = DateTime.UtcNow;
            var oldAlias = await db.PostSlugAliases.FirstOrDefaultAsync(
                a => a.DocumentId == document.Id && a.PublishedDate == date && a.Slug == document.Slug,
                cancellationToken);
            if (oldAlias == null)
            {
                db.PostSlugAliases.Add(new PostSlugAlias
                {
                    Id = Guid.NewGuid(), DocumentId = document.Id, PublishedDate = date,
                    Slug = document.Slug, RetiredAt = retiredAt,
                    ExpiresAt = retiredAt.AddDays(AliasLifetimeDays)
                });
            }
            else
            {
                oldAlias.RetiredAt = retiredAt;
                oldAlias.ExpiresAt = retiredAt.AddDays(AliasLifetimeDays);
            }
        }

        document.Slug = normalized;
        document.SlugDate = normalized == null ? null : date;
        await db.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        return SlugChangeResult.Success;
    }
}

public enum SlugChangeResult { Success, Invalid, Occupied, ConfirmationRequired }
