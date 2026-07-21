using System.ComponentModel.DataAnnotations;
using Aiursoft.MoongladeV2.Authorization;
using Aiursoft.MoongladeV2.Models.PublicViewModels;
using Aiursoft.MoongladeV2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Aiursoft.MoongladeV2.Entities;

namespace Aiursoft.MoongladeV2.Controllers;

/// <summary>
/// Controller for shared and public documents.
/// Public documents are accessible to everyone.
/// Private documents (drafts) are only visible to users with content permissions
/// (CreateOrEditDraftDocument or CreateEditOrPublishAnyDocument).
/// </summary>
[Route("share/{id:guid}")]
public class PublicController(
    ILogger<PublicController> logger,
    TemplateDbContext context,
    MoongladeV2Service mtohService,
    IAuthorizationService authorizationService) : Controller
{
    /// <summary>
    /// View a shared document.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> View([Required][FromRoute] Guid id)
    {
        logger.LogTrace("Attempting to view document with ID: '{Id}'", id);

        var document = await context.MarkdownDocuments
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            logger.LogWarning("Document with ID: '{Id}' was not found.", id);
            return NotFound("The document was not found.");
        }

        // Public documents are visible to everyone
        if (document.IsPublic)
        {
            return await RenderDocumentAsync(document, id);
        }

        // Private documents: only users with content permissions can preview
        if (!await HasContentPermission())
        {
            return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();
        }

        return await RenderDocumentAsync(document, id);
    }

    /// <summary>
    /// Print a shared document.
    /// </summary>
    [HttpGet("print")]
    public async Task<IActionResult> Print([Required][FromRoute] Guid id)
    {
        var document = await context.MarkdownDocuments
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        if (!document.IsPublic && !await HasContentPermission())
        {
            return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();
        }

        var outputHtml = mtohService.ConvertMarkdownToHtml(document.Content ?? string.Empty);
        var model = new PublicDocumentViewModel(document.Title ?? "Untitled Document")
        {
            DocumentTitle = document.Title ?? "Untitled Document",
            Content = outputHtml,
            MarkdownContent = document.Content ?? string.Empty,
            AuthorName = document.User.UserName ?? "Unknown Author",
            CreationTime = document.CreationTime,
            CanEdit = false
        };

        return View(model);
    }

    /// <summary>
    /// View the raw Markdown content of a shared document.
    /// </summary>
    [HttpGet("raw")]
    public async Task<IActionResult> Raw([Required][FromRoute] Guid id)
    {
        var document = await context.MarkdownDocuments
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        if (!document.IsPublic && !await HasContentPermission())
        {
            return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();
        }

        return Content(document.Content ?? string.Empty, "text/plain; charset=utf-8");
    }

    private async Task<bool> HasContentPermission()
    {
        return (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CreateEditOrPublishAnyDocument)).Succeeded ||
               (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CreateOrEditDraftDocument)).Succeeded;
    }

    private async Task<IActionResult> RenderDocumentAsync(MarkdownDocument document, Guid id)
    {
        logger.LogInformation(
            "Document with ID: '{DocumentId}' accessed. Public: {IsPublic}",
            document.Id, document.IsPublic);

        var outputHtml = mtohService.ConvertMarkdownToHtml(document.Content ?? string.Empty);

        var canEdit = await HasContentPermission();

        var model = new PublicDocumentViewModel(document.Title ?? "Untitled Document")
        {
            DocumentTitle = document.Title ?? "Untitled Document",
            Content = outputHtml,
            MarkdownContent = document.Content ?? string.Empty,
            AuthorName = document.User.UserName ?? "Unknown Author",
            CreationTime = document.CreationTime,
            CanEdit = canEdit
        };

        ViewBag.DocumentId = id;
        return this.StackView(model);
    }
}
