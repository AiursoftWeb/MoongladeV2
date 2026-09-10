using System.Net;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Tests.IntegrationTests;

[TestClass]
public class CustomPagesTests : TestBase
{
    private readonly List<Guid> _createdPageIds = [];

    [TestCleanup]
    public override async Task CleanServer()
    {
        if (Server != null)
        {
            using var scope = Server.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.CustomPages.RemoveRange(await db.CustomPages.Where(p => _createdPageIds.Contains(p.Id)).ToListAsync());
            await db.SaveChangesAsync();
        }
        await base.CleanServer();
    }

    [TestMethod]
    [DataRow("/")]
    [DataRow("/tags")]
    [DataRow("/archive")]
    [DataRow("/page/about")]
    public async Task PublicNavbar_ShowsPublishedPagesOnly_InTitleOrder(string path)
    {
        foreach (var (title, slug, published) in new[]
                 {
                     ("Z Contacts", "contacts", true),
                     ("About", "about", true),
                     ("Secret draft", "secret-draft", false),
                     ("Safe <b>title</b>", "safe-title", true)
                 })
        {
            await AddPageAsync(new CustomPage
            {
                Title = title, Slug = slug, IsPublished = published,
                HtmlContent = "<p>Page body</p>", CssContent = string.Empty, MetaDescription = string.Empty
            });
        }

        var response = await Http.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var navStart = html.IndexOf("<nav ", StringComparison.Ordinal);
        Assert.IsTrue(navStart >= 0);
        var nav = html[navStart..html.IndexOf("</nav>", navStart, StringComparison.Ordinal)];
        StringAssert.Contains(nav, "href=\"/page/about\">About</a>");
        StringAssert.Contains(nav, "href=\"/page/contacts\">Z Contacts</a>");
        StringAssert.Contains(nav, "Safe &lt;b&gt;title&lt;/b&gt;");
        Assert.IsFalse(nav.Contains("secret-draft", StringComparison.Ordinal));
        Assert.IsFalse(nav.Contains("Secret draft", StringComparison.Ordinal));
        Assert.IsTrue(nav.IndexOf("/page/about", StringComparison.Ordinal) < nav.IndexOf("/page/contacts", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Navbar_RemovesUnpublishedPageImmediately()
    {
        var page = new CustomPage
        {
            Title = "About", Slug = "about", IsPublished = true,
            HtmlContent = string.Empty, CssContent = string.Empty, MetaDescription = string.Empty
        };
        await AddPageAsync(page);
        StringAssert.Contains(await Http.GetStringAsync("/"), "href=\"/page/about\"");
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var stored = await db.CustomPages.SingleAsync(p => p.Id == page.Id);
            stored.IsPublished = false;
            await db.SaveChangesAsync();
        }
        Assert.IsFalse((await Http.GetStringAsync("/")).Contains("href=\"/page/about\"", StringComparison.Ordinal));
    }

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
        _createdPageIds.Add(page.Id);
    }
}
