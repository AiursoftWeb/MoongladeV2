using Aiursoft.MoongladeV2.Authorization;
using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Models.ContractViewModels;
using Aiursoft.MoongladeV2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Aiursoft.MoongladeV2.Controllers;

/// <summary>
/// Contract template filling. Public documents are accessible to everyone;
/// private documents require content permission (CreateOrEditDraftDocument
/// or CreateEditOrPublishAnyDocument).
/// </summary>
[Route("contract/{id:guid}")]
public class ContractController(
    TemplateDbContext context,
    MoongladeV2Service mtohService,
    GlobalSettingsService globalSettingsService,
    IAuthorizationService authorizationService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Fill([Required][FromRoute] Guid id)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        if (!document.IsPublic && !await HasContentPermission())
        {
            return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();
        }

        var model = new ContractViewModel(document.Title ?? "Untitled Document")
        {
            DocumentId = document.Id,
            ShowPreview = false
        };

        await PopulateCompanySettings(model);
        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Fill([Required][FromRoute] Guid id, ContractViewModel model)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        if (!document.IsPublic && !await HasContentPermission())
        {
            return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();
        }

        model.Title = document.Title ?? "Untitled Document";
        model.PageTitle = $"{model.Title} - Contract";

        await PopulateCompanySettings(model);
        if (!ModelState.IsValid)
        {
            model.ShowPreview = false;
            return this.StackView(model);
        }

        model.ContentHtml = mtohService.ConvertMarkdownToHtml(document.Content ?? string.Empty);
        model.ShowPreview = true;
        return this.StackView(model, nameof(Fill));
    }

    private async Task PopulateCompanySettings(ContractViewModel model)
    {
        model.LogoUrl = await globalSettingsService.GetContractLogoUrlAsync();
        model.ShowContractHeader = await globalSettingsService.GetBoolSettingAsync(SettingsMap.ShowContractHeader);
        model.CompanyAddress = await globalSettingsService.GetSettingValueAsync(SettingsMap.CompanyAddress);
        model.CompanyPhone = await globalSettingsService.GetSettingValueAsync(SettingsMap.CompanyPhone);
        model.CompanyEmail = await globalSettingsService.GetSettingValueAsync(SettingsMap.CompanyEmail);
        model.CompanyPostcode = await globalSettingsService.GetSettingValueAsync(SettingsMap.CompanyPostcode);
    }

    private async Task<bool> HasContentPermission()
    {
        return (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CreateEditOrPublishAnyDocument)).Succeeded ||
               (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CreateOrEditDraftDocument)).Succeeded;
    }
}
