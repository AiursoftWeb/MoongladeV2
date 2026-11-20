using System.Net;
using Aiursoft.MoongladeV2.Authorization;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Tests.IntegrationTests;

[TestClass]
public class HomeControllerTests : TestBase
{
    [TestMethod]
    public async Task GetIndex()
    {
        var url = "/";
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
    }

    // Bug 1 (log order) is a pure observability issue with no user-facing behavior change.
    // There is no integration test that can fail because of a wrong log message,
    // so it is fixed directly in the code without a corresponding failing test.

    // Bug 2: SaveUpdate should return 404 when the document does not exist.
    // Currently it silently creates a new document instead — this test will FAIL until fixed.
    [TestMethod]
    public async Task SaveUpdate_WithNonExistentDocumentId_ReturnsNotFound()
    {
        var (email, password) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email, AppPermissionNames.CreateEditOrPublishAnyDocument);
        await ReloginAsync(email, password);
        var nonExistentId = Guid.NewGuid();

        var response = await PostForm("/Home/SaveUpdate", new Dictionary<string, string>
        {
            { "DocumentId", nonExistentId.ToString() },
            { "Title", "Ghost document" },
            { "InputMarkdown", "# Should not be created" }
        });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    [DataRow("Original title", "# Original text", false)]
    [DataRow("Translated title", "# Original text", true)]
    [DataRow("Original title", "# Translated text", true)]
    public async Task SaveUpdate_ReclassifiesOnlyWhenTheSourceChanges(string title, string content, bool changed)
    {
        var (email, password) = await RegisterAndLoginAsync();
        await GrantPermissionToUser(email, AppPermissionNames.CreateEditOrPublishAnyDocument);
        await ReloginAsync(email, password);
        var documentId = Guid.NewGuid();
        var originalRevision = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var owner = await db.Users.SingleAsync(user => user.Email == email);
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = documentId,
                Title = "Original title",
                Content = "# Original text",
                UserId = owner.Id,
                IsPublic = true,
                SourceCulture = "zh-CN",
                UpdatedAt = originalRevision
            });
            await db.SaveChangesAsync();
        }

        var response = await PostForm("/Home/SaveUpdate", new Dictionary<string, string>
        {
            { "DocumentId", documentId.ToString() },
            { "Title", title },
            { "InputMarkdown", content }
        }, tokenUrl: $"/Home/Edit/{documentId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var resultScope = Server!.Services.CreateScope();
        var resultDb = resultScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var document = await resultDb.MarkdownDocuments.SingleAsync(d => d.Id == documentId);
        Assert.AreEqual(title, document.Title);
        Assert.AreEqual(content, document.Content);
        Assert.AreEqual(changed ? null : "zh-CN", document.SourceCulture);
        if (changed)
        {
            Assert.IsTrue(document.UpdatedAt > originalRevision);
        }
        else
        {
            Assert.AreEqual(originalRevision, document.UpdatedAt);
        }
    }
}
