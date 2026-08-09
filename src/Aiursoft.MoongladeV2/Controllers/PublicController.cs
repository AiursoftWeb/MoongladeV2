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
/// (CreateEditOrDeleteDraftDocument or CreateEditOrPublishAnyDocument).
/// </summary>
[Route("share/{id:guid}")]
public class PublicController(
    ILogger<PublicController> logger,
    TemplateDbContext context,
    MoongladeV2Service mtohService,
    DocumentAuthorizationService documentAuthorizationService) : Controller
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

        var accessLevel = await documentAuthorizationService.GetAccessLevelAsync(User);
        if (!document.IsPublic && accessLevel == DocumentAccessLevel.None)
        {
            return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();
        }

        return RenderDocument(document, id, accessLevel);
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

        var accessLevel = await documentAuthorizationService.GetAccessLevelAsync(User);
        if (!document.IsPublic && accessLevel == DocumentAccessLevel.None)
        {
            return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();
        }

        return Content(document.Content ?? string.Empty, "text/plain; charset=utf-8");
    }

    private IActionResult RenderDocument(
        MarkdownDocument document,
        Guid id,
        DocumentAccessLevel accessLevel)
    {
        logger.LogInformation(
            "Document with ID: '{DocumentId}' accessed. Public: {IsPublic}",
            document.Id, document.IsPublic);

        var outputHtml = mtohService.ConvertMarkdownToHtml(document.Content ?? string.Empty);

        var model = new PublicDocumentViewModel(document.Title ?? "Untitled Document")
        {
            DocumentTitle = document.Title ?? "Untitled Document",
            Content = outputHtml,
            MarkdownContent = document.Content ?? string.Empty,
            AuthorName = document.User.UserName ?? "Unknown Author",
            CreationTime = document.CreationTime,
            CanEdit = DocumentAuthorizationService.CanModify(accessLevel, document)
        };

        ViewBag.DocumentId = id;
        return this.StackView(model);
    }
}
