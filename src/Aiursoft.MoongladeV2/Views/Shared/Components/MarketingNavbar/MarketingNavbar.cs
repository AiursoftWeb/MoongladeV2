using Aiursoft.MoongladeV2.Configuration;
using Microsoft.AspNetCore.Mvc;
using Aiursoft.MoongladeV2.Services;

namespace Aiursoft.MoongladeV2.Views.Shared.Components.MarketingNavbar;

public class MarketingNavbar(
    GlobalSettingsService globalSettingsService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(MarketingNavbarViewModel? model = null)
    {
        model ??= new MarketingNavbarViewModel();
        model.ProjectName = await globalSettingsService.GetSettingValueAsync(SettingsMap.ProjectName);
        model.LogoUrl = await globalSettingsService.GetLogoUrlAsync();
        return View(model);
    }
}
