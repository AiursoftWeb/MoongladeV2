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
    [Authorize(Policy = AppPermissionNames.CanManagePosts)]
    public IActionResult Editor()
    {
        return this.StackView(new IndexViewModel("Untitled Post"));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanManagePosts)]
    public async Task<IActionResult> SaveNew(IndexViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return this.StackView(model);
        }

        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var documentInDb = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == model.DocumentId);
        var isExistingDocument = documentInDb != null;

        if (documentInDb != null)
        {
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

    [Authorize(Policy = AppPermissionNames.CanManagePosts)]
    public async Task<IActionResult> Edit([Required][FromRoute] Guid id, [FromQuery] bool? saved = false)
    {
        var document = await context.MarkdownDocuments
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
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
            PublicLink = publicLink
        };

        return this.StackView(model: model, viewName: nameof(Editor));
    }

    /// <summary>
    /// AJAX quick save endpoint for Ctrl+S. Saves the document without page refresh.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AppPermissionNames.CanManagePosts)]
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

    [Authorize(Policy = AppPermissionNames.CanManagePosts)]
    [RenderInNavBar(
    NavGroupName = "Features",
    NavGroupOrder = 1,
    CascadedLinksGroupName = "Home",
    CascadedLinksIcon = "file-text",
    CascadedLinksOrder = 2,
    LinkText = "Posts",
    LinkOrder = 2)]
    public async Task<IActionResult> Posts([FromQuery] string? search)
    {
        var trimmedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var documentsQuery = context.MarkdownDocuments
            .Include(d => d.User)
            .AsQueryable();

        if (trimmedSearch != null)
        {
            documentsQuery = documentsQuery.Where(d =>
                (d.Title != null && d.Title.Contains(trimmedSearch)) ||
                (d.Content != null && d.Content.Contains(trimmedSearch)) ||
                d.User.DisplayName.Contains(trimmedSearch) ||
                (d.User.UserName != null && d.User.UserName.Contains(trimmedSearch)));
        }

        var documents = await documentsQuery
            .OrderByDescending(d => d.CreationTime)
            .ToListAsync();

        var model = new PostsViewModel
        {
            Posts = documents,
            SearchQuery = trimmedSearch
        };
        return this.StackView(model);
    }

    // GET: /Home/Delete/{guid}
    [Authorize(Policy = AppPermissionNames.CanManagePosts)]
    public async Task<IActionResult> Delete(Guid? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var document = await context.MarkdownDocuments
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
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
    [Authorize(Policy = AppPermissionNames.CanManagePosts)]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound();
        }

        context.MarkdownDocuments.Remove(document);
        await context.SaveChangesAsync();

        logger.LogInformation("Document with ID: '{Id}' was deleted by user: '{UserId}'.", id, userId);

        return RedirectToAction(nameof(Posts));
    }

    /// <summary>
    /// Make a document public.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AppPermissionNames.CanManagePosts)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MakePublic([Required][FromRoute] Guid id)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        if (!document.IsPublic)
        {
            document.IsPublic = true;
            await context.SaveChangesAsync();
            var userId = userManager.GetUserId(User);
            logger.LogInformation("Document with ID: '{DocumentId}' was made public by user: '{UserId}'.",
                id, userId);
        }

        return Ok();
    }

    /// <summary>
    /// Make a document private.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AppPermissionNames.CanManagePosts)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MakePrivate([Required][FromRoute] Guid id)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        if (document.IsPublic)
        {
            document.IsPublic = false;
            await context.SaveChangesAsync();
            var userId = userManager.GetUserId(User);
            logger.LogInformation("Document with ID: '{DocumentId}' was made private by user: '{UserId}'.",
                id, userId);
        }

        return Ok();
    }

    /// <summary>
    /// GET: Localization editor for a document. Shows all configured languages and their translations.
    /// </summary>
    [Authorize(Policy = AppPermissionNames.CanManagePosts)]
    [HttpGet]
    public async Task<IActionResult> Localize([Required][FromRoute] Guid id)
    {
        var document = await context.MarkdownDocuments
            .Include(d => d.LocalizedDocuments)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

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
    [Authorize(Policy = AppPermissionNames.CanManagePosts)]
    [HttpGet]
    public async Task<IActionResult> LocalizeData([Required] Guid id, [Required] string culture)
    {
        var document = await context.MarkdownDocuments
            .Include(d => d.LocalizedDocuments)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
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
    [Authorize(Policy = AppPermissionNames.CanManagePosts)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLocalization([Required] Guid documentId, [Required] string culture,
        string localizedTitle, string localizedContent)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

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

        var userId = userManager.GetUserId(User);
        logger.LogInformation(
            "User '{UserId}' manually updated localization for document '{DocumentId}' to culture '{Culture}'.",
            userId, documentId, culture);

        return Ok(new { success = true });
    }

    /// <summary>
    /// GET: Abstract localization editor for a document. Shows all configured languages and their abstracts.
    /// </summary>
    [Authorize(Policy = AppPermissionNames.CanManagePosts)]
    [HttpGet]
    public async Task<IActionResult> AbstractLocalize([Required][FromRoute] Guid id)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

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
    [Authorize(Policy = AppPermissionNames.CanManagePosts)]
    [HttpGet]
    public async Task<IActionResult> AbstractLocalizeData([Required] Guid id, [Required] string culture)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
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
    /// </summary>
    [Authorize(Policy = AppPermissionNames.CanManagePosts)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAbstractLocalization([Required] Guid documentId, [Required] string culture,
        string abstractText)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

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

            var userId = userManager.GetUserId(User);
            logger.LogInformation(
                "User '{UserId}' updated source abstract for document '{DocumentId}' ({Culture}). " +
                "{Count} other language abstract(s) invalidated for re-translation.",
                userId, documentId, culture, otherAbstracts.Count);
        }
        else
        {
            var userId = userManager.GetUserId(User);
            logger.LogInformation(
                "User '{UserId}' manually updated abstract for document '{DocumentId}' to culture '{Culture}'.",
                userId, documentId, culture);
        }

        await context.SaveChangesAsync();

        return Ok(new { success = true, isSourceCulture });
    }
}
