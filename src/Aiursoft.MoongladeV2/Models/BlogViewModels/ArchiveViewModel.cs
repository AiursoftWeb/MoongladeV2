using Aiursoft.UiStack.Layout;

namespace Aiursoft.MoongladeV2.Models.BlogViewModels;

public class ArchiveViewModel : UiStackLayoutViewModel
{
    public ArchiveViewModel()
    {
        PageTitle = "Archive";
    }

    public int TotalPosts { get; set; }
    public IReadOnlyList<ArchiveMonthViewModel> Months { get; set; } = [];
}

public class ArchiveMonthViewModel
{
    public required string Label { get; init; }
    public required IReadOnlyList<BlogPostSummaryViewModel> Posts { get; init; }
}
