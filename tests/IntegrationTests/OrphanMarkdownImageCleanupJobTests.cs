using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Services.BackgroundJobs;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Tests.IntegrationTests;

/// <summary>
/// Integration tests for OrphanMarkdownImageCleanupJob.
/// Each test writes real files into a temporary storage directory,
/// optionally seeds the DB with documents, runs the job, then asserts
/// which files survived and which were deleted.
/// </summary>
[TestClass]
public class OrphanMarkdownImageCleanupJobTests : TestBase
{
    private string ImagesDir => Path.Combine(StoragePath, "Workspace", "markdown-images");

    /// <summary>
    /// Creates a file in the markdown-images directory and optionally
    /// back-dates its LastWriteTime so it falls outside the grace period.
    /// </summary>
    private string CreateImageFile(string filename, bool isOld)
    {
        Directory.CreateDirectory(ImagesDir);
        var path = Path.Combine(ImagesDir, filename);
        File.WriteAllText(path, "fake-image-data");
        if (isOld)
        {
            // Set write time to 8 hours ago — safely past the 7-hour grace period.
            var oldTime = DateTime.UtcNow.AddHours(-8);
            File.SetLastWriteTimeUtc(path, oldTime);
        }
        return path;
    }

    private async Task RunJob()
    {
        var job = Server!.Services.GetRequiredService<OrphanMarkdownImageCleanupJob>();
        await job.ExecuteAsync();
    }

    // -----------------------------------------------------------------------
    // Test 1: orphan file older than grace period → deleted
    // -----------------------------------------------------------------------
    [TestMethod]
    public async Task OldOrphanImageIsDeleted()
    {
        var orphanPath = CreateImageFile("orphan-old.png", isOld: true);

        await RunJob();

        Assert.IsFalse(File.Exists(orphanPath),
            "An old orphan image should have been deleted by the cleanup job.");
    }

    // -----------------------------------------------------------------------
    // Test 2: orphan file within grace period → kept
    // -----------------------------------------------------------------------
    [TestMethod]
    public async Task NewOrphanImageIsKeptWithinGracePeriod()
    {
        var freshPath = CreateImageFile("orphan-fresh.png", isOld: false);

        await RunJob();

        Assert.IsTrue(File.Exists(freshPath),
            "A freshly uploaded orphan image must be kept within the grace period.");
    }

    // -----------------------------------------------------------------------
    // Test 3: referenced image (old) → kept because it is in the DB
    // -----------------------------------------------------------------------
    [TestMethod]
    public async Task ReferencedImageIsNeverDeleted()
    {
        var filename = "referenced-old.png";
        var referencedPath = CreateImageFile(filename, isOld: true);

        // Seed a document whose content references this image.
        var db = Server!.Services.GetRequiredService<TemplateDbContext>();
        var admin = await db.Users.FirstAsync();
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "Test doc",
            Content = $"![screenshot](/download/markdown-images/{filename})",
            UserId = admin.Id
        });
        await db.SaveChangesAsync();

        await RunJob();

        Assert.IsTrue(File.Exists(referencedPath),
            "An image referenced in a document must never be deleted, even if it is old.");
    }

    // -----------------------------------------------------------------------
    // Test 4: mix — one referenced, one old orphan, one fresh orphan
    // -----------------------------------------------------------------------
    [TestMethod]
    public async Task MixedScenario_OnlyOldOrphansAreDeleted()
    {
        var referencedFile = CreateImageFile("keep-referenced.png", isOld: true);
        var oldOrphanFile = CreateImageFile("delete-old-orphan.png", isOld: true);
        var freshOrphanFile = CreateImageFile("keep-fresh-orphan.png", isOld: false);

        var db = Server!.Services.GetRequiredService<TemplateDbContext>();
        var admin = await db.Users.FirstAsync();
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "Mixed test doc",
            Content = "![img](/download/markdown-images/keep-referenced.png)",
            UserId = admin.Id
        });
        await db.SaveChangesAsync();

        await RunJob();

        Assert.IsTrue(File.Exists(referencedFile), "Referenced image must survive.");
        Assert.IsFalse(File.Exists(oldOrphanFile), "Old orphan must be deleted.");
        Assert.IsTrue(File.Exists(freshOrphanFile), "Fresh orphan must survive grace period.");
    }

    // -----------------------------------------------------------------------
    // Test 5: no markdown-images directory → job completes without throwing
    // -----------------------------------------------------------------------
    [TestMethod]
    public async Task MissingDirectoryDoesNotThrow()
    {
        // Do NOT create the directory — job should handle missing dir gracefully.
        await RunJob();
        // Reaching here without exception is the pass condition.
    }

    // -----------------------------------------------------------------------
    // Test 6: referenced image with absolute URL → kept (fix for #502)
    // -----------------------------------------------------------------------
    [TestMethod]
    public async Task ReferencedImageWithAbsoluteUrlIsKept()
    {
        var filename = "absolute-url-ref.png";
        var referencedPath = CreateImageFile(filename, isOld: true);

        var db = Server!.Services.GetRequiredService<TemplateDbContext>();
        var admin = await db.Users.FirstAsync();
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "Absolute URL test",
            Content = $"![screenshot](https://news.aiursoft.com/download/markdown-images/{filename})",
            UserId = admin.Id
        });
        await db.SaveChangesAsync();

        await RunJob();

        Assert.IsTrue(File.Exists(referencedPath),
            "An image referenced via absolute URL must never be deleted.");
    }

    // -----------------------------------------------------------------------
    // Test 7: references do not have to use inline Markdown image syntax
    // -----------------------------------------------------------------------
    [TestMethod]
    public async Task HtmlAndReferenceStyleImagesAreKept()
    {
        var htmlFilename = "html-reference.png";
        var markdownFilename = "reference-style.png";
        var htmlPath = CreateImageFile(htmlFilename, isOld: true);
        var markdownPath = CreateImageFile(markdownFilename, isOld: true);

        var db = Server!.Services.GetRequiredService<TemplateDbContext>();
        var admin = await db.Users.FirstAsync();
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "Non-inline references",
            Content = $"""
                <img src="/download/markdown-images/{htmlFilename}" alt="HTML image">
                ![reference-style image][diagram]
                [diagram]: <https://news.aiursoft.com/download/markdown-images/{markdownFilename}> "Diagram"
                """,
            UserId = admin.Id
        });
        await db.SaveChangesAsync();

        await RunJob();

        Assert.IsTrue(File.Exists(htmlPath), "An image referenced by HTML must be kept.");
        Assert.IsTrue(File.Exists(markdownPath), "A reference-style Markdown image must be kept.");
    }

    // -----------------------------------------------------------------------
    // Test 8: URL-encoded paths are matched conservatively
    // -----------------------------------------------------------------------
    [TestMethod]
    public async Task UrlEncodedImagePathIsKept()
    {
        var filename = "encoded image (final).png";
        var referencedPath = CreateImageFile(filename, isOld: true);
        var encodedFilename = Uri.EscapeDataString(filename);

        var db = Server!.Services.GetRequiredService<TemplateDbContext>();
        var admin = await db.Users.FirstAsync();
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "Encoded URL",
            Content = $"![image](https://news.aiursoft.com/download/markdown-images/{encodedFilename}?v=1)",
            UserId = admin.Id
        });
        await db.SaveChangesAsync();

        await RunJob();

        Assert.IsTrue(File.Exists(referencedPath), "An image referenced by an encoded URL must be kept.");
    }

    // -----------------------------------------------------------------------
    // Test 9: localized content is also authoritative
    // -----------------------------------------------------------------------
    [TestMethod]
    public async Task ImageReferencedOnlyByLocalizedDocumentIsKept()
    {
        var filename = "localized-only.png";
        var referencedPath = CreateImageFile(filename, isOld: true);

        var db = Server!.Services.GetRequiredService<TemplateDbContext>();
        var admin = await db.Users.FirstAsync();
        var document = new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "Localized reference",
            Content = "The source no longer contains the localized illustration.",
            UserId = admin.Id
        };
        db.MarkdownDocuments.Add(document);
        db.LocalizedDocuments.Add(new LocalizedDocument
        {
            DocumentId = document.Id,
            Culture = "zh-CN",
            LocalizedTitle = "本地化引用",
            LocalizedContent = $"![](https://news.aiursoft.com/download/markdown-images/{filename})"
        });
        await db.SaveChangesAsync();

        await RunJob();

        Assert.IsTrue(File.Exists(referencedPath),
            "An image referenced only by localized content must be kept.");
    }

    // -----------------------------------------------------------------------
    // Test 10: a longer, different filename is not an exact path reference
    // -----------------------------------------------------------------------
    [TestMethod]
    public async Task FilenamePrefixDoesNotKeepAnOrphan()
    {
        var orphanPath = CreateImageFile("prefix.png", isOld: true);

        var db = Server!.Services.GetRequiredService<TemplateDbContext>();
        var admin = await db.Users.FirstAsync();
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "Different filename",
            Content = "![](/download/markdown-images/prefix.png.backup)",
            UserId = admin.Id
        });
        await db.SaveChangesAsync();

        await RunJob();

        Assert.IsFalse(File.Exists(orphanPath),
            "A different filename that merely has the orphan path as a prefix must not keep it.");
    }
}
