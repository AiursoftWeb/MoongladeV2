using System.Net;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Tests.IntegrationTests;

[TestClass]
public class AdminCommentsTests : TestBase
{
    [TestMethod]
    public async Task Comments_WithRootAndReply_RendersSafeFullTextModalEntriesWithoutApprovalControls()
    {
        await LoginAsAdmin();

        const string rootContent = "Root first line\nRoot second line <script>alert(1)</script>";
        const string replyContent = "Reply first line\nReply second line & more";
        const string replyAuthor = "Reply author";
        using (var scope = Server!.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var admin = await userManager.FindByEmailAsync("admin@default.com")
                        ?? throw new InvalidOperationException("Seeded admin user was not found.");
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var document = new MarkdownDocument
            {
                UserId = admin.Id,
                Title = "Post with comments"
            };
            var rootComment = new Comment
            {
                Document = document,
                UserId = admin.Id,
                Content = rootContent
            };
            var replyUser = new User
            {
                UserName = $"reply-{Guid.NewGuid():N}",
                Email = $"reply-{Guid.NewGuid():N}@example.com",
                DisplayName = replyAuthor
            };
            var createUserResult = await userManager.CreateAsync(replyUser);
            Assert.IsTrue(createUserResult.Succeeded,
                string.Join(Environment.NewLine, createUserResult.Errors.Select(e => e.Description)));

            db.Comments.AddRange(rootComment, new Comment
            {
                Document = document,
                UserId = replyUser.Id,
                ParentComment = rootComment,
                Content = replyContent
            });
            await db.SaveChangesAsync();
        }

        var response = await Http.GetAsync("/Admin/Comments");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(html, replyAuthor);
        Assert.AreEqual(2, CountOccurrences(html, "data-bs-target=\"#commentContentModal\""));
        StringAssert.Contains(html, $"data-comment-content=\"{EncodeHtmlAttribute(rootContent)}\"");
        StringAssert.Contains(html, $"data-comment-content=\"{EncodeHtmlAttribute(replyContent)}\"");
        StringAssert.Contains(html, "id=\"commentContentModal\"");
        StringAssert.Contains(html, "white-space: pre-wrap");
        StringAssert.Contains(html, "fullText.textContent");
        Assert.IsFalse(html.Contains("<script>alert(1)</script>", StringComparison.Ordinal));
        Assert.IsFalse(html.Contains("ToggleApproval", StringComparison.Ordinal));
        Assert.IsFalse(html.Contains(">Approved<", StringComparison.Ordinal));
        Assert.IsFalse(html.Contains(">Pending<", StringComparison.Ordinal));
        Assert.IsFalse(html.Contains(">Status<", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Comment_IsVisibleOnPublicPostWithoutApproval()
    {
        const string commentContent = "Visible without approval";
        Guid documentId;
        using (var scope = Server!.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var admin = await userManager.FindByEmailAsync("admin@default.com")
                        ?? throw new InvalidOperationException("Seeded admin user was not found.");
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var document = new MarkdownDocument
            {
                Id = Guid.NewGuid(),
                UserId = admin.Id,
                Title = "Public post with comment",
                Content = "Post body",
                IsPublic = true
            };
            documentId = document.Id;
            db.Comments.Add(new Comment
            {
                Document = document,
                UserId = admin.Id,
                Content = commentContent
            });
            await db.SaveChangesAsync();
        }

        var response = await Http.GetAsync($"/post/{documentId}");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(html, commentContent);

        using var cleanupScope = Server!.Services.CreateScope();
        var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var documentToDelete = await cleanupDb.MarkdownDocuments.FindAsync(documentId);
        cleanupDb.MarkdownDocuments.Remove(documentToDelete!);
        await cleanupDb.SaveChangesAsync();
    }

    [TestMethod]
    public async Task GlobalSettings_RemovesObsoleteCommentReviewSetting()
    {
        const string obsoleteKey = "RequireCommentReview";
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.GlobalSettings.Add(new GlobalSetting { Key = obsoleteKey, Value = "True" });
            await db.SaveChangesAsync();

            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            await settingsService.SeedSettingsAsync();

            Assert.IsFalse(await db.GlobalSettings.AnyAsync(s => s.Key == obsoleteKey));
        }

        await LoginAsAdmin();
        var response = await Http.GetAsync("/GlobalSettings");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(html.Contains(obsoleteKey, StringComparison.Ordinal));
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value).Length - 1;

    private static string EncodeHtmlAttribute(string value) =>
        WebUtility.HtmlEncode(value).Replace("\n", "&#xA;", StringComparison.Ordinal);
}
