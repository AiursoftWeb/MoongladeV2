using Aiursoft.UiStack.Layout;
using Aiursoft.MoongladeV2.Entities;

namespace Aiursoft.MoongladeV2.Models.AdminViewModels;

public class UserDocumentsViewModel : UiStackLayoutViewModel
{
    public UserDocumentsViewModel()
    {
        PageTitle = "User Documents";
    }

    public required User User { get; set; }
    public required List<MarkdownDocument> UserDocuments { get; set; }
    public string? SearchQuery { get; set; }
}
