using System.Net;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Tests.IntegrationTests;

[TestClass]
public class GuestCommentTests : TestBase
{
    private readonly List<Guid> createdDocumentIds = [];

    [TestCleanup]
    public override async Task CleanServer()
    {
        if (Server != null)
        {
            using var scope = Server.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var documents = await db.MarkdownDocuments
                .Where(d => createdDocumentIds.Contains(d.Id))
                .ToListAsync();
            db.MarkdownDocuments.RemoveRange(documents);
            await db.SaveChangesAsync();
        }

        await base.CleanServer();
    }

    [TestMethod]
    public async Task AnonymousVisitor_CanPostComment_WithGuestNameAndLongPureContent()
    {
        var documentId = await CreatePublicDocumentAsync();
        var content = new string('x', 1500);

        var response = await PostForm("/Comments/Post", new Dictionary<string, string>
        {
            ["documentId"] = documentId.ToString(),
            ["guestName"] = "  Pure Guest  ",
            ["content"] = content
        }, tokenUrl: $"/post/{documentId}");

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var comment = await db.Comments.AsNoTracking().SingleAsync(c => c.DocumentId == documentId);
        Assert.IsNull(comment.UserId);
        Assert.AreEqual("Pure Guest", comment.GuestName);
        Assert.AreEqual(content, comment.Content);

        var postResponse = await Http.GetAsync($"/post/{documentId}");
        postResponse.EnsureSuccessStatusCode();
        StringAssert.Contains(await postResponse.Content.ReadAsStringAsync(), "Pure Guest");
    }

    [TestMethod]
    public async Task AnonymousVisitor_WithoutGuestName_IsRejected()
    {
        var documentId = await CreatePublicDocumentAsync();
        var response = await PostForm("/Comments/Post", new Dictionary<string, string>
        {
            ["documentId"] = documentId.ToString(),
            ["guestName"] = " ",
            ["content"] = "Anonymous comment"
        }, tokenUrl: $"/post/{documentId}");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Guid> CreatePublicDocumentAsync()
    {
        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var admin = await userManager.FindByEmailAsync("admin@default.com")
                    ?? throw new InvalidOperationException("Seeded admin user was not found.");
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var document = new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            UserId = admin.Id,
            Title = "Guest comment target",
            Content = "Post body",
            IsPublic = true
        };
        db.MarkdownDocuments.Add(document);
        await db.SaveChangesAsync();
        createdDocumentIds.Add(document.Id);
        return document.Id;
    }
}
