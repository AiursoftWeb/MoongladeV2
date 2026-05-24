using Aiursoft.UiStack.Layout;
using Aiursoft.MoongladeV2.Entities;

namespace Aiursoft.MoongladeV2.Models.AdminViewModels;

public class AllDocumentsViewModel : UiStackLayoutViewModel
{
    public AllDocumentsViewModel()
    {
        PageTitle = "All Documents";
    }

    public required List<MarkdownDocument> AllDocuments { get; set; }
    public string? SearchQuery { get; set; }
}
