using System.Net;
using Aiursoft.MoongladeV2.Authorization;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Tests.IntegrationTests;

[TestClass]
public class PostsTests : TestBase
{
    [TestMethod]
    public async Task DraftOnlyUser_CanCreatePost()
    {
        var (email, password) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email, AppPermissionNames.CreateOrEditDraftDocument);
        await ReloginAsync(email, password);

        var response = await Http.GetAsync("/Home/Editor");
        response.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task DraftOnlyUser_CannotPublish()
    {
        var (email, password) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email, AppPermissionNames.CreateOrEditDraftDocument);
        await ReloginAsync(email, password);

        // Create a document
        var docId = Guid.NewGuid();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email);
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = docId,
                Title = "Draft Post",
                Content = "# Draft",
                UserId = user.Id,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Try to make public — should redirect to access denied (Forbid triggers redirect)
        var response = await PostForm($"/Home/MakePublic/{docId}", new(), tokenUrl: $"/Home/Edit/{docId}");
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
    }

    [TestMethod]
    public async Task DraftOnlyUser_CanEditAnyPost()
    {
        // Create post as user 1
        var (email1, _) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email1, AppPermissionNames.CreateEditOrPublishAnyDocument);
        var docId = Guid.NewGuid();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email1);
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = docId,
                Title = "Boss Post",
                Content = "# Boss content",
                UserId = user.Id,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Login as draft-only user 2, try to edit user 1's post
        var (email2, password2) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email2, AppPermissionNames.CreateOrEditDraftDocument);
        await ReloginAsync(email2, password2);

        var response = await Http.GetAsync($"/Home/Edit/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Draft-only user should be able to edit any post (all posts are company property)");
    }

    [TestMethod]
    public async Task DraftOnlyUser_CanDeleteAnyPost()
    {
        // Create post as user 1 (publish-any user)
        var (email1, _) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email1, AppPermissionNames.CreateEditOrPublishAnyDocument);
        var docId = Guid.NewGuid();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email1);
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = docId,
                Title = "Boss Post to Delete",
                Content = "# Content",
                UserId = user.Id,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Login as draft-only user 2, try to delete user 1's post
        var (email2, password2) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email2, AppPermissionNames.CreateOrEditDraftDocument);
        await ReloginAsync(email2, password2);

        var deletePage = await Http.GetAsync($"/Home/Delete/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, deletePage.StatusCode,
            "Draft-only user should see delete confirmation page");

        var deleteResponse = await PostForm($"/Home/Delete/{docId}", new(), tokenUrl: $"/Home/Delete/{docId}");
        Assert.AreEqual(HttpStatusCode.Found, deleteResponse.StatusCode,
            "Draft-only user should be able to delete any post");
    }

    [TestMethod]
    public async Task PublishAnyUser_CanPublishOwnPost()
    {
        var (email, password) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email, AppPermissionNames.CreateEditOrPublishAnyDocument);
        await ReloginAsync(email, password);

        var docId = Guid.NewGuid();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email);
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = docId,
                Title = "My Post",
                Content = "# Mine",
                UserId = user.Id,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await PostForm($"/Home/MakePublic/{docId}", new(), tokenUrl: $"/Home/Edit/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var doc = await db.MarkdownDocuments.FindAsync(docId);
            Assert.IsTrue(doc!.IsPublic);
        }
    }

    [TestMethod]
    public async Task PublishAnyUser_CanPublishOthersDraft()
    {
        // User 1 (draft-only) creates a draft
        var (email1, _) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email1, AppPermissionNames.CreateOrEditDraftDocument);
        var docId = Guid.NewGuid();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email1);
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = docId,
                Title = "Junior's Draft",
                Content = "# Draft",
                UserId = user.Id,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // User 2 (publish-any) publishes user 1's draft
        var (email2, password2) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email2, AppPermissionNames.CreateEditOrPublishAnyDocument);
        await ReloginAsync(email2, password2);

        var response = await PostForm($"/Home/MakePublic/{docId}", new(), tokenUrl: $"/Home/Edit/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Publish-any user should be able to publish another user's draft");

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var doc = await db.MarkdownDocuments.FindAsync(docId);
            Assert.IsTrue(doc!.IsPublic);
        }
    }

    [TestMethod]
    public async Task PublishAnyUser_CanUnpublishPublishedPost()
    {
        var (email, password) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email, AppPermissionNames.CreateEditOrPublishAnyDocument);
        await ReloginAsync(email, password);

        var docId = Guid.NewGuid();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email);
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = docId,
                Title = "Public Post",
                Content = "# Public",
                UserId = user.Id,
                IsPublic = true,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await PostForm($"/Home/MakePrivate/{docId}", new(), tokenUrl: $"/Home/Edit/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var doc = await db.MarkdownDocuments.FindAsync(docId);
            Assert.IsFalse(doc!.IsPublic);
        }
    }

    [TestMethod]
    public async Task PublishAnyUser_CanEditAnyPost()
    {
        // User 1 (draft-only) creates a draft
        var (email1, _) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email1, AppPermissionNames.CreateOrEditDraftDocument);
        var docId = Guid.NewGuid();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email1);
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = docId,
                Title = "Draft for Boss",
                Content = "# Edit me",
                UserId = user.Id,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // User 2 (publish-any) edits user 1's draft
        var (email2, password2) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email2, AppPermissionNames.CreateEditOrPublishAnyDocument);
        await ReloginAsync(email2, password2);

        var editPage = await Http.GetAsync($"/Home/Edit/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, editPage.StatusCode);

        // Save update
        var saveResponse = await PostForm("/Home/SaveUpdate", new Dictionary<string, string>
        {
            { "DocumentId", docId.ToString() },
            { "Title", "Edited by Boss" },
            { "InputMarkdown", "# Boss was here" }
        });
        saveResponse.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task PostsPage_ShowsAllDocuments_ToBothPermissions()
    {
        // Create a document as user 1
        var (email1, password1) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email1, AppPermissionNames.CreateOrEditDraftDocument);
        await ReloginAsync(email1, password1);

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email1);
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = Guid.NewGuid(),
                Title = "Junior Draft",
                Content = "# Junior",
                UserId = user.Id,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Create a document as user 2
        var (email2, password2) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email2, AppPermissionNames.CreateEditOrPublishAnyDocument);
        await ReloginAsync(email2, password2);

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email2);
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = Guid.NewGuid(),
                Title = "Boss Draft",
                Content = "# Boss",
                UserId = user.Id,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // User 2 (publish-any) sees all posts
        var response = await Http.GetAsync("/Home/Posts");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("Junior Draft"), "Posts page should show junior's draft");
        Assert.IsTrue(html.Contains("Boss Draft"), "Posts page should show boss's draft");
    }

    [TestMethod]
    public async Task Posts_SearchWithPercentSign_ReturnsOkAndOnlyMatchingDocs()
    {
        var (email, password) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email, AppPermissionNames.CreateEditOrPublishAnyDocument);
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
        Assert.IsTrue(html.Contains("50% complete"));
        Assert.IsFalse(html.Contains("Regular document"));
    }

    [TestMethod]
    public async Task Visitor_WithoutPermission_CannotAccessEditor()
    {
        await RegisterAndLoginAsync();
        // No permissions granted

        var response = await Http.GetAsync("/Home/Editor");
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode,
            "User without content permission should be redirected (Forbid)");
    }

    [TestMethod]
    public async Task Visitor_WithoutPermission_CannotAccessPosts()
    {
        await RegisterAndLoginAsync();
        // No permissions granted

        var response = await Http.GetAsync("/Home/Posts");
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode,
            "User without content permission should be redirected (Forbid)");
    }

    [TestMethod]
    public async Task Visitor_WithoutPermission_CannotViewPrivateDraft()
    {
        var (email, password) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email, AppPermissionNames.CreateEditOrPublishAnyDocument);
        await ReloginAsync(email, password);

        // Create a private draft
        var docId = Guid.NewGuid();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email);
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = docId,
                Title = "Secret Draft",
                Content = "# Top Secret",
                UserId = user.Id,
                IsPublic = false,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Now login as a different user with NO permissions
        _ = await RegisterAndLoginAsync();
        // No permissions granted

        var response = await Http.GetAsync($"/share/{docId}");
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode,
            "Visitor without content permission should be forbidden from viewing private drafts");
    }

    [TestMethod]
    public async Task Visitor_CanViewPublicPost()
    {
        var (email, password) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email, AppPermissionNames.CreateEditOrPublishAnyDocument);
        await ReloginAsync(email, password);

        // Create and publish a post
        var docId = Guid.NewGuid();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email);
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = docId,
                Title = "Public News",
                Content = "# News",
                UserId = user.Id,
                IsPublic = true,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Anonymous user can view
        // ReSharper disable once ShortLivedHttpClient
        using var anonHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        anonHttp.BaseAddress = Http.BaseAddress;

        var response = await anonHttp.GetAsync($"/share/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Anyone should be able to view a public post, even anonymously");
    }
}
