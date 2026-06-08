using System.ComponentModel.DataAnnotations;
using Aiursoft.MoongladeV2.Authorization;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Controllers;

[Authorize]
[LimitPerMin]
public class CommentsController(
    TemplateDbContext db,
    UserManager<User> userManager,
    GlobalSettingsService globalSettingsService) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Post(
        [Required][FromForm] Guid documentId,
        [FromForm] Guid? parentCommentId,
        [Required][FromForm][MaxLength(1000)] string content)
    {
        var enableComments = await globalSettingsService.GetBoolSettingAsync(SettingsMap.EnableComments);
        if (!enableComments) return Forbid();

        if (string.IsNullOrWhiteSpace(content) || content.Length > 1000)
            return BadRequest();

        var documentExists = await db.MarkdownDocuments
            .AnyAsync(d => d.Id == documentId && d.IsPublic);
        if (!documentExists)
            return NotFound();

        if (parentCommentId.HasValue)
        {
            var parent = await db.Comments
                .FirstOrDefaultAsync(c => c.Id == parentCommentId.Value && c.DocumentId == documentId);
            if (parent == null || parent.ParentCommentId != null) // max 2 levels
                return BadRequest();
        }

        var userId = userManager.GetUserId(User)!;
        var requireReview = await globalSettingsService.GetBoolSettingAsync(SettingsMap.RequireCommentReview);

        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            UserId = userId,
            ParentCommentId = parentCommentId,
            Content = content.Trim(),
            CreatedAt = DateTime.UtcNow,
            IsApproved = !requireReview
        };

        db.Comments.Add(comment);
        await db.SaveChangesAsync();

        var slug = await db.MarkdownDocuments
            .Where(d => d.Id == documentId)
            .Select(d => d.Slug)
            .FirstOrDefaultAsync();

        var postUrlSegment = !string.IsNullOrWhiteSpace(slug) ? slug : documentId.ToString();
        return RedirectToAction("Post", "Blog", new { slug = postUrlSegment }, "comments");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([Required][FromForm] Guid commentId)
    {
        var comment = await db.Comments
            .Include(c => c.Replies)
            .FirstOrDefaultAsync(c => c.Id == commentId);

        if (comment == null)
            return NotFound();

        var userId = userManager.GetUserId(User);
        var user = await userManager.FindByIdAsync(userId!);
        var isAdmin = (user != null && await userManager.IsInRoleAsync(user, "Administrators")) ||
                       User.IsInRole("Administrators");

        if (comment.UserId != userId && !isAdmin)
            return Forbid();

        // Delete replies first, then the comment itself
        db.Comments.RemoveRange(comment.Replies);
        db.Comments.Remove(comment);
        await db.SaveChangesAsync();

        var slug = await db.MarkdownDocuments
            .Where(d => d.Id == comment.DocumentId)
            .Select(d => d.Slug)
            .FirstOrDefaultAsync();

        var postUrlSegment = !string.IsNullOrWhiteSpace(slug) ? slug : comment.DocumentId.ToString();
        return RedirectToAction("Post", "Blog", new { slug = postUrlSegment }, "comments");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanManageComments)]
    public async Task<IActionResult> ToggleApproval([Required][FromForm] Guid commentId)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId);
        if (comment == null)
            return NotFound();

        comment.IsApproved = !comment.IsApproved;
        await db.SaveChangesAsync();

        return RedirectToAction("Comments", "Admin");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanManageComments)]
    public async Task<IActionResult> DeleteSelected([FromForm] List<Guid> commentIds)
    {
        if (commentIds == null || commentIds.Count == 0)
            return RedirectToAction("Comments", "Admin");

        var comments = await db.Comments
            .Include(c => c.Replies)
            .Where(c => commentIds.Contains(c.Id))
            .ToListAsync();

        foreach (var comment in comments)
        {
            db.Comments.RemoveRange(comment.Replies);
            db.Comments.Remove(comment);
        }

        await db.SaveChangesAsync();
        return RedirectToAction("Comments", "Admin");
    }
}
