using Aiursoft.MoongladeV2.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.MoongladeV2.Models.HomeViewModels;

public class SharedWithMeViewModel : UiStackLayoutViewModel
{
    public SharedWithMeViewModel(string pageTitle)
    {
        PageTitle = pageTitle;
    }

    public required List<DocumentShare> Shares { get; init; }
    public required Dictionary<string, string> RoleNames { get; init; }
}
