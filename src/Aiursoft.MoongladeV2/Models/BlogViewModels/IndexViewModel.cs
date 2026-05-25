using Aiursoft.UiStack.Layout;

namespace Aiursoft.MoongladeV2.Models.BlogViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Home";
    }

    public IReadOnlyList<BlogPostSummaryViewModel> Posts { get; set; } = [];
    public IReadOnlyList<TagCountViewModel> TopTags { get; set; } = [];
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalPosts { get; set; }
    public string SortBy { get; set; } = "Recent";
    public string? SearchQuery { get; set; }
    public string? CurrentTag { get; set; }
    public bool UsedAiSearch { get; set; }
    public bool RateLimited { get; set; }
}
