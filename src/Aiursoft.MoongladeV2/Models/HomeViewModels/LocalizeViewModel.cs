using Aiursoft.UiStack.Layout;

namespace Aiursoft.MoongladeV2.Models.HomeViewModels;

public class LocalizeViewModel : UiStackLayoutViewModel
{
    // ReSharper disable once UnusedMember.Global
    [Obsolete("This constructor is only used for framework!", true)]
    public LocalizeViewModel()
    {
        PageTitle = "Localization";
    }

    public LocalizeViewModel(string documentTitle)
    {
        PageTitle = $"{documentTitle} - Localization";
    }

    public Guid DocumentId { get; set; }

    public string? DocumentTitle { get; set; }

    public string? SourceCulture { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<LanguageInfo> Languages { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public string? SuccessMessage { get; set; }
}

public class LanguageInfo
{
    public string Culture { get; init; } = string.Empty;

    public string NativeName { get; init; } = string.Empty;

    public bool HasTranslation { get; init; }

    public DateTime? LastLocalizedAt { get; init; }
}
