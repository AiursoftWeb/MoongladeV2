namespace Aiursoft.MoongladeV2.Models.BlogViewModels;

public class BlogPostSummaryViewModel
{
    public required Guid Id { get; init; }
    public string? Slug { get; init; }
    public required string Url { get; init; }
    public string Title { get; init; } = "Untitled";
    public string Excerpt { get; init; } = string.Empty;
    public DateTime PublishedAt { get; init; }
    public bool IsFeatured { get; init; }
    public string? HeroImageUrl { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}
