using Aiursoft.UiStack.Layout;
using Aiursoft.MoongladeV2.Entities;

namespace Aiursoft.MoongladeV2.Models.AdminViewModels;

public class DeleteDocumentViewModel : UiStackLayoutViewModel
{
    public DeleteDocumentViewModel()
    {
        PageTitle = "Delete Document";
    }

    public required MarkdownDocument Document { get; set; }
}
