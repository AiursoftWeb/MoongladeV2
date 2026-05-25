using System.Text.RegularExpressions;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Models.BlogViewModels;
using Aiursoft.MoongladeV2.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Controllers;

[LimitPerMin]
public class BlogController(
    TemplateDbContext dbContext,
    MoongladeV2Service moongladeV2Service,
    DocumentLocalizationService localizationService,
    DocumentVectorSearchService vectorSearch,
    SearchRateLimiter rateLimiter) : Controller
{
    private const int PageSize = 10;

    [HttpGet("/")]
    public async Task<IActionResult> Index([FromQuery] int page = 1, [FromQuery] string sort = "recent")
    {
        return await RenderIndexPageAsync(page: page, sort: sort, query: null, tag: null);
    }

    [HttpGet("/search")]
    public async Task<IActionResult> Search([FromQuery(Name = "q")] string? query, [FromQuery] int page = 1, [FromQuery] string sort = "recent")
    {
        return await RenderIndexPageAsync(page: page, sort: sort, query: query, tag: null);
    }

    [HttpGet("/tags")]
    public async Task<IActionResult> Tags()
    {
        var rawTags = await dbContext.MarkdownDocuments
            .AsNoTracking()
            .Where(d => d.IsPublic && d.Tags != null)
            .Select(d => d.Tags!)
            .ToListAsync();

        var model = new TagsViewModel
        {
            Tags = rawTags
                .SelectMany(BlogTagParser.ParseTags)
                .GroupBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => new TagCountViewModel
                {
                    Tag = g.Key,
                    Count = g.Count()
                })
                .ToArray()
        };
        return this.SimpleView(model);
    }

    [HttpGet("/tags/{tag}")]
    public async Task<IActionResult> Tag([FromRoute] string tag, [FromQuery] int page = 1, [FromQuery] string sort = "recent")
    {
        return await RenderIndexPageAsync(page: page, sort: sort, query: null, tag: tag);
    }

    [HttpGet("/archive")]
    public async Task<IActionResult> Archive()
    {
        var documents = await dbContext.MarkdownDocuments
            .AsNoTracking()
            .Where(d => d.IsPublic)
            .OrderByDescending(d => d.CreationTime)
            .ToListAsync();

        var (localizedTitles, _) = await localizationService.LoadLocalizedStringsAsync(documents);

        var months = documents
            .GroupBy(d => new DateTime(d.CreationTime.Year, d.CreationTime.Month, 1))
            .OrderByDescending(g => g.Key)
            .Select(group => new ArchiveMonthViewModel
            {
                Label = group.Key.ToString("yyyy MMM"),
                Posts = group.Select(d => new BlogPostSummaryViewModel
                {
                    Id = d.Id,
                    Slug = d.Slug,
                    Url = BuildPostUrl(d),
                    Title = localizedTitles.TryGetValue(d.Id, out var localizedTitle)
                        ? localizedTitle
                        : d.Title ?? "Untitled",
                    PublishedAt = d.CreationTime,
                    IsFeatured = d.IsFeatured,
                    HeroImageUrl = d.HeroImageUrl,
                    Tags = BlogTagParser.ParseTags(d.Tags)
                }).ToArray()
            })
            .ToArray();

        var model = new ArchiveViewModel
        {
            TotalPosts = documents.Count,
            Months = months
        };
        return this.SimpleView(model);
    }

    [HttpGet("/post/{slug}")]
    public async Task<IActionResult> Post([FromRoute] string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return NotFound();
        }

        var document = await dbContext.MarkdownDocuments
            .AsNoTracking()
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.IsPublic && d.Slug == slug);

        return await BuildPostResultAsync(document);
    }

    [HttpGet("/post/{id:guid}")]
    public async Task<IActionResult> PostById([FromRoute] Guid id)
    {
        var document = await dbContext.MarkdownDocuments
            .AsNoTracking()
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.IsPublic && d.Id == id);

        return await BuildPostResultAsync(document);
    }

    private async Task<IActionResult> BuildPostResultAsync(MarkdownDocument? document)
    {
        if (document == null)
        {
            return NotFound("The post was not found.");
        }

        var (localizedTitles, localizedContents) = await localizationService.LoadLocalizedStringsAsync([document]);
        var title = localizedTitles.TryGetValue(document.Id, out var localizedTitle)
            ? localizedTitle
            : document.Title ?? "Untitled";
        var markdownContent = localizedContents.TryGetValue(document.Id, out var localizedContent)
            ? localizedContent
            : document.Content ?? string.Empty;

        var model = new PostViewModel
        {
            PageTitle = title,
            Title = title,
            AuthorName = !string.IsNullOrWhiteSpace(document.User.DisplayName)
                ? document.User.DisplayName
                : document.User.UserName ?? "Unknown Author",
            PublishedAt = document.CreationTime,
            HeroImageUrl = document.HeroImageUrl,
            ContentHtml = moongladeV2Service.ConvertMarkdownToHtml(markdownContent),
            Tags = BlogTagParser.ParseTags(document.Tags)
        };
        return this.SimpleView(model, viewName: nameof(Post));
    }

    private async Task<IActionResult> RenderIndexPageAsync(int page, string sort, string? query, string? tag)
    {
        var normalizedSort = string.Equals(sort, "featured", StringComparison.OrdinalIgnoreCase)
            ? "Featured"
            : "Recent";
        var normalizedTag = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim();
        var normalizedQuery = string.IsNullOrWhiteSpace(query) ? null : query.Trim();

        // ── Vector search path (only when no tag filter is active) ──────────
        List<MarkdownDocument>? aiResults = null;
        var usedAi = false;
        var rateLimited = false;
        var aiTotalCount = 0;

        if (!string.IsNullOrWhiteSpace(normalizedQuery) && string.IsNullOrWhiteSpace(normalizedTag))
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            if (!rateLimiter.TryConsume(ip))
            {
                rateLimited = true;
            }
            else
            {
                var baseQuery = dbContext.MarkdownDocuments
                    .AsNoTracking()
                    .Where(d => d.IsPublic);

                var aiResult = await vectorSearch.SearchAsync(
                    baseQuery, normalizedQuery, page, PageSize);

                if (aiResult.UsedAi)
                {
                    usedAi = true;
                    aiResults = aiResult.Results;
                    aiTotalCount = aiResult.TotalCount;
                }
            }
        }

        List<MarkdownDocument> pagedPosts;
        int totalPosts;
        List<MarkdownDocument> allFiltered; // used for computing top tags

        if (aiResults != null)
        {
            pagedPosts = aiResults;
            totalPosts = aiTotalCount;
            allFiltered = aiResults;
        }
        else
        {
            var documents = await dbContext.MarkdownDocuments
                .AsNoTracking()
                .Where(d => d.IsPublic)
                .ToListAsync();

            var filtered = documents.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(normalizedTag))
            {
                filtered = filtered.Where(d => BlogTagParser.ContainsTag(d.Tags, normalizedTag));
            }

            if (!string.IsNullOrWhiteSpace(normalizedQuery))
            {
                filtered = filtered.Where(d =>
                    (d.Title?.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (d.Content?.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (d.Tags?.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            filtered = normalizedSort switch
            {
                "Featured" => filtered
                    .OrderByDescending(d => d.IsFeatured)
                    .ThenByDescending(d => d.CreationTime),
                _ => filtered
                    .OrderByDescending(d => d.CreationTime)
            };

            allFiltered = filtered.ToList();
            totalPosts = allFiltered.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalPosts / (double)PageSize));
            var currentPage = Math.Max(1, Math.Min(page, totalPages));
            pagedPosts = allFiltered
                .Skip((currentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();
        }

        var totalPagesFinal = Math.Max(1, (int)Math.Ceiling(totalPosts / (double)PageSize));
        var currentPageFinal = Math.Max(1, Math.Min(page, totalPagesFinal));

        var (localizedTitles, localizedContents) = await localizationService.LoadLocalizedStringsAsync(pagedPosts);
        var localizedAbstracts = await localizationService.LoadLocalizedAbstractsAsync(pagedPosts);

        var postCards = pagedPosts
            .Select(d =>
            {
                var content = localizedContents.TryGetValue(d.Id, out var localizedContent)
                    ? localizedContent
                    : d.Content ?? string.Empty;
                var title = localizedTitles.TryGetValue(d.Id, out var localizedTitle)
                    ? localizedTitle
                    : d.Title ?? "Untitled";
                var excerpt = localizedAbstracts.TryGetValue(d.Id, out var localizedAbstract)
                    ? localizedAbstract
                    : BuildExcerpt(content);

                return new BlogPostSummaryViewModel
                {
                    Id = d.Id,
                    Slug = d.Slug,
                    Url = BuildPostUrl(d),
                    Title = title,
                    Excerpt = excerpt,
                    PublishedAt = d.CreationTime,
                    IsFeatured = d.IsFeatured,
                    HeroImageUrl = d.HeroImageUrl,
                    Tags = BlogTagParser.ParseTags(d.Tags)
                };
            })
            .ToArray();

        var topTags = allFiltered
            .SelectMany(d => BlogTagParser.ParseTags(d.Tags))
            .GroupBy(tagName => tagName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(30)
            .Select(g => new TagCountViewModel
            {
                Tag = g.Key,
                Count = g.Count()
            })
            .ToArray();

        var model = new IndexViewModel
        {
            PageTitle = BuildIndexPageTitle(normalizedTag, normalizedQuery),
            SortBy = normalizedSort,
            SearchQuery = normalizedQuery,
            CurrentTag = normalizedTag,
            TotalPosts = totalPosts,
            CurrentPage = currentPageFinal,
            TotalPages = totalPagesFinal,
            Posts = postCards,
            TopTags = topTags,
            UsedAiSearch = usedAi,
            RateLimited = rateLimited
        };
        return this.SimpleView(model, viewName: nameof(Index));
    }

    private static string BuildPostUrl(MarkdownDocument document)
    {
        return !string.IsNullOrWhiteSpace(document.Slug)
            ? $"/post/{document.Slug}"
            : $"/post/{document.Id}";
    }

    private static string BuildIndexPageTitle(string? tag, string? query)
    {
        if (!string.IsNullOrWhiteSpace(tag))
        {
            return $"Tag: {tag}";
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            return $"Search: {query}";
        }

        return "Home";
    }

    private static string BuildExcerpt(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var plainText = Regex.Replace(markdown, @"!\[[^\]]*]\([^)]+\)", " ");
        plainText = Regex.Replace(plainText, @"\[(?<text>[^\]]+)]\([^)]+\)", "${text}");
        plainText = Regex.Replace(plainText, @"[#>*`~\-_|]", " ");
        plainText = Regex.Replace(plainText, @"\s+", " ").Trim();

        return plainText.Length <= 280
            ? plainText
            : $"{plainText[..280]}...";
    }
}
