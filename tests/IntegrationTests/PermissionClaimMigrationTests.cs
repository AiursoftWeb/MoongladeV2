using System.Net;
using System.Security.Claims;
using Aiursoft.MoongladeV2.Authorization;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.MoongladeV2.Tests.IntegrationTests;

[TestClass]
public class PermissionClaimMigrationTests : TestBase
{
    private const string LegacyDraftPermission = "CreateOrEditDraftDocument";

    [TestMethod]
    public async Task LegacyDraftPermission_RemainsAuthorizedAndMigratesToNewClaim()
    {
        var (email, password) = await RegisterAndLoginAsync();
        const string roleName = "Legacy Draft Editors";

        using (var scope = Server!.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var role = new IdentityRole(roleName);
            Assert.IsTrue((await roleManager.CreateAsync(role)).Succeeded);
            Assert.IsTrue((await roleManager.AddClaimAsync(
                role,
                new Claim(AppPermissions.Type, LegacyDraftPermission))).Succeeded);

            var user = await userManager.FindByEmailAsync(email);
            Assert.IsTrue((await userManager.AddToRoleAsync(user!, roleName)).Succeeded);
        }

        await ReloginAsync(email, password);
        var compatibilityResponse = await Http.GetAsync("/Home/Editor");
        Assert.AreEqual(HttpStatusCode.OK, compatibilityResponse.StatusCode);

        await Server!.SeedAsync();

        using var verifyScope = Server.Services.CreateScope();
        var verifyRoleManager = verifyScope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var migratedRole = await verifyRoleManager.FindByNameAsync(roleName);
        var claims = await verifyRoleManager.GetClaimsAsync(migratedRole!);
        Assert.IsTrue(claims.Any(c => c.Type == AppPermissions.Type &&
                                      c.Value == AppPermissionNames.CreateEditOrDeleteDraftDocument));
        Assert.IsFalse(claims.Any(c => c.Type == AppPermissions.Type &&
                                       c.Value == LegacyDraftPermission));
    }
}
