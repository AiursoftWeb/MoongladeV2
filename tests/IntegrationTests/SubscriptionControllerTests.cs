using System.Net;
using System.Xml.Linq;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Tests.IntegrationTests;

[TestClass]
public class SubscriptionControllerTests : TestBase
{
    [TestMethod]
    public async Task EmptyFeed_ReturnsValidRssAndAtom()
    {
        // 1. RSS
        var rssResponse = await Http.GetAsync("/rss");
        Assert.AreEqual(HttpStatusCode.OK, rssResponse.StatusCode);
        Assert.IsTrue(rssResponse.Content.Headers.ContentType?.MediaType?.Contains("xml"));

        var rssXml = await rssResponse.Content.ReadAsStringAsync();
        var rssDoc = XDocument.Parse(rssXml);
        var channel = rssDoc.Root?.Element("channel");
        Assert.IsNotNull(channel);
        Assert.AreEqual(0, channel.Elements("item").Count());

        // 2. Atom
        var atomResponse = await Http.GetAsync("/atom");
        Assert.AreEqual(HttpStatusCode.OK, atomResponse.StatusCode);
        Assert.IsTrue(atomResponse.Content.Headers.ContentType?.MediaType?.Contains("xml"));

        var atomXml = await atomResponse.Content.ReadAsStringAsync();
        var atomDoc = XDocument.Parse(atomXml);
        XNamespace atomNs = "http://www.w3.org/2005/Atom";
        Assert.AreEqual(atomNs + "feed", atomDoc.Root?.Name);
        Assert.AreEqual(0, atomDoc.Root?.Elements(atomNs + "entry").Count());
    }

    [TestMethod]
    public async Task PublicAndPrivatePosts_OnlyPublicIncludedInFeed()
    {
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var adminUser = await db.Users.FirstAsync();

            db.MarkdownDocuments.AddRange(
                new MarkdownDocument
                {
                    Id = Guid.NewGuid(),
                    Title = "Public Announcement",
                    Content = "This is a **public** blog post about .NET.",
                    Slug = "public-announcement",
                    Tags = "dotnet,csharp",
                    IsPublic = true,
                    UserId = adminUser.Id,
                    CreationTime = DateTime.UtcNow.AddHours(-1),
                    UpdatedAt = DateTime.UtcNow.AddHours(-1)
                },
                new MarkdownDocument
                {
                    Id = Guid.NewGuid(),
                    Title = "Secret Internal Plan",
                    Content = "This should never appear in any public RSS feed.",
                    Slug = "secret-internal-plan",
                    Tags = "secret,internal",
                    IsPublic = false,
                    UserId = adminUser.Id,
                    CreationTime = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            );
            await db.SaveChangesAsync();
        }

        // Test RSS
        var rssResponse = await Http.GetAsync("/rss");
        Assert.AreEqual(HttpStatusCode.OK, rssResponse.StatusCode);
        var rssXml = await rssResponse.Content.ReadAsStringAsync();

        StringAssert.Contains(rssXml, "Public Announcement");
        Assert.IsFalse(rssXml.Contains("Secret Internal Plan"));

        var rssDoc = XDocument.Parse(rssXml);
        var items = rssDoc.Root?.Element("channel")?.Elements("item").ToList();
        Assert.IsNotNull(items);
        Assert.AreEqual(1, items.Count);

        var publicItem = items[0];
        Assert.AreEqual("Public Announcement", publicItem.Element("title")?.Value);
        StringAssert.Contains(publicItem.Element("link")?.Value, "/post/");
        Assert.IsNotNull(publicItem.Element("pubDate"));
        Assert.IsNotNull(publicItem.Element("guid"));

        XNamespace contentNs = "http://purl.org/rss/1.0/modules/content/";
        var encoded = publicItem.Element(contentNs + "encoded")?.Value;
        Assert.IsNotNull(encoded);
        StringAssert.Contains(encoded, "<strong>public</strong>");

        // Test Atom
        var atomResponse = await Http.GetAsync("/atom");
        Assert.AreEqual(HttpStatusCode.OK, atomResponse.StatusCode);
        var atomXml = await atomResponse.Content.ReadAsStringAsync();

        StringAssert.Contains(atomXml, "Public Announcement");
        Assert.IsFalse(atomXml.Contains("Secret Internal Plan"));

        var atomDoc = XDocument.Parse(atomXml);
        XNamespace atomNs = "http://www.w3.org/2005/Atom";
        var entries = atomDoc.Root?.Elements(atomNs + "entry").ToList();
        Assert.IsNotNull(entries);
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("Public Announcement", entries[0].Element(atomNs + "title")?.Value);
    }

    [TestMethod]
    public async Task TagFiltering_OnlyReturnsMatchingPosts()
    {
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var adminUser = await db.Users.FirstAsync();

            db.MarkdownDocuments.AddRange(
                new MarkdownDocument
                {
                    Id = Guid.NewGuid(),
                    Title = "DotNet Article",
                    Content = "All about .NET architecture.",
                    Slug = "dotnet-article",
                    Tags = "dotnet,backend",
                    IsPublic = true,
                    UserId = adminUser.Id,
                    CreationTime = DateTime.UtcNow.AddMinutes(-30)
                },
                new MarkdownDocument
                {
                    Id = Guid.NewGuid(),
                    Title = "Python Machine Learning",
                    Content = "All about Python and PyTorch.",
                    Slug = "python-article",
                    Tags = "python,ai",
                    IsPublic = true,
                    UserId = adminUser.Id,
                    CreationTime = DateTime.UtcNow.AddMinutes(-10)
                }
            );
            await db.SaveChangesAsync();
        }

        // 1. Tag dotnet RSS
        var dotnetRss = await Http.GetAsync("/rss/dotnet");
        Assert.AreEqual(HttpStatusCode.OK, dotnetRss.StatusCode);
        var dotnetRssXml = await dotnetRss.Content.ReadAsStringAsync();
        StringAssert.Contains(dotnetRssXml, "DotNet Article");
        Assert.IsFalse(dotnetRssXml.Contains("Python Machine Learning"));

        // 2. Tag python Atom
        var pythonAtom = await Http.GetAsync("/atom/python");
        Assert.AreEqual(HttpStatusCode.OK, pythonAtom.StatusCode);
        var pythonAtomXml = await pythonAtom.Content.ReadAsStringAsync();
        StringAssert.Contains(pythonAtomXml, "Python Machine Learning");
        Assert.IsFalse(pythonAtomXml.Contains("DotNet Article"));

        // 3. Nonexistent tag returns empty feed with 200 OK
        var emptyTagRss = await Http.GetAsync("/rss/nonexistent-tag");
        Assert.AreEqual(HttpStatusCode.OK, emptyTagRss.StatusCode);
        var emptyDoc = XDocument.Parse(await emptyTagRss.Content.ReadAsStringAsync());
        Assert.AreEqual(0, emptyDoc.Root?.Element("channel")?.Elements("item").Count());
    }

    [TestMethod]
    [DataRow("/rss")]
    [DataRow("/rss.xml")]
    [DataRow("/feed")]
    [DataRow("/atom")]
    [DataRow("/atom.xml")]
    public async Task FeedAliases_ReturnOk(string path)
    {
        var response = await Http.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Content.Headers.ContentType?.MediaType?.Contains("xml"));
    }
    [TestMethod]
    public async Task HomePage_ContainsRssAlternateLinkAndFooterLink()
    {
        var response = await Http.GetAsync("/");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        StringAssert.Contains(html, "type=\"application/rss+xml\"");
        StringAssert.Contains(html, "href=\"/rss\"");
        StringAssert.Contains(html, "type=\"application/atom+xml\"");
        StringAssert.Contains(html, "href=\"/atom\"");
        StringAssert.Contains(html, "btn-rss");
    }

    [TestMethod]
    public async Task TagPage_ContainsTagSpecificRssButton()
    {
        var response = await Http.GetAsync("/tags/dotnet");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        StringAssert.Contains(html, "href=\"/rss/dotnet\"");
        StringAssert.Contains(html, "btn-rss");
    }
}
