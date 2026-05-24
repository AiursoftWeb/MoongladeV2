using Aiursoft.UiStack.Layout;
using Aiursoft.MoongladeV2.Entities;

namespace Aiursoft.MoongladeV2.Models.HomeViewModels;

public class HistoryViewModel : UiStackLayoutViewModel
{
    public HistoryViewModel()
    {
        PageTitle = "My Documents History";
    }

    public IEnumerable<MarkdownDocument> MyDocuments { get; set; } = new List<MarkdownDocument>();
    public string? SearchQuery { get; set; }
}
