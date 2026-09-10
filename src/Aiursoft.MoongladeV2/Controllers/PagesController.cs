using Aiursoft.MoongladeV2.Authorization;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Models.CustomPageViewModels;
using Aiursoft.MoongladeV2.Services;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Ganss.Xss;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Controllers;

[LimitPerMin]
public class PagesController(TemplateDbContext context, HtmlSanitizer htmlSanitizer) : Controller
{
    [HttpGet("/page/{slug}")]
    [AllowAnonymous]
    public async Task<IActionResult> ViewPage([FromRoute] string slug)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var page = await context.CustomPages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == normalizedSlug && p.IsPublished);
        return page == null ? NotFound() : RenderPage(page, false);
    }

    [HttpGet("/page/preview/{id:guid}")]
    [Authorize(Policy = AppPermissionNames.CanManageCustomPages)]
    public async Task<IActionResult> Preview([FromRoute] Guid id)
    {
        var page = await context.CustomPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        return page == null ? NotFound() : RenderPage(page, true);
    }

    [HttpGet]
    [Authorize(Policy = AppPermissionNames.CanManageCustomPages)]
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Home",
        CascadedLinksIcon = "file-stack",
        CascadedLinksOrder = 3,
        LinkText = "Custom Pages",
        LinkOrder = 3)]
    public async Task<IActionResult> Index()
    {
        var pages = await context.CustomPages.AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
        return this.StackView(new IndexViewModel { Pages = pages });
    }

    [HttpGet]
    [Authorize(Policy = AppPermissionNames.CanManageCustomPages)]
    public IActionResult Create() => this.StackView(new EditViewModel(), nameof(Edit));

    [HttpGet]
    [Authorize(Policy = AppPermissionNames.CanManageCustomPages)]
    public async Task<IActionResult> Edit([FromRoute] Guid id)
    {
        var page = await context.CustomPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (page == null) return NotFound();

        return this.StackView(new EditViewModel
        {
            Id = page.Id,
            Title = page.Title,
            Slug = page.Slug,
            MetaDescription = page.MetaDescription,
            HtmlContent = page.HtmlContent,
            CssContent = page.CssContent,
            HideSidebar = page.HideSidebar,
            IsPublished = page.IsPublished
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanManageCustomPages)]
    public async Task<IActionResult> Save(EditViewModel model)
    {
        model.Slug = model.Slug.Trim().ToLowerInvariant();
        if (!ModelState.IsValid) return this.StackView(model, nameof(Edit));

        var slugIsOccupied = await context.CustomPages
            .AnyAsync(p => p.Slug == model.Slug && p.Id != model.Id);
        if (slugIsOccupied)
        {
            ModelState.AddModelError(nameof(model.Slug), "That page URL is already in use.");
            return this.StackView(model, nameof(Edit));
        }

        CustomPage page;
        if (model.Id.HasValue)
        {
            var existingPage = await context.CustomPages.FirstOrDefaultAsync(p => p.Id == model.Id.Value);
            if (existingPage == null) return NotFound();
            page = existingPage;
            page.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            page = new CustomPage
            {
                Id = Guid.NewGuid(),
                Title = string.Empty,
                Slug = string.Empty,
                MetaDescription = string.Empty,
                HtmlContent = string.Empty,
                CssContent = string.Empty,
                CreatedAt = DateTime.UtcNow
            };
            context.CustomPages.Add(page);
        }

        page.Title = model.Title.Trim();
        page.Slug = model.Slug;
        page.MetaDescription = model.MetaDescription.Trim();
        page.HtmlContent = model.HtmlContent ?? string.Empty;
        page.CssContent = model.CssContent ?? string.Empty;
        page.HideSidebar = model.HideSidebar;
        page.IsPublished = model.IsPublished;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            if (await context.CustomPages.AsNoTracking()
                    .AnyAsync(p => p.Slug == model.Slug && p.Id != page.Id))
            {
                ModelState.AddModelError(nameof(model.Slug), "That page URL is already in use.");
                return this.StackView(model, nameof(Edit));
            }

            throw;
        }

        return RedirectToAction(nameof(Edit), new { id = page.Id, saved = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanManageCustomPages)]
    public async Task<IActionResult> Delete([FromForm] Guid id)
    {
        var page = await context.CustomPages.FirstOrDefaultAsync(p => p.Id == id);
        if (page == null) return NotFound();
        context.CustomPages.Remove(page);
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private IActionResult RenderPage(CustomPage page, bool isPreview)
    {
        ViewBag.MetaDescription = page.MetaDescription;
        return this.SimpleView(new PageViewModel
        {
            PageTitle = page.Title,
            Title = page.Title,
            MetaDescription = page.MetaDescription,
            SanitizedHtmlContent = htmlSanitizer.Sanitize(page.HtmlContent),
            CssContent = page.CssContent.Replace("<", "\\3C ", StringComparison.Ordinal),
            IsPreview = isPreview
        }, nameof(ViewPage));
    }
}
