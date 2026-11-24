using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services;

public class FeedService(
    TemplateDbContext dbContext,
    GlobalSettingsService globalSettingsService,
    DocumentLocalizationService localizationService,
    MoongladeV2Service moongladeV2Service) : IScopedDependency
{
    private const int MaxFeedItems = 50;

    private static readonly XNamespace AtomNs = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace ContentNs = "http://purl.org/rss/1.0/modules/content/";
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }

    private static string ToXmlString(XDocument document)
    {
        using var sw = new Utf8StringWriter();
        document.Save(sw);
        return sw.ToString();
    }

    private async Task<(List<FeedItemModel> Items, string ChannelTitle, string ChannelLink, string ChannelDescription, string SelfUrl, string Language)>
        PrepareFeedDataAsync(HttpRequest request, string? tag, string feedPath, CancellationToken cancellationToken)
    {
        var baseUrl = $"{request.Scheme}://{request.Host}".TrimEnd('/');
        var projectName = await globalSettingsService.GetSettingValueAsync(SettingsMap.ProjectName);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = "Moonglade";
        }

        var normalizedTag = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim();
        var channelTitle = normalizedTag != null
            ? $"{projectName} - {normalizedTag}"
            : projectName;

        var channelLink = normalizedTag != null
            ? $"{baseUrl}/tags/{Uri.EscapeDataString(normalizedTag)}"
            : $"{baseUrl}/";

        var channelDescription = normalizedTag != null
            ? $"Posts tagged with {normalizedTag} on {projectName}"
            : "Modern Markdown blogging with AI-powered localization and search.";

        var selfUrl = normalizedTag != null
            ? $"{baseUrl}/{feedPath}/{Uri.EscapeDataString(normalizedTag)}"
            : $"{baseUrl}/{feedPath}";

        var language = CultureInfo.CurrentCulture.Name;
        if (string.IsNullOrWhiteSpace(language))
        {
            language = "en-US";
        }

        var allPublic = await dbContext.MarkdownDocuments
            .AsNoTracking()
            .Include(d => d.User)
            .Where(d => d.IsPublic)
            .OrderByDescending(d => d.CreationTime)
            .ToListAsync(cancellationToken);

        var filtered = allPublic.AsEnumerable();
        if (normalizedTag != null)
        {
            filtered = filtered.Where(d => BlogTagParser.ContainsTag(d.Tags, normalizedTag));
        }

        var pagedDocuments = filtered.Take(MaxFeedItems).ToList();
        var (localizedTitles, localizedContents) = await localizationService.LoadLocalizedStringsAsync(pagedDocuments);
        var localizedAbstracts = await localizationService.LoadLocalizedAbstractsAsync(pagedDocuments);

        var items = pagedDocuments.Select(doc =>
        {
            var content = localizedContents.TryGetValue(doc.Id, out var locContent)
                ? locContent
                : doc.Content ?? string.Empty;

            var title = localizedTitles.TryGetValue(doc.Id, out var locTitle)
                ? locTitle
                : doc.Title ?? "Untitled";

            var excerpt = localizedAbstracts.TryGetValue(doc.Id, out var locAbstract) && !string.IsNullOrWhiteSpace(locAbstract)
                ? locAbstract
                : BlogExcerptHelper.BuildExcerpt(content);

            var postUrl = $"{baseUrl}{PostUrlService.BuildUrl(doc)}";
            var authorName = !string.IsNullOrWhiteSpace(doc.User.DisplayName)
                ? doc.User.DisplayName
                : doc.User.UserName ?? "Author";

            var htmlContent = moongladeV2Service.ConvertMarkdownToHtml(content);

            return new FeedItemModel
            {
                Id = doc.Id,
                Title = title,
                Url = postUrl,
                Excerpt = excerpt,
                ContentHtml = htmlContent,
                PublishedAt = doc.CreationTime,
                UpdatedAt = doc.UpdatedAt,
                AuthorName = authorName,
                Tags = BlogTagParser.ParseTags(doc.Tags)
            };
        }).ToList();

        return (items, channelTitle, channelLink, channelDescription, selfUrl, language);
    }

    public async Task<string> BuildRssAsync(HttpRequest request, string? tag = null, CancellationToken cancellationToken = default)
    {
        var (items, channelTitle, channelLink, channelDescription, selfUrl, language) =
            await PrepareFeedDataAsync(request, tag, "rss", cancellationToken);

        var lastBuildDate = items.Count > 0
            ? items.Max(i => i.PublishedAt).ToUniversalTime().ToString("r", CultureInfo.InvariantCulture)
            : DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);

        var channel = new XElement("channel",
            new XElement("title", channelTitle),
            new XElement("link", channelLink),
            new XElement("description", channelDescription),
            new XElement("language", language),
            new XElement("lastBuildDate", lastBuildDate),
            new XElement(AtomNs + "link",
                new XAttribute("href", selfUrl),
                new XAttribute("rel", "self"),
                new XAttribute("type", "application/rss+xml"))
        );

        foreach (var item in items)
        {
            var itemElement = new XElement("item",
                new XElement("title", item.Title),
                new XElement("link", item.Url),
                new XElement("description", item.Excerpt),
                new XElement("pubDate", item.PublishedAt.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture)),
                new XElement("guid", new XAttribute("isPermaLink", "true"), item.Url),
                new XElement(DcNs + "creator", item.AuthorName)
            );

            if (!string.IsNullOrWhiteSpace(item.ContentHtml))
            {
                var safeHtml = item.ContentHtml.Replace("]]>", "]]]]><![CDATA[>");
                itemElement.Add(new XElement(ContentNs + "encoded", new XCData(safeHtml)));
            }

            foreach (var category in item.Tags)
            {
                itemElement.Add(new XElement("category", category));
            }

            channel.Add(itemElement);
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("rss",
                new XAttribute("version", "2.0"),
                new XAttribute(XNamespace.Xmlns + "atom", AtomNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "content", ContentNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "dc", DcNs.NamespaceName),
                channel)
        );

        return ToXmlString(doc);
    }

    public async Task<string> BuildAtomAsync(HttpRequest request, string? tag = null, CancellationToken cancellationToken = default)
    {
        var (items, channelTitle, channelLink, channelDescription, selfUrl, _) =
            await PrepareFeedDataAsync(request, tag, "atom", cancellationToken);

        var updated = items.Count > 0
            ? items.Max(i => i.UpdatedAt > i.PublishedAt ? i.UpdatedAt : i.PublishedAt).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        var feed = new XElement(AtomNs + "feed",
            new XElement(AtomNs + "title", channelTitle),
            new XElement(AtomNs + "subtitle", channelDescription),
            new XElement(AtomNs + "id", channelLink),
            new XElement(AtomNs + "link", new XAttribute("href", channelLink)),
            new XElement(AtomNs + "link",
                new XAttribute("href", selfUrl),
                new XAttribute("rel", "self"),
                new XAttribute("type", "application/atom+xml")),
            new XElement(AtomNs + "updated", updated)
        );

        foreach (var item in items)
        {
            var entry = new XElement(AtomNs + "entry",
                new XElement(AtomNs + "title", item.Title),
                new XElement(AtomNs + "link", new XAttribute("href", item.Url)),
                new XElement(AtomNs + "id", item.Url),
                new XElement(AtomNs + "published", item.PublishedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
                new XElement(AtomNs + "updated", item.UpdatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
                new XElement(AtomNs + "summary", item.Excerpt),
                new XElement(AtomNs + "author",
                    new XElement(AtomNs + "name", item.AuthorName)
                )
            );

            if (!string.IsNullOrWhiteSpace(item.ContentHtml))
            {
                entry.Add(new XElement(AtomNs + "content", new XAttribute("type", "html"), item.ContentHtml));
            }

            foreach (var category in item.Tags)
            {
                entry.Add(new XElement(AtomNs + "category", new XAttribute("term", category)));
            }

            feed.Add(entry);
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            feed
        );

        return ToXmlString(doc);
    }

    private sealed class FeedItemModel
    {
        public required Guid Id { get; init; }
        public required string Title { get; init; }
        public required string Url { get; init; }
        public required string Excerpt { get; init; }
        public required string ContentHtml { get; init; }
        public required DateTime PublishedAt { get; init; }
        public required DateTime UpdatedAt { get; init; }
        public required string AuthorName { get; init; }
        public required IReadOnlyList<string> Tags { get; init; }
    }
}
