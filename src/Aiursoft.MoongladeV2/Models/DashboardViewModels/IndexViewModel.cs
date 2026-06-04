using Aiursoft.UiStack.Layout;
using Aiursoft.MoongladeV2.Entities;

namespace Aiursoft.MoongladeV2.Models.DashboardViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Dashboard";
    }

    public int TotalDocuments { get; set; }
    public int PublicPosts { get; set; }
    public List<MarkdownDocument>? RecentDocuments { get; set; }
}
