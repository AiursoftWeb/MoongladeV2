namespace Aiursoft.MoongladeV2.Views.Shared.Components.MarketingNavbar;

public class MarketingNavbarViewModel
{
    public string ProjectName { get; set; } = "Aiursoft MoongladeV2";
    public string LogoUrl { get; set; } = "/logo.svg";
    public string SearchQuery { get; set; } = string.Empty;
    public IReadOnlyList<string> Categories { get; set; } = [];
    public bool IsSignedIn { get; set; }
    public string CurrentUserDisplayName { get; set; } = string.Empty;
}
