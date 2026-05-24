using Aiursoft.MoongladeV2.Configuration;
using Microsoft.AspNetCore.Mvc;
using Aiursoft.MoongladeV2.Services;

namespace Aiursoft.MoongladeV2.Views.Shared.Components.MarketingFooter;

public class MarketingFooter(
    GlobalSettingsService globalSettingsService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(MarketingFooterViewModel? model = null)
    {
        model ??= new MarketingFooterViewModel();
        model.ProjectName = await globalSettingsService.GetSettingValueAsync(SettingsMap.ProjectName);
        model.BrandName = await globalSettingsService.GetSettingValueAsync(SettingsMap.BrandName);
        model.BrandHomeUrl = await globalSettingsService.GetSettingValueAsync(SettingsMap.BrandHomeUrl);
        model.Icp = await globalSettingsService.GetSettingValueAsync(SettingsMap.Icp);
        model.LogoUrl = await globalSettingsService.GetLogoUrlAsync();
        
        return View(model);
    }
}
