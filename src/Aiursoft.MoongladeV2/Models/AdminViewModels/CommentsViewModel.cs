using Aiursoft.MoongladeV2.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.MoongladeV2.Models.AdminViewModels;

public class CommentsViewModel : UiStackLayoutViewModel
{
    public CommentsViewModel()
    {
        PageTitle = "Manage Comments";
    }

    public required List<Comment> AllComments { get; set; }
}
