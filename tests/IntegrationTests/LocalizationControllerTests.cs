using System.Net;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Tests.IntegrationTests;

[TestClass]
public class LocalizationControllerTests : TestBase
{
    private async Task<Guid> EnsureDocumentAsync()
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var doc = await db.MarkdownDocuments.FirstOrDefaultAsync();
        if (doc == null)
        {
            var user = await db.Users.FirstAsync();
            doc = new MarkdownDocument
            {
                UserId = user.Id,
                Title = "Test Blog Post",
                Content = "# Hello World\nThis is a test post.",
                IsPublic = true,
                Slug = "test-blog-post-" + Guid.NewGuid(),
                UpdatedAt = DateTime.UtcNow
            };
            db.MarkdownDocuments.Add(doc);
            await db.SaveChangesAsync();
        }
        return doc.Id;
    }

    [TestMethod]
    public async Task Localize_GetAndSave_WorksSuccessfully()
    {
        var docId = await EnsureDocumentAsync();
        await LoginAsAdmin();

        var getResp = await Http.GetAsync($"/Home/Localize/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, getResp.StatusCode);

        var getDataResp = await Http.GetAsync($"/Home/LocalizeData?id={docId}&culture=zh-CN");
        Assert.AreEqual(HttpStatusCode.OK, getDataResp.StatusCode);

        var saveResp = await PostForm("/Home/SaveLocalization", new Dictionary<string, string>
        {
            { "documentId", docId.ToString() },
            { "culture", "zh-CN" },
            { "localizedTitle", "中文标题" },
            { "localizedContent", "中文内容" }
        }, tokenUrl: $"/Home/Localize/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, saveResp.StatusCode);

        var notFoundResp = await Http.GetAsync($"/Home/Localize/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, notFoundResp.StatusCode);
    }

    [TestMethod]
    public async Task AbstractLocalize_GetAndSave_WorksSuccessfully()
    {
        var docId = await EnsureDocumentAsync();
        await LoginAsAdmin();

        var getResp = await Http.GetAsync($"/Home/AbstractLocalize/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, getResp.StatusCode);

        var getDataResp = await Http.GetAsync($"/Home/AbstractLocalizeData?id={docId}&culture=zh-CN");
        Assert.AreEqual(HttpStatusCode.OK, getDataResp.StatusCode);

        var saveResp = await PostForm("/Home/SaveAbstractLocalization", new Dictionary<string, string>
        {
            { "documentId", docId.ToString() },
            { "culture", "zh-CN" },
            { "abstractText", "这是中文摘要" }
        }, tokenUrl: $"/Home/AbstractLocalize/{docId}");
        Assert.AreEqual(HttpStatusCode.OK, saveResp.StatusCode);

        var notFoundResp = await Http.GetAsync($"/Home/AbstractLocalize/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, notFoundResp.StatusCode);
    }
}
