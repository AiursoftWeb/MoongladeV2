using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Models.DashboardViewModels;
using Aiursoft.MoongladeV2.Services;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Controllers;

[Authorize]
[LimitPerMin]
public class DashboardController(
    TemplateDbContext context,
    UserManager<User> userManager) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Dashboard",
        CascadedLinksIcon = "layout",
        CascadedLinksOrder = 1,
        LinkText = "Overview",
        LinkOrder = 1)]
    public async Task<IActionResult> Index()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var userId = user.Id;

        var totalDocuments = await context.MarkdownDocuments
            .CountAsync(d => d.UserId == userId);

        var publicPosts = await context.MarkdownDocuments
            .CountAsync(d => d.UserId == userId && d.IsPublic);

        var recentDocuments = await context.MarkdownDocuments
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.UpdatedAt)
            .Take(5)
            .ToListAsync();

        var model = new IndexViewModel
        {
            TotalDocuments = totalDocuments,
            PublicPosts = publicPosts,
            RecentDocuments = recentDocuments
        };

        return this.StackView(model);
    }
}
