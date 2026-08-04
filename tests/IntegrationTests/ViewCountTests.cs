using System.Net;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Services;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Tests.IntegrationTests;

[TestClass]
public class ViewCountTests : TestBase
{
    [TestMethod]
    public async Task PublicPost_IsCountedInMemory_DisplayedAndArchived()
    {
        var documentId = await CreateDocumentAsync(isPublic: true);

        var response = await Http.GetAsync($"/post/{documentId}");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(html, "Views");
        StringAssert.Contains(html, "Views 1");

        var viewCounts = GetService<ViewCountService>();
        Assert.AreEqual(1L, viewCounts.GetCount(documentId));

        var indexHtml = await Http.GetStringAsync("/");
        StringAssert.Contains(indexHtml, "Views");

        await viewCounts.ArchiveAsync();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.AreEqual(1L, await db.MarkdownDocuments
            .Where(document => document.Id == documentId)
            .Select(document => document.ViewCount)
            .SingleAsync());
    }

    [TestMethod]
    public async Task ParallelIncrements_AreExactAcrossConsecutiveArchives()
    {
        var documentId = await CreateDocumentAsync(isPublic: true);
        var viewCounts = GetService<ViewCountService>();

        Parallel.For(0, 20_000, _ => viewCounts.Increment(documentId));
        await viewCounts.ArchiveAsync();
        Parallel.For(0, 10_000, _ => viewCounts.Increment(documentId));
        await viewCounts.ArchiveAsync();

        Assert.AreEqual(30_000L, viewCounts.GetCount(documentId));
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.AreEqual(30_000L, await db.MarkdownDocuments
            .Where(document => document.Id == documentId)
            .Select(document => document.ViewCount)
            .SingleAsync());
    }

    [TestMethod]
    public async Task DraftMissingAndRedirectRequests_AreNotCounted()
    {
        var draftId = await CreateDocumentAsync(isPublic: false);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/post/{draftId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/post/{Guid.NewGuid()}")).StatusCode);

        var sluggedId = await CreateDocumentAsync(isPublic: true, slug: "redirect-only");
        Assert.AreEqual(HttpStatusCode.MovedPermanently, (await Http.GetAsync($"/post/{sluggedId}")).StatusCode);

        var viewCounts = GetService<ViewCountService>();
        Assert.AreEqual(0L, viewCounts.GetCount(draftId));
        Assert.AreEqual(0L, viewCounts.GetCount(sluggedId));
    }

    [TestMethod]
    public async Task DeletedDocument_IsSafelyDiscardedDuringArchive()
    {
        var documentId = await CreateDocumentAsync(isPublic: true);
        var viewCounts = GetService<ViewCountService>();
        viewCounts.Increment(documentId);

        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.MarkdownDocuments.Remove((await db.MarkdownDocuments.FindAsync(documentId))!);
            await db.SaveChangesAsync();
        }

        await viewCounts.ArchiveAsync();
        Assert.AreEqual(0L, viewCounts.GetCount(documentId));
    }

    private async Task<Guid> CreateDocumentAsync(bool isPublic, string? slug = null)
    {
        var id = Guid.NewGuid();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await db.Users.FirstAsync();
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = id,
            Title = "View count test post",
            Content = "Test content",
            UserId = user.Id,
            IsPublic = isPublic,
            Slug = slug,
            SlugDate = slug == null ? null : DateTime.UtcNow.Date,
            CreationTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }
}
