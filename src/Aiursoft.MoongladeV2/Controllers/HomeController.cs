using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Aiursoft.CSTools.Tools;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.MoongladeV2.Models.HomeViewModels;
using Aiursoft.MoongladeV2.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.WebTools.Attributes;
using Aiursoft.MoongladeV2.Authorization;


namespace Aiursoft.MoongladeV2.Controllers;

[LimitPerMin]
public class HomeController(
    ILogger<HomeController> logger,
    UserManager<User> userManager,
    TemplateDbContext context,
    MoongladeV2Service mtohService,
    IAuthorizationService authorizationService,
    GlobalSettingsService globalSettingsService) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Home",
        CascadedLinksIcon = "home",
        CascadedLinksOrder = 1,
        LinkText = "Write Post",
        LinkOrder = 1
    )]

    [Route("/Home")]
    [Route("/Home/Editor")]
    [HttpGet]
    public IActionResult Editor()
    {
        return this.StackView(new IndexViewModel("Untitled Post"));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveNew(IndexViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return this.StackView(model);
        }

        var userId = userManager.GetUserId(User);
        if (User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(userId))
        {
            // If the user is authenticated, this action only saves the document in the database. And it's `edit` action to render it.
            // And go to the edit page.
            logger.LogTrace("Authenticated user submitted a document with ID: '{Id}'. Save it to the database.",
                model.DocumentId);
            var documentInDb = await context.MarkdownDocuments
                .FirstOrDefaultAsync(d => d.Id == model.DocumentId);
            var isExistingDocument = documentInDb != null;

            if (documentInDb != null)
            {
                // Check permissions for existing document
                bool isOwner = documentInDb.UserId == userId;
                bool canEdit = isOwner;

                if (!isOwner)
                {
                    // Check if user has Editable permission
                    var userRoles = await context.UserRoles
                        .Where(ur => ur.UserId == userId)
                        .Select(ur => ur.RoleId)
                        .ToListAsync();

                    canEdit = await context.DocumentShares
                        .AnyAsync(s => s.DocumentId == model.DocumentId &&
                                      s.Permission == SharePermission.Editable &&
                                      (s.SharedWithUserId == userId ||
                                       (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));
                }

                if (!canEdit)
                {
                    return Forbid();
                }

                logger.LogInformation("Updating the document with ID: '{Id}'.", model.DocumentId);
                documentInDb.UpdatedAt = DateTime.UtcNow;
                documentInDb.Content = model.InputMarkdown.SafeSubstring(65535);
                documentInDb.Title = model.Title;
            }
            else
            {
                model.DocumentId = Guid.NewGuid();
                logger.LogInformation("Creating a new document with ID: '{Id}'.", model.DocumentId);
                var newDocument = new MarkdownDocument
                {
                    Id = model.DocumentId,
                    Content = model.InputMarkdown.SafeSubstring(65535),
                    Title = string.IsNullOrWhiteSpace(model.Title)
                        ? model.InputMarkdown.SafeSubstring(40)
                        : model.Title.Trim(),
                    UserId = userId
                };
                context.MarkdownDocuments.Add(newDocument);
            }

            await context.SaveChangesAsync();
            return RedirectToAction(nameof(Edit), new { id = model.DocumentId, saved = isExistingDocument });
        }
        else
        {
            // If the user is not authenticated, just show the result.
            logger.LogInformation(
                "An anonymous user submitted a document with ID: '{Id}'. It was not saved to the database.",
                model.DocumentId);
            model.OutputHtml = mtohService.ConvertMarkdownToHtml(model.InputMarkdown);
            return this.StackView(model);
        }
    }

    [Authorize]
    public async Task<IActionResult> Edit([Required][FromRoute] Guid id, [FromQuery] bool? saved = false)
    {
        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .Include(d => d.DocumentShares)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        // Check if user is the owner
        bool isOwner = document.UserId == userId;
        bool canEdit = isOwner;

        if (!isOwner)
        {
            // Check if document is shared with the user with Editable permission
            var userRoles = await context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            canEdit = await context.DocumentShares
                .AnyAsync(s => s.DocumentId == id &&
                              s.Permission == SharePermission.Editable &&
                              (s.SharedWithUserId == userId ||
                               (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));
        }

        if (!canEdit)
        {
            return Forbid();
        }

        var publicLink = Url.Action(nameof(PublicController.View), "Public", new { id = document.Id }, Request.Scheme);

        var model = new IndexViewModel(document.Title ?? "Untitled Post")
        {
            DocumentId = document.Id,
            Title = document.Title,
            InputMarkdown = document.Content ?? string.Empty,
            OutputHtml = mtohService.ConvertMarkdownToHtml(document.Content ?? string.Empty),
            IsEditing = true,
            SavedSuccessfully = saved ?? false,
            IsPublic = document.IsPublic,
            PublicLink = publicLink,
            HasInternalShares = document.DocumentShares.Any()
        };

        return this.StackView(model: model, viewName: nameof(Editor)); // Reuse the Index view for editing.
    }

    /// <summary>
    /// AJAX quick save endpoint for Ctrl+S. Saves the document without page refresh.
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveUpdate(IndexViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();
            return BadRequest(new { success = false, errors });
        }

        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var documentInDb = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == model.DocumentId);

        if (documentInDb != null)
        {
            bool isOwner = documentInDb.UserId == userId;
            bool canEdit = isOwner;

            if (!isOwner)
            {
                var userRoles = await context.UserRoles
                    .Where(ur => ur.UserId == userId)
                    .Select(ur => ur.RoleId)
                    .ToListAsync();

                canEdit = await context.DocumentShares
                    .AnyAsync(s => s.DocumentId == model.DocumentId &&
                                  s.Permission == SharePermission.Editable &&
                                  (s.SharedWithUserId == userId ||
                                   (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));
            }

            if (!canEdit)
            {
                return Forbid();
            }

            documentInDb.UpdatedAt = DateTime.UtcNow;
            documentInDb.Content = model.InputMarkdown.SafeSubstring(65535);
            documentInDb.Title = model.Title;
        }
        else
        {
            return NotFound("Document not found. Use SaveNew to create a new document.");
        }

        await context.SaveChangesAsync();
        return Ok(new { success = true, documentId = model.DocumentId });
    }

    [Authorize]
    [RenderInNavBar(
    NavGroupName = "Features",
    NavGroupOrder = 1,
    CascadedLinksGroupName = "Home",
    CascadedLinksIcon = "history",
    CascadedLinksOrder = 2,
    LinkText = "My posts",
    LinkOrder = 2)]
    public async Task<IActionResult> History([FromQuery] string? search)
    {
        var userId = userManager.GetUserId(User);
        var trimmedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var documentsQuery = context.MarkdownDocuments
            .Where(d => d.UserId == userId);

        if (trimmedSearch != null)
        {
            documentsQuery = documentsQuery.Where(d =>
                (d.Title != null && d.Title.Contains(trimmedSearch)) ||
                (d.Content != null && d.Content.Contains(trimmedSearch)));
        }

        var documents = await documentsQuery
            .Include(d => d.DocumentShares)
            .OrderByDescending(d => d.CreationTime)
            .ToListAsync();

        var model = new HistoryViewModel
        {
            MyDocuments = documents,
            SearchQuery = trimmedSearch
        };
        return this.StackView(model);
    }

    // GET: /Home/Delete/{guid}
    [Authorize]
    public async Task<IActionResult> Delete(Guid? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

        if (document == null)
        {
            // Document not found or user does not have permission.
            return NotFound();
        }

        return this.StackView(new DeleteViewModel
        {
            Document = document
        });
    }

    // POST: /Home/Delete/{guid}
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

        if (document == null)
        {
            return NotFound();
        }

        context.MarkdownDocuments.Remove(document);
        await context.SaveChangesAsync();

        logger.LogInformation("Document with ID: '{Id}' was deleted by user: '{UserId}'.", id, userId);

        return RedirectToAction(nameof(History));
    }

    /// <summary>
    /// Make a document public.
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MakePublic([Required][FromRoute] Guid id)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        var userId = userManager.GetUserId(User);
        var isOwner = document.UserId == userId;
        var canManage = isOwner || (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyShare)).Succeeded;

        if (!canManage)
        {
            return NotFound("The document was not found or you do not have permission to modify it.");
        }

        if (!document.IsPublic)
        {
            document.IsPublic = true;
            await context.SaveChangesAsync();
            logger.LogInformation("Document with ID: '{DocumentId}' was made public by user: '{UserId}'.",
                id, userId);
        }

        return Ok();
    }

    /// <summary>
    /// Make a document private.
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MakePrivate([Required][FromRoute] Guid id)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        var userId = userManager.GetUserId(User);
        var isOwner = document.UserId == userId;
        var canManage = isOwner || (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyShare)).Succeeded;

        if (!canManage)
        {
            return NotFound("The document was not found or you do not have permission to modify it.");
        }

        if (document.IsPublic)
        {
            document.IsPublic = false;
            await context.SaveChangesAsync();
            logger.LogInformation("Document with ID: '{DocumentId}' was made private by user: '{UserId}'.",
                id, userId);
        }

        return Ok();
    }

    /// <summary>
    /// Update document visibility (Public or Private)
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateVisibility([Required][FromRoute] Guid id, [FromForm] bool publicAccess)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        var userId = userManager.GetUserId(User);
        var isOwner = document.UserId == userId;
        var canManage = isOwner || (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyShare)).Succeeded;

        if (!canManage)
        {
            return NotFound("The document was not found or you do not have permission to modify it.");
        }

        document.IsPublic = publicAccess;
        logger.LogInformation("Document with ID: '{DocumentId}' visibility updated to {IsPublic} by user: '{UserId}'.",
            id, document.IsPublic, userId);

        await context.SaveChangesAsync();
        return RedirectToAction(nameof(ManageShares), new { id });
    }

    /// <summary>
    /// GET: Manage shares for a specific document
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> ManageShares([Required][FromRoute] Guid id)
    {
        var document = await context.MarkdownDocuments
            .Include(d => d.DocumentShares)
                .ThenInclude(s => s.SharedWithUser)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        var userId = userManager.GetUserId(User);
        var isOwner = document.UserId == userId;
        var canManage = isOwner || (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyShare)).Succeeded;

        if (!canManage)
        {
            return NotFound("The document was not found or you do not have permission to modify it.");
        }

        var allRoles = await context.Roles.ToListAsync();
        var model = new ManageSharesViewModel(document.Title ?? "Untitled Document")
        {
            DocumentId = document.Id,
            DocumentTitle = document.Title ?? "Untitled Document",
            IsPublic = document.IsPublic,
            PublicLink = Url.Action(nameof(PublicController.View), "Public", new { id = document.Id }, Request.Scheme),
            ExistingShares = document.DocumentShares.ToList(),
            AvailableRoles = allRoles
        };

        return this.StackView(model);
    }

    /// <summary>
    /// POST: Add a new share for a document
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddShare([Required][FromRoute] Guid id, AddShareViewModel model)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        var userId = userManager.GetUserId(User);
        var isOwner = document.UserId == userId;
        var canManage = isOwner || (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyShare)).Succeeded;

        if (!canManage)
        {
            return NotFound("The document was not found or you do not have permission to modify it.");
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var targetUserId = string.IsNullOrWhiteSpace(model.TargetUserId) ? null : model.TargetUserId;
        var targetRoleId = string.IsNullOrWhiteSpace(model.TargetRoleId) ? null : model.TargetRoleId;

        if (targetUserId == null && targetRoleId == null)
        {
            return RedirectToAction(nameof(ManageShares), new { id, error = "invalid" });
        }

        var exists = await context.DocumentShares
            .AnyAsync(s => s.DocumentId == document.Id &&
                           ((targetUserId != null && s.SharedWithUserId == targetUserId) ||
                            (targetRoleId != null && s.SharedWithRoleId == targetRoleId)));

        if (exists)
        {
            return RedirectToAction(nameof(ManageShares), new { id, error = "duplicate" });
        }

        var share = new DocumentShare
        {
            DocumentId = document.Id,
            SharedWithUserId = targetUserId,
            SharedWithRoleId = targetRoleId,
            Permission = model.Permission
        };

        context.DocumentShares.Add(share);
        await context.SaveChangesAsync();

        logger.LogInformation("Document with ID: '{DocumentId}' was shared by user: '{UserId}'.", id, userId);

        return RedirectToAction(nameof(ManageShares), new { id });
    }

    /// <summary>
    /// POST: Remove a share
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveShare([Required][FromRoute] Guid id)
    {
        var share = await context.DocumentShares
            .Include(s => s.Document)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (share == null)
        {
            return NotFound("Share not found.");
        }

        var userId = userManager.GetUserId(User);
        var isOwner = share.Document.UserId == userId;
        var canManage = isOwner || (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyShare)).Succeeded;

        if (!canManage)
        {
            return Forbid();
        }

        context.DocumentShares.Remove(share);
        await context.SaveChangesAsync();

        logger.LogInformation("Share with ID: '{ShareId}' was removed by user: '{UserId}'.", id, userId);

        return RedirectToAction(nameof(ManageShares), new { id = share.DocumentId });
    }

    /// <summary>
    /// GET: View documents shared with me
    /// </summary>
    [HttpGet]
    [Authorize]
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Home",
        CascadedLinksIcon = "share-2",
        CascadedLinksOrder = 3,
        LinkText = "Shared with me",
        LinkOrder = 3)]
    public async Task<IActionResult> SharedWithMe()
    {
        var userId = userManager.GetUserId(User);
        var user = await userManager.FindByIdAsync(userId!);

        if (user == null)
        {
            return NotFound("User not found.");
        }

        // Get user's roles
        var userRoles = await userManager.GetRolesAsync(user);
        var userRoleIds = await context.Roles
            .Where(r => userRoles.Contains(r.Name!))
            .Select(r => r.Id)
            .ToListAsync();

        // Get documents shared directly with user or with user's roles
        var shares = await context.DocumentShares
            .Include(s => s.Document)
                .ThenInclude(d => d.User)
            .Where(s => s.SharedWithUserId == userId || (s.SharedWithRoleId != null && userRoleIds.Contains(s.SharedWithRoleId)))
            .OrderByDescending(s => s.CreationTime)
            .ToListAsync();

        // Get role names
        var roleIds = shares.Where(s => s.SharedWithRoleId != null).Select(s => s.SharedWithRoleId).Distinct().ToList();
        var roles = await context.Roles
            .Where(r => roleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name!);

        var model = new SharedWithMeViewModel("Shared with Me")
        {
            Shares = shares,
            RoleNames = roles
        };

        return this.StackView(model);
    }

    [RenderInNavBar(
        NavGroupName = "Self Host",
        NavGroupOrder = 10,
        CascadedLinksGroupName = "Deployment",
        CascadedLinksIcon = "server",
        CascadedLinksOrder = 1,
        LinkText = "Self host a new server",
        LinkOrder = 1
    )]
    public IActionResult SelfHost()
    {
        return this.StackView(new SelfHostViewModel("Self host a new server"));
    }

    /// <summary>
    /// GET: Localization editor for a document. Shows all configured languages and their translations.
    /// </summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Localize([Required][FromRoute] Guid id)
    {
        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .Include(d => d.LocalizedDocuments)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        // Check edit permissions: owner, shared-with-edit, or admin
        var isOwner = document.UserId == userId;
        var canEdit = isOwner;

        if (!isOwner)
        {
            var userRoles = await context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            canEdit = await context.DocumentShares
                .AnyAsync(s => s.DocumentId == id &&
                              s.Permission == SharePermission.Editable &&
                              (s.SharedWithUserId == userId ||
                               (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));

            if (!canEdit)
            {
                canEdit = (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanEditAnyDocument)).Succeeded;
            }
        }

        if (!canEdit)
        {
            return Forbid();
        }

        // Build the language list
        var languagesRaw = await globalSettingsService.GetSettingValueAsync(SettingsMap.LocalizationLanguages);
        var cultures = languagesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var languageList = new List<LanguageInfo>();

        foreach (var code in cultures)
        {
            try
            {
                var cultureInfo = CultureInfo.GetCultureInfo(code);
                var localized = document.LocalizedDocuments
                    .FirstOrDefault(ld => string.Equals(ld.Culture, code, StringComparison.OrdinalIgnoreCase));

                languageList.Add(new LanguageInfo
                {
                    Culture = code,
                    NativeName = cultureInfo.NativeName,
                    HasTranslation = localized != null,
                    LastLocalizedAt = localized?.LastLocalizedAt
                });
            }
            catch (CultureNotFoundException)
            {
                logger.LogWarning("Invalid culture code in LocalizationLanguages setting: {Code}", code);
            }
        }

        var model = new LocalizeViewModel(document.Title ?? "Untitled Document")
        {
            DocumentId = document.Id,
            DocumentTitle = document.Title ?? "Untitled Document",
            SourceCulture = document.SourceCulture,
            UpdatedAt = document.UpdatedAt,
            Languages = languageList
        };

        return this.StackView(model, nameof(Localize));
    }

    /// <summary>
    /// GET: Returns JSON with localization data for a specific document and culture.
    /// </summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> LocalizeData([Required] Guid id, [Required] string culture)
    {
        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .Include(d => d.LocalizedDocuments)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        // Same permission check as Localize GET
        var isOwner = document.UserId == userId;
        var canEdit = isOwner;

        if (!isOwner)
        {
            var userRoles = await context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            canEdit = await context.DocumentShares
                .AnyAsync(s => s.DocumentId == id &&
                              s.Permission == SharePermission.Editable &&
                              (s.SharedWithUserId == userId ||
                               (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));

            if (!canEdit)
            {
                canEdit = (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanEditAnyDocument)).Succeeded;
            }
        }

        if (!canEdit)
        {
            return Forbid();
        }

        var localized = document.LocalizedDocuments
            .FirstOrDefault(ld => string.Equals(ld.Culture, culture, StringComparison.OrdinalIgnoreCase));

        return Json(new
        {
            documentId = document.Id,
            documentTitle = document.Title,
            sourceCulture = document.SourceCulture,
            updatedAt = document.UpdatedAt,
            culture,
            localizedTitle = localized?.LocalizedTitle ?? string.Empty,
            localizedContent = localized?.LocalizedContent ?? string.Empty,
            lastLocalizedAt = localized?.LastLocalizedAt
        });
    }

    /// <summary>
    /// POST: Saves a manual localization correction via AJAX.
    /// </summary>
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLocalization([Required] Guid documentId, [Required] string culture,
        string localizedTitle, string localizedContent)
    {
        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        // Same permission check
        var isOwner = document.UserId == userId;
        var canEdit = isOwner;

        if (!isOwner)
        {
            var userRoles = await context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            canEdit = await context.DocumentShares
                .AnyAsync(s => s.DocumentId == documentId &&
                              s.Permission == SharePermission.Editable &&
                              (s.SharedWithUserId == userId ||
                               (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));

            if (!canEdit)
            {
                canEdit = (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanEditAnyDocument)).Succeeded;
            }
        }

        if (!canEdit)
        {
            return Forbid();
        }

        // Truncate to entity limits
        localizedTitle = localizedTitle.SafeSubstring(200);
        localizedContent = localizedContent.SafeSubstring(65535);

        var existing = await context.LocalizedDocuments
            .FirstOrDefaultAsync(ld => ld.DocumentId == documentId &&
                                       ld.Culture == culture);

        if (existing == null)
        {
            context.LocalizedDocuments.Add(new LocalizedDocument
            {
                DocumentId = documentId,
                Culture = culture,
                LocalizedTitle = localizedTitle,
                LocalizedContent = localizedContent,
                LastLocalizedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.LocalizedTitle = localizedTitle;
            existing.LocalizedContent = localizedContent;
            existing.LastLocalizedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync();

        logger.LogInformation(
            "User '{UserId}' manually updated localization for document '{DocumentId}' to culture '{Culture}'.",
            userId, documentId, culture);

        return Ok(new { success = true });
    }

    /// <summary>
    /// GET: Abstract localization editor for a document. Shows all configured languages and their abstracts.
    /// </summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> AbstractLocalize([Required][FromRoute] Guid id)
    {
        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        // Check edit permissions: owner, shared-with-edit, or admin
        var isOwner = document.UserId == userId;
        var canEdit = isOwner;

        if (!isOwner)
        {
            var userRoles = await context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            canEdit = await context.DocumentShares
                .AnyAsync(s => s.DocumentId == id &&
                              s.Permission == SharePermission.Editable &&
                              (s.SharedWithUserId == userId ||
                               (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));

            if (!canEdit)
            {
                canEdit = (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanEditAnyDocument)).Succeeded;
            }
        }

        if (!canEdit)
        {
            return Forbid();
        }

        // Build the language list from LocalizedAbstracts (single query, then match in memory)
        var languagesRaw = await globalSettingsService.GetSettingValueAsync(SettingsMap.LocalizationLanguages);
        var cultures = languagesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var allAbstracts = await context.LocalizedAbstracts
            .Where(la => la.DocumentId == id)
            .ToListAsync();

        var languageList = new List<AbstractLanguageInfo>();

        foreach (var code in cultures)
        {
            try
            {
                var cultureInfo = CultureInfo.GetCultureInfo(code);
                var localized = allAbstracts
                    .FirstOrDefault(la => string.Equals(la.Culture, code, StringComparison.OrdinalIgnoreCase));

                languageList.Add(new AbstractLanguageInfo
                {
                    Culture = code,
                    NativeName = cultureInfo.NativeName,
                    HasTranslation = localized != null,
                    LastGeneratedAt = localized?.LastGeneratedAt,
                    IsSourceCulture = string.Equals(code, document.SourceCulture, StringComparison.OrdinalIgnoreCase)
                });
            }
            catch (CultureNotFoundException)
            {
                logger.LogWarning("Invalid culture code in LocalizationLanguages setting: {Code}", code);
            }
        }

        var model = new AbstractLocalizeViewModel(document.Title ?? "Untitled Document")
        {
            DocumentId = document.Id,
            DocumentTitle = document.Title ?? "Untitled Document",
            SourceCulture = document.SourceCulture,
            UpdatedAt = document.UpdatedAt,
            Languages = languageList
        };

        return this.StackView(model, nameof(AbstractLocalize));
    }

    /// <summary>
    /// GET: Returns JSON with abstract data for a specific document and culture.
    /// </summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> AbstractLocalizeData([Required] Guid id, [Required] string culture)
    {
        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        // Same permission check as AbstractLocalize GET
        var isOwner = document.UserId == userId;
        var canEdit = isOwner;

        if (!isOwner)
        {
            var userRoles = await context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            canEdit = await context.DocumentShares
                .AnyAsync(s => s.DocumentId == id &&
                              s.Permission == SharePermission.Editable &&
                              (s.SharedWithUserId == userId ||
                               (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));

            if (!canEdit)
            {
                canEdit = (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanEditAnyDocument)).Succeeded;
            }
        }

        if (!canEdit)
        {
            return Forbid();
        }

        var localized = await context.LocalizedAbstracts
            .FirstOrDefaultAsync(la => la.DocumentId == id &&
                                       la.Culture == culture);

        var isSourceCulture = string.Equals(culture, document.SourceCulture, StringComparison.OrdinalIgnoreCase);

        return Json(new
        {
            documentId = document.Id,
            documentTitle = document.Title,
            sourceCulture = document.SourceCulture,
            culture,
            abstractText = localized?.Abstract ?? string.Empty,
            lastGeneratedAt = localized?.LastGeneratedAt,
            isSourceCulture
        });
    }

    /// <summary>
    /// POST: Saves a manual abstract correction via AJAX.
    /// If saving the source-culture abstract, all other language abstracts are invalidated
    /// so the background job will re-translate them.
    /// </summary>
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAbstractLocalization([Required] Guid documentId, [Required] string culture,
        string abstractText)
    {
        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        // Same permission check
        var isOwner = document.UserId == userId;
        var canEdit = isOwner;

        if (!isOwner)
        {
            var userRoles = await context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            canEdit = await context.DocumentShares
                .AnyAsync(s => s.DocumentId == documentId &&
                              s.Permission == SharePermission.Editable &&
                              (s.SharedWithUserId == userId ||
                               (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));

            if (!canEdit)
            {
                canEdit = (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanEditAnyDocument)).Succeeded;
            }
        }

        if (!canEdit)
        {
            return Forbid();
        }

        // Truncate to entity limit
        abstractText = abstractText.SafeSubstring(8192);

        var existing = await context.LocalizedAbstracts
            .FirstOrDefaultAsync(la => la.DocumentId == documentId &&
                                       la.Culture == culture);

        if (existing == null)
        {
            context.LocalizedAbstracts.Add(new LocalizedAbstract
            {
                DocumentId = documentId,
                Culture = culture,
                Abstract = abstractText,
                LastGeneratedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Abstract = abstractText;
            existing.LastGeneratedAt = DateTime.UtcNow;
        }

        var isSourceCulture = string.Equals(culture, document.SourceCulture, StringComparison.OrdinalIgnoreCase);

        // If saving the source-culture abstract, invalidate all other language abstracts
        // so the background job re-translates them from the updated source.
        if (isSourceCulture)
        {
            var otherAbstracts = await context.LocalizedAbstracts
                .Where(la => la.DocumentId == documentId &&
                             la.Culture != culture)
                .ToListAsync();

            foreach (var other in otherAbstracts)
            {
                other.LastGeneratedAt = DateTime.MinValue;
            }

            logger.LogInformation(
                "User '{UserId}' updated source abstract for document '{DocumentId}' ({Culture}). " +
                "{Count} other language abstract(s) invalidated for re-translation.",
                userId, documentId, culture, otherAbstracts.Count);
        }
        else
        {
            logger.LogInformation(
                "User '{UserId}' manually updated abstract for document '{DocumentId}' to culture '{Culture}'.",
                userId, documentId, culture);
        }

        await context.SaveChangesAsync();

        return Ok(new { success = true, isSourceCulture });
    }
}
