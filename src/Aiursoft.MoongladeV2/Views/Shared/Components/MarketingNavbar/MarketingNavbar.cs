using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Services.Authentication;
using Microsoft.AspNetCore.Mvc;
using Aiursoft.MoongladeV2.Services;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Views.Shared.Components.MarketingNavbar;

public class MarketingNavbar(
    GlobalSettingsService globalSettingsService,
    TemplateDbContext dbContext) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(MarketingNavbarViewModel? model = null)
    {
        model ??= new MarketingNavbarViewModel();
        model.ProjectName = await globalSettingsService.GetSettingValueAsync(SettingsMap.ProjectName);
        model.LogoUrl = await globalSettingsService.GetLogoUrlAsync();
        model.SearchQuery = HttpContext.Request.Query["q"].ToString();

        var allRawTags = await dbContext.MarkdownDocuments
            .AsNoTracking()
            .Where(d => d.IsPublic && d.Tags != null)
            .Select(d => d.Tags!)
            .ToListAsync();

        model.Categories = allRawTags
            .SelectMany(BlogTagParser.ParseTags)
            .GroupBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(12)
            .Select(g => g.Key)
            .ToArray();

        if (HttpContext.User.Identity?.IsAuthenticated == true)
        {
            model.IsSignedIn = true;
            model.CurrentUserDisplayName = HttpContext.User.Claims
                .FirstOrDefault(c => c.Type == UserClaimsPrincipalFactory.DisplayNameClaimType)?.Value
                ?? HttpContext.User.Identity?.Name
                ?? "User";
        }

        return View(model);
    }
}
