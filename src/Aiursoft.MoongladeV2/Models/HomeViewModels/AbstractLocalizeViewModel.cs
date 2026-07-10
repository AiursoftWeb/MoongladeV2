using Aiursoft.UiStack.Layout;

namespace Aiursoft.MoongladeV2.Models.HomeViewModels;

public class AbstractLocalizeViewModel : UiStackLayoutViewModel
{
    // ReSharper disable once UnusedMember.Global
    [Obsolete("This constructor is only used for framework!", true)]
    public AbstractLocalizeViewModel()
    {
        PageTitle = "Abstract Localization";
    }

    public AbstractLocalizeViewModel(string documentTitle)
    {
        PageTitle = $"{documentTitle} - Abstract Localization";
    }

    public Guid DocumentId { get; set; }

    public string? DocumentTitle { get; set; }

    public string? SourceCulture { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<AbstractLanguageInfo> Languages { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public string? SuccessMessage { get; set; }
}

public class AbstractLanguageInfo
{
    public string Culture { get; init; } = string.Empty;

    public string NativeName { get; init; } = string.Empty;

    public bool HasTranslation { get; init; }

    public DateTime? LastGeneratedAt { get; init; }

    public bool IsSourceCulture { get; init; }
}
