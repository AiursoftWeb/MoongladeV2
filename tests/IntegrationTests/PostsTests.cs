using System.Net;
using Aiursoft.MoongladeV2.Authorization;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Tests.IntegrationTests;

[TestClass]
public class PostsTests : TestBase
{
    [TestMethod]
    public async Task DraftOnlyUser_CanCreateDraft()
    {
        var (email, password) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email, AppPermissionNames.CreateEditOrDeleteDraftDocument);
        await ReloginAsync(email, password);

        var response = await PostForm("/Home/SaveNew", new Dictionary<string, string>
        {
            { "DocumentId", Guid.NewGuid().ToString() },
            { "Title", "Employee Draft" },
            { "InputMarkdown", "# Draft content" }
        }, tokenUrl: "/Home/Editor");

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var document = await db.MarkdownDocuments.SingleAsync(d => d.Title == "Employee Draft");
        Assert.IsFalse(document.IsPublic);
    }

    [TestMethod]
    public async Task DraftOnlyUser_CannotPublish()
    {
        var (email, password) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email, AppPermissionNames.CreateEditOrDeleteDraftDocument);
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
    public async Task DraftOnlyUser_CanEditAnyDraft()
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
                Title = "Boss Draft",
                Content = "# Boss content",
                UserId = user.Id,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Login as draft-only user 2, try to edit user 1's post
        var (email2, password2) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email2, AppPermissionNames.CreateEditOrDeleteDraftDocument);
        await ReloginAsync(email2, password2);

        var response = await Http.GetAsync($"/Home/Edit/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "Draft-only user should be able to edit any company draft, regardless of author.");

        var saveResponse = await PostForm("/Home/SaveUpdate", new Dictionary<string, string>
        {
            { "DocumentId", docId.ToString() },
            { "Title", "Edited Company Draft" },
            { "InputMarkdown", "# Edited by another employee" }
        }, tokenUrl: $"/Home/Edit/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, saveResponse.StatusCode);

        using var verifyScope = Server!.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var updated = await verifyDb.MarkdownDocuments.FindAsync(docId);
        Assert.AreEqual("Edited Company Draft", updated!.Title);
    }

    [TestMethod]
    public async Task DraftOnlyUser_CanDeleteAnyDraft()
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
                Title = "Boss Draft to Delete",
                Content = "# Content",
                UserId = user.Id,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Login as draft-only user 2, try to delete user 1's post
        var (email2, password2) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email2, AppPermissionNames.CreateEditOrDeleteDraftDocument);
        await ReloginAsync(email2, password2);

        var deletePage = await Http.GetAsync($"/Home/Delete/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, deletePage.StatusCode,
            "Draft-only user should see delete confirmation page");

        var deleteResponse = await PostForm($"/Home/Delete/{docId}", new(), tokenUrl: $"/Home/Delete/{docId}");
        Assert.AreEqual(HttpStatusCode.Found, deleteResponse.StatusCode,
            "Draft-only user should be able to delete any company draft");
    }

    [TestMethod]
    public async Task DraftOnlyUser_CannotEditPublishedPost()
    {
        var (ownerEmail, _) = await RegisterAndLoginAsync();
        var docId = await CreateDocumentAsync(ownerEmail, isPublic: true, "Protected Published Post");

        var (employeeEmail, employeePassword) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(employeeEmail, AppPermissionNames.CreateEditOrDeleteDraftDocument);
        await ReloginAsync(employeeEmail, employeePassword);

        var editResponse = await Http.GetAsync($"/Home/Edit/{docId}");
        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);

        var saveResponse = await PostForm("/Home/SaveUpdate", new Dictionary<string, string>
        {
            { "DocumentId", docId.ToString() },
            { "Title", "Unauthorized Published Edit" },
            { "InputMarkdown", "# This must not become public" }
        }, tokenUrl: "/Home/Editor");
        Assert.AreEqual(HttpStatusCode.Found, saveResponse.StatusCode);

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var document = await db.MarkdownDocuments.AsNoTracking().SingleAsync(d => d.Id == docId);
        Assert.AreEqual("Protected Published Post", document.Title);
        Assert.AreEqual("# Original content", document.Content);
        Assert.IsTrue(document.IsPublic);
    }

    [TestMethod]
    public async Task DraftOnlyUser_CannotDeletePublishedPost()
    {
        var (ownerEmail, _) = await RegisterAndLoginAsync();
        var docId = await CreateDocumentAsync(ownerEmail, isPublic: true, "Published Post to Keep");

        var (employeeEmail, employeePassword) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(employeeEmail, AppPermissionNames.CreateEditOrDeleteDraftDocument);
        await ReloginAsync(employeeEmail, employeePassword);

        var deletePage = await Http.GetAsync($"/Home/Delete/{docId}");
        Assert.AreEqual(HttpStatusCode.Found, deletePage.StatusCode);

        var deleteResponse = await PostForm(
            $"/Home/Delete/{docId}",
            new(),
            tokenUrl: "/Home/Editor");
        Assert.AreEqual(HttpStatusCode.Found, deleteResponse.StatusCode);

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.IsTrue(await db.MarkdownDocuments.AnyAsync(d => d.Id == docId));
    }

    [TestMethod]
    public async Task DraftOnlyUser_CannotUnpublishPublishedPost()
    {
        var (ownerEmail, _) = await RegisterAndLoginAsync();
        var docId = await CreateDocumentAsync(ownerEmail, isPublic: true, "Published Post to Keep Public");

        var (employeeEmail, employeePassword) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(employeeEmail, AppPermissionNames.CreateEditOrDeleteDraftDocument);
        await ReloginAsync(employeeEmail, employeePassword);

        var response = await PostForm(
            $"/Home/MakePrivate/{docId}",
            new(),
            tokenUrl: "/Home/Editor");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var document = await db.MarkdownDocuments.AsNoTracking().SingleAsync(d => d.Id == docId);
        Assert.IsTrue(document.IsPublic);
    }

    [TestMethod]
    public async Task SaveNew_WithExistingDocumentId_ReturnsConflictWithoutUpdating()
    {
        var (email, password) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email, AppPermissionNames.CreateEditOrPublishAnyDocument);
        await ReloginAsync(email, password);
        var docId = await CreateDocumentAsync(email, isPublic: false, "Existing Draft");

        var response = await PostForm("/Home/SaveNew", new Dictionary<string, string>
        {
            { "DocumentId", docId.ToString() },
            { "Title", "Overwrite Attempt" },
            { "InputMarkdown", "# Overwrite attempt" }
        }, tokenUrl: "/Home/Editor");
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var document = await db.MarkdownDocuments.AsNoTracking().SingleAsync(d => d.Id == docId);
        Assert.AreEqual("Existing Draft", document.Title);
        Assert.AreEqual("# Original content", document.Content);
    }

    [TestMethod]
    public async Task DraftOnlyUser_SeesPublishedPostsWithoutEditOrDeleteActions()
    {
        var (email, password) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email, AppPermissionNames.CreateEditOrDeleteDraftDocument);
        await ReloginAsync(email, password);
        var publishedId = await CreateDocumentAsync(email, isPublic: true, "Read-only Published Post");
        var draftId = await CreateDocumentAsync(email, isPublic: false, "Editable Company Draft");

        var postsHtml = await Http.GetStringAsync("/Home/Posts");
        StringAssert.Contains(postsHtml, "Read-only Published Post");
        StringAssert.Contains(postsHtml, $"/Home/Edit/{draftId}");
        Assert.IsFalse(postsHtml.Contains($"/Home/Edit/{publishedId}", StringComparison.Ordinal));
        Assert.IsFalse(postsHtml.Contains($"/Home/Delete/{publishedId}", StringComparison.Ordinal));

        var publicViewHtml = await Http.GetStringAsync($"/share/{publishedId}");
        Assert.IsFalse(publicViewHtml.Contains($"/Home/Edit/{publishedId}", StringComparison.Ordinal));
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
        await GrantPermissionToUser(email1, AppPermissionNames.CreateEditOrDeleteDraftDocument);
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
    public async Task PublishAnyUser_CanEditPublishedPost()
    {
        // User 1 creates a company post that is already published.
        var (email1, _) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email1, AppPermissionNames.CreateEditOrDeleteDraftDocument);
        var docId = Guid.NewGuid();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email1);
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = docId,
                Title = "Published Post for Boss",
                Content = "# Edit me",
                UserId = user.Id,
                IsPublic = true,
                CreationTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // User 2 (publish-any) edits user 1's published post.
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
    public async Task PublishAnyUser_CanDeletePublishedPost()
    {
        var (email, password) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email, AppPermissionNames.CreateEditOrPublishAnyDocument);
        await ReloginAsync(email, password);
        var docId = await CreateDocumentAsync(email, isPublic: true, "Published Post to Delete");

        var response = await PostForm(
            $"/Home/Delete/{docId}",
            new(),
            tokenUrl: $"/Home/Delete/{docId}");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.IsFalse(await db.MarkdownDocuments.AnyAsync(d => d.Id == docId));
    }

    [TestMethod]
    public async Task PostsPage_ShowsAllDocuments_ToBothPermissions()
    {
        // Create a document as user 1
        var (email1, password1) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email1, AppPermissionNames.CreateEditOrDeleteDraftDocument);
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

        var blogResponse = await anonHttp.GetAsync($"/post/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, blogResponse.StatusCode,
            "Anyone should be able to view a public post through its blog URL");
        var blogHtml = await blogResponse.Content.ReadAsStringAsync();
        StringAssert.Contains(blogHtml, "Public News");
        StringAssert.Contains(blogHtml, $"rel=\"canonical\" href=\"{new Uri(anonHttp.BaseAddress!, $"post/{docId}")}\"");
    }

    [TestMethod]
    public async Task PublicPostLinks_PreferDatedSlug_AndKeepGuidFallback()
    {
        var sluggedId = Guid.NewGuid();
        var fallbackId = Guid.NewGuid();
        var creationTime = new DateTime(2026, 6, 28, 12, 34, 56, DateTimeKind.Utc);
        const string slug = "something-cool";
        const string title = "SEO Link Integration Post";
        const string tag = "seo-link-test";
        var seoUrl = $"/post/2026/06/28/{slug}";

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var user = await db.Users.FirstAsync();
            db.MarkdownDocuments.AddRange(
                new MarkdownDocument
                {
                    Id = sluggedId,
                    Title = title,
                    Content = "Searchable SEO content",
                    Tags = tag,
                    UserId = user.Id,
                    IsPublic = true,
                    Slug = slug,
                    SlugDate = creationTime.Date,
                    CreationTime = creationTime
                },
                new MarkdownDocument
                {
                    Id = fallbackId,
                    Title = "Post Awaiting Slug",
                    Content = "Fallback content",
                    UserId = user.Id,
                    IsPublic = true,
                    CreationTime = creationTime.AddMinutes(-1)
                });
            await db.SaveChangesAsync();
        }

        foreach (var entryPoint in new[] { "/", $"/search?q={Uri.EscapeDataString(title)}", $"/tags/{tag}", "/archive" })
        {
            var html = await Http.GetStringAsync(entryPoint);
            StringAssert.Contains(html, $"href=\"{seoUrl}\"", $"Expected SEO URL on {entryPoint}");
            Assert.IsFalse(html.Contains($"href=\"/post/{sluggedId}\"", StringComparison.Ordinal),
                $"GUID URL must not be emitted for a slugged post on {entryPoint}");
        }

        var homeHtml = await Http.GetStringAsync("/");
        StringAssert.Contains(homeHtml, $"href=\"/post/{fallbackId}\"");

        var redirect = await Http.GetAsync($"/post/{sluggedId}");
        Assert.AreEqual(HttpStatusCode.MovedPermanently, redirect.StatusCode);
        Assert.AreEqual(seoUrl, redirect.Headers.Location?.OriginalString);

        var seoResponse = await Http.GetAsync(seoUrl);
        Assert.AreEqual(HttpStatusCode.OK, seoResponse.StatusCode);
        var seoHtml = await seoResponse.Content.ReadAsStringAsync();
        StringAssert.Contains(seoHtml, title);
        StringAssert.Contains(seoHtml,
            $"rel=\"canonical\" href=\"{new Uri(Http.BaseAddress!, seoUrl.TrimStart('/'))}\"");

        var fallbackResponse = await Http.GetAsync($"/post/{fallbackId}");
        Assert.AreEqual(HttpStatusCode.OK, fallbackResponse.StatusCode);
        var fallbackHtml = await fallbackResponse.Content.ReadAsStringAsync();
        StringAssert.Contains(fallbackHtml,
            $"rel=\"canonical\" href=\"{new Uri(Http.BaseAddress!, $"post/{fallbackId}")}\"");
    }

    private async Task<Guid> CreateDocumentAsync(string ownerEmail, bool isPublic, string title)
    {
        var docId = Guid.NewGuid();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var owner = await db.Users.FirstAsync(u => u.Email == ownerEmail);
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = docId,
            Title = title,
            Content = "# Original content",
            UserId = owner.Id,
            IsPublic = isPublic,
            CreationTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return docId;
    }
}
