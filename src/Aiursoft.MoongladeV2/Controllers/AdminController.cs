using Aiursoft.MoongladeV2.Authorization;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Aiursoft.MoongladeV2.Models.AdminViewModels;
using Aiursoft.WebTools.Attributes;

namespace Aiursoft.MoongladeV2.Controllers;

/// <summary>
/// This controller is used for administrative actions.
/// </summary>
[Authorize]
[LimitPerMin]
public class AdminController(
    TemplateDbContext context)
    : Controller
{
    /// <summary>
    /// Displays a list of all comments for administration.
    /// This action requires the 'CanManageComments' permission.
    /// </summary>
    [Authorize(Policy = AppPermissionNames.CanManageComments)]
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Comments",
        CascadedLinksIcon = "message-square",
        CascadedLinksOrder = 2,
        LinkText = "Manage Comments",
        LinkOrder = 2)]
    public async Task<IActionResult> Comments()
    {
        var comments = await context.Comments
            .AsNoTracking()
            .Include(c => c.Document)
            .Include(c => c.User)
            .Include(c => c.Replies)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return this.StackView(new CommentsViewModel
        {
            AllComments = comments
        });
    }
}
