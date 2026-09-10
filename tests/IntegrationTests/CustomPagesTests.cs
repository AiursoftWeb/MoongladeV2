using System.Net;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Tests.IntegrationTests;

[TestClass]
public class CustomPagesTests : TestBase
{
    [TestMethod]
    public async Task PublishedPage_IsPublic_AndSanitizesExecutableHtml()
    {
        await AddPageAsync(new CustomPage
        {
            Title = "About",
            Slug = "about",
            MetaDescription = "About this site",
            HtmlContent = "<h1>About us</h1><script>alert(1)</script><p onclick=\"alert(2)\">Safe text</p>",
            CssContent = ".custom-page { color: green; }</style><script>alert(3)</script>",
            IsPublished = true
        });

        var response = await Http.GetAsync("/page/about");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(html, "About us");
        StringAssert.Contains(html, "Safe text");
        StringAssert.Contains(html, "About this site");
        StringAssert.Contains(html, ".custom-page { color: green; }");
        Assert.IsFalse(html.Contains("alert(1)", StringComparison.Ordinal));
        Assert.IsFalse(html.Contains("onclick", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(html.Contains("<script>alert(3)</script>", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task DraftPage_IsNotPublic_ButAdministratorCanPreview()
    {
        var id = Guid.NewGuid();
        await AddPageAsync(new CustomPage
        {
            Id = id,
            Title = "Draft",
            Slug = "draft",
            MetaDescription = "Draft page",
            HtmlContent = "<p>Draft content</p>",
            CssContent = string.Empty,
            IsPublished = false
        });

        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync("/page/draft")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Redirect, (await Http.GetAsync($"/page/preview/{id}")).StatusCode);

        await LoginAsAdmin();
        var preview = await Http.GetAsync($"/page/preview/{id}");
        preview.EnsureSuccessStatusCode();
        StringAssert.Contains(await preview.Content.ReadAsStringAsync(), "Draft content");
    }

    [TestMethod]
    public async Task Administrator_CanCreateEditAndDeletePage()
    {
        await LoginAsAdmin();
        var createResponse = await PostForm("/Pages/Save", new Dictionary<string, string>
        {
            ["Title"] = "Migration Guide",
            ["Slug"] = "migration-guide",
            ["MetaDescription"] = "How to migrate",
            ["HtmlContent"] = "<h1>Move</h1>",
            ["CssContent"] = "h1 { color: blue; }",
            ["HideSidebar"] = "true",
            ["IsPublished"] = "true"
        }, tokenUrl: "/Pages/Create");
        Assert.AreEqual(HttpStatusCode.Redirect, createResponse.StatusCode);

        Guid id;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var page = await db.CustomPages.SingleAsync(p => p.Slug == "migration-guide");
            id = page.Id;
            Assert.IsTrue(page.IsPublished);
            Assert.IsTrue(page.HideSidebar);
        }

        var editResponse = await PostForm("/Pages/Save", new Dictionary<string, string>
        {
            ["Id"] = id.ToString(),
            ["Title"] = "Updated Guide",
            ["Slug"] = "updated-guide",
            ["MetaDescription"] = "Updated",
            ["HtmlContent"] = "<h1>Updated</h1>",
            ["CssContent"] = string.Empty
        }, tokenUrl: $"/Pages/Edit/{id}");
        Assert.AreEqual(HttpStatusCode.Redirect, editResponse.StatusCode,
            await editResponse.Content.ReadAsStringAsync());

        var deleteResponse = await PostForm("/Pages/Delete", new Dictionary<string, string>
        {
            ["id"] = id.ToString()
        }, tokenUrl: "/Pages");
        Assert.AreEqual(HttpStatusCode.Redirect, deleteResponse.StatusCode);

        using var verifyScope = Server!.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.IsFalse(await verifyDb.CustomPages.AnyAsync(p => p.Id == id));
    }

    private async Task AddPageAsync(CustomPage page)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        db.CustomPages.Add(page);
        await db.SaveChangesAsync();
    }
}
