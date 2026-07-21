using Aiursoft.MoongladeV2.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.MoongladeV2.Models.HomeViewModels;

public class PostsViewModel : UiStackLayoutViewModel
{
    public PostsViewModel()
    {
        PageTitle = "Posts";
    }

    public required List<MarkdownDocument> Posts { get; init; }
    public string? SearchQuery { get; set; }
}
