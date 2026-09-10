using Aiursoft.UiStack.Layout;

namespace Aiursoft.MoongladeV2.Models.CustomPageViewModels;

public class PageViewModel : UiStackLayoutViewModel
{
    public required string Title { get; init; }
    public required string MetaDescription { get; init; }
    public required string SanitizedHtmlContent { get; init; }
    public required string CssContent { get; init; }
    public bool IsPreview { get; init; }
}
