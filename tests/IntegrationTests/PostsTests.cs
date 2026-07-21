using System.Net;
using Aiursoft.MoongladeV2.Authorization;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Tests.IntegrationTests;

[TestClass]
public class PostsTests : TestBase
{
    [TestMethod]
    public async Task GetPosts()
    {
        var (email, password) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email, AppPermissionNames.CanManagePosts);
        await ReloginAsync(email, password);
        var url = "/Home/Posts";

        var response = await Http.GetAsync(url);

        response.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task Posts_SearchWithPercentSign_OnlyReturnsDocumentsContainingLiteralPercent()
    {
        var (email, password) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email, AppPermissionNames.CanManagePosts);
        await ReloginAsync(email, password);

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == email);

        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "50% complete",
            Content = "has percent",
            UserId = user.Id,
            CreationTime = DateTime.UtcNow
        });
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "Regular document",
            Content = "no special chars",
            UserId = user.Id,
            CreationTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var response = await Http.GetAsync("/Home/Posts?search=%25");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("50% complete"), "Document with % in title must appear in results");
        Assert.IsFalse(html.Contains("Regular document"), "Document without % must NOT appear when searching for %");
    }

    [TestMethod]
    public async Task Posts_ShowsAllDocuments_FromAllUsers()
    {
        var (email1, password1) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email1, AppPermissionNames.CanManagePosts);

        // Create a document as user 1
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email1);
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = Guid.NewGuid(),
                Title = "User 1 Document",
                Content = "Content from user 1",
                UserId = user.Id,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await ReloginAsync(email1, password1);

        // Create a second user and their document
        var (email2, password2) = await RegisterAndLoginAsync();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email2);
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = Guid.NewGuid(),
                Title = "User 2 Document",
                Content = "Content from user 2",
                UserId = user.Id,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Login back as user 1 (who has CanManagePosts) and check Posts page
        await ReloginAsync(email1, password1);

        var response = await Http.GetAsync("/Home/Posts");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("User 1 Document"), "Posts page must show user 1's document");
        Assert.IsTrue(html.Contains("User 2 Document"), "Posts page must show user 2's document (all users' docs)");
    }
}
