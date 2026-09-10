using Aiursoft.MoongladeV2.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.MoongladeV2.Models.CustomPageViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Custom Pages";
    }

    public required IReadOnlyList<CustomPage> Pages { get; init; }
}
