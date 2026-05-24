using Aiursoft.UiStack.Layout;

namespace Aiursoft.MoongladeV2.Models.BlogViewModels;

public class TagsViewModel : UiStackLayoutViewModel
{
    public TagsViewModel()
    {
        PageTitle = "Tags";
    }

    public IReadOnlyList<TagCountViewModel> Tags { get; set; } = [];
}
