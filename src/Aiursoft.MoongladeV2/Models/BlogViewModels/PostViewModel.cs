using Aiursoft.UiStack.Layout;

namespace Aiursoft.MoongladeV2.Models.BlogViewModels;

public class PostViewModel : UiStackLayoutViewModel
{
    public PostViewModel()
    {
        PageTitle = "Post";
    }

    public string Title { get; set; } = "Untitled";
    public string ContentHtml { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public string AuthorName { get; set; } = "Unknown Author";
    public string? HeroImageUrl { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = [];
}
