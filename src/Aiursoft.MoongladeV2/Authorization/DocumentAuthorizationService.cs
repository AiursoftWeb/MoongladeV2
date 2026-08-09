using System.Security.Claims;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.Scanner.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace Aiursoft.MoongladeV2.Authorization;

public enum DocumentAccessLevel
{
    None,
    Draft,
    Full
}

/// <summary>
/// Resolves content permissions without considering document ownership.
/// Documents belong to the company; their publication state determines whether
/// a draft-only user may modify them.
/// </summary>
public class DocumentAuthorizationService(IAuthorizationService authorizationService) : IScopedDependency
{
    public async Task<DocumentAccessLevel> GetAccessLevelAsync(ClaimsPrincipal user)
    {
        if ((await authorizationService.AuthorizeAsync(
                user,
                AppPermissionNames.CreateEditOrPublishAnyDocument)).Succeeded)
        {
            return DocumentAccessLevel.Full;
        }

        if ((await authorizationService.AuthorizeAsync(
                user,
                AppPermissionNames.CreateEditOrDeleteDraftDocument)).Succeeded)
        {
            return DocumentAccessLevel.Draft;
        }

        return DocumentAccessLevel.None;
    }

    public static bool CanModify(DocumentAccessLevel accessLevel, MarkdownDocument document)
    {
        return accessLevel == DocumentAccessLevel.Full ||
               accessLevel == DocumentAccessLevel.Draft && !document.IsPublic;
    }
}
