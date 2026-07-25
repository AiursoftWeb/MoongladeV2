using System.Globalization;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Tests;

[TestClass]
public class DocumentLocalizationServiceTests
{
    /// <summary>
    /// Concrete context for SQLite in-memory tests.
    /// </summary>
    private sealed class SqliteTestContext(DbContextOptions<SqliteTestContext> options)
        : TemplateDbContext(options)
    {
    }

    private SqliteConnection _connection = null!;
    private DbContextOptions<SqliteTestContext> _dbOptions = null!;

    [TestInitialize]
    public void Initialize()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var pragmaCmd = _connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA foreign_keys = OFF;";
        pragmaCmd.ExecuteNonQuery();

        _dbOptions = new DbContextOptionsBuilder<SqliteTestContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new SqliteTestContext(_dbOptions);
        db.Database.EnsureCreated();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _connection.Close();
        _connection.Dispose();
    }

    /// <summary>
    /// Fake <see cref="IHttpContextAccessor"/> that returns a configured culture.
    /// </summary>
    private sealed class FakeHttpContextAccessor(string culture) : IHttpContextAccessor
    {
        public HttpContext? HttpContext
        {
            get
            {
                var context = new DefaultHttpContext();
                var cultureInfo = new CultureInfo(culture);
                var requestCulture = new RequestCulture(cultureInfo);
                var feature = new RequestCultureFeature(requestCulture, provider: null!);
                context.Features.Set<IRequestCultureFeature>(feature);
                return context;
            }
            set => throw new NotSupportedException();
        }
    }

    private SqliteTestContext NewDb() =>
        new(_dbOptions);

    private static async Task SeedAsync(TemplateDbContext db, params object[] entities)
    {
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private static MarkdownDocument CreateDoc(Guid id, string title, string content,
        string? sourceCulture, bool isPublic = true)
    {
        return new MarkdownDocument
        {
            Id = id,
            Title = title,
            Content = content,
            UserId = "test-user",
            IsPublic = isPublic,
            SourceCulture = sourceCulture,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static LocalizedDocument CreateLocalized(Guid docId, string culture,
        string localizedTitle, string localizedContent)
    {
        return new LocalizedDocument
        {
            DocumentId = docId,
            Culture = culture,
            LocalizedTitle = localizedTitle,
            LocalizedContent = localizedContent,
            LastLocalizedAt = DateTime.UtcNow
        };
    }

    // ═════════════════════════════════════════════════════════════════════
    // LoadLocalizedStringsAsync
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When the request culture matches the document's SourceCulture,
    /// the document should be excluded from the localized query.
    /// The caller will fall back to the original title and content.
    /// </summary>
    [TestMethod]
    public async Task LoadLocalizedStrings_SameCulture_SkipsQueryAndReturnsEmpty()
    {
        // Arrange: doc in zh-CN, a ja-JP translation exists, request culture is zh-CN
        var docId = Guid.NewGuid();
        await using (var seedDb = NewDb())
        {
            var doc = CreateDoc(docId, "你好世界", "这是中文内容", sourceCulture: "zh-CN");
            var localization = CreateLocalized(docId, "ja-JP",
                "こんにちは世界", "これは日本語の内容です");
            await SeedAsync(seedDb, doc, localization);
        }

        var accessor = new FakeHttpContextAccessor("zh-CN");
        var db = NewDb();
        var service = new DocumentLocalizationService(db, accessor);

        var docInMemory = await db.MarkdownDocuments.FirstAsync(d => d.Id == docId);

        // Act
        var (titles, contents) = await service.LoadLocalizedStringsAsync([docInMemory]);

        // Assert: dictionaries should be empty — caller uses original content directly
        Assert.AreEqual(0, titles.Count,
            "Document with SourceCulture == current culture should be excluded from title lookup.");
        Assert.AreEqual(0, contents.Count,
            "Document with SourceCulture == current culture should be excluded from content lookup.");
    }

    /// <summary>
    /// When the request culture differs from SourceCulture, the localized
    /// content should be returned from the database.
    /// </summary>
    [TestMethod]
    public async Task LoadLocalizedStrings_DifferentCulture_ReturnsLocalized()
    {
        // Arrange: doc in zh-CN, ja-JP translation exists, request culture is ja-JP
        var docId = Guid.NewGuid();
        await using (var seedDb = NewDb())
        {
            var doc = CreateDoc(docId, "你好世界", "这是中文内容", sourceCulture: "zh-CN");
            var localization = CreateLocalized(docId, "ja-JP",
                "こんにちは世界", "これは日本語の内容です");
            await SeedAsync(seedDb, doc, localization);
        }

        var accessor = new FakeHttpContextAccessor("ja-JP");
        var db = NewDb();
        var service = new DocumentLocalizationService(db, accessor);

        var docInMemory = await db.MarkdownDocuments.FirstAsync(d => d.Id == docId);

        // Act
        var (titles, contents) = await service.LoadLocalizedStringsAsync([docInMemory]);

        // Assert
        Assert.AreEqual(1, titles.Count,
            "Document with different SourceCulture should have a localized title.");
        Assert.AreEqual("こんにちは世界", titles[docId]);
        Assert.AreEqual(1, contents.Count);
        Assert.AreEqual("これは日本語の内容です", contents[docId]);
    }

    /// <summary>
    /// Mixed batch: some docs match the request culture (skipped), others don't (translated).
    /// </summary>
    [TestMethod]
    public async Task LoadLocalizedStrings_MixedBatch_SkipsOnlyMatching()
    {
        // Arrange: request culture = zh-CN
        // zhDoc (sourceCulture=zh-CN) → skip, same culture
        // enDoc (sourceCulture=en-US) → needs zh-CN translation
        var zhDocId = Guid.NewGuid();
        var enDocId = Guid.NewGuid();
        await using (var seedDb = NewDb())
        {
            var zhDoc = CreateDoc(zhDocId, "你好", "中文内容", sourceCulture: "zh-CN");
            var enDoc = CreateDoc(enDocId, "Hello", "English content", sourceCulture: "en-US");
            // Seed zh-CN translation for enDoc (en-US → zh-CN)
            var enLocalization = CreateLocalized(enDocId, "zh-CN",
                "你好（翻译）", "中文翻译内容");
            await SeedAsync(seedDb, zhDoc, enDoc, enLocalization);
        }

        var accessor = new FakeHttpContextAccessor("zh-CN");
        var db = NewDb();
        var service = new DocumentLocalizationService(db, accessor);

        var docs = await db.MarkdownDocuments
            .Where(d => d.Id == zhDocId || d.Id == enDocId)
            .ToListAsync();

        // Act
        var (titles, contents) = await service.LoadLocalizedStringsAsync(docs);

        // Assert: zhDoc excluded (same culture), enDoc included
        Assert.IsFalse(titles.ContainsKey(zhDocId),
            "zh-CN doc should be skipped (same culture → fallback to original).");
        Assert.IsFalse(contents.ContainsKey(zhDocId),
            "zh-CN doc content should be skipped (same culture → fallback to original).");
        Assert.IsTrue(titles.ContainsKey(enDocId),
            "en-US doc reading zh-CN should get localized title.");
        Assert.AreEqual("你好（翻译）", titles[enDocId]);
        Assert.IsTrue(contents.ContainsKey(enDocId),
            "en-US doc reading zh-CN should get localized content.");
    }

    /// <summary>
    /// When SourceCulture is null, the document is NOT skipped
    /// (null != any culture via OrdinalIgnoreCase comparison).
    /// </summary>
    [TestMethod]
    public async Task LoadLocalizedStrings_NullSourceCulture_NotSkipped()
    {
        // Arrange: doc has null SourceCulture, a ja-JP translation exists
        var docId = Guid.NewGuid();
        await using (var seedDb = NewDb())
        {
            var doc = CreateDoc(docId, "Unknown Lang", "Some content", sourceCulture: null);
            var localization = CreateLocalized(docId, "ja-JP",
                "不明な言語", "何らかのコンテンツ");
            await SeedAsync(seedDb, doc, localization);
        }

        var accessor = new FakeHttpContextAccessor("ja-JP");
        var db = NewDb();
        var service = new DocumentLocalizationService(db, accessor);

        var docInMemory = await db.MarkdownDocuments.FirstAsync(d => d.Id == docId);

        // Act
        var (titles, _) = await service.LoadLocalizedStringsAsync([docInMemory]);

        // Assert: even with null SourceCulture, the translation should be returned
        Assert.AreEqual(1, titles.Count,
            "Document with null SourceCulture should still be queried for localization.");
        Assert.AreEqual("不明な言語", titles[docId]);
    }

    /// <summary>
    /// Empty document list returns empty dictionaries without hitting the database.
    /// </summary>
    [TestMethod]
    public async Task LoadLocalizedStrings_EmptyList_ReturnsEmpty()
    {
        var accessor = new FakeHttpContextAccessor("zh-CN");
        var db = NewDb();
        var service = new DocumentLocalizationService(db, accessor);

        var (titles, contents) = await service.LoadLocalizedStringsAsync([]);

        Assert.AreEqual(0, titles.Count);
        Assert.AreEqual(0, contents.Count);
    }

    /// <summary>
    /// When no current culture can be determined, skip all documents.
    /// </summary>
    [TestMethod]
    public async Task LoadLocalizedStrings_NoCurrentCulture_ReturnsEmpty()
    {
        var docId = Guid.NewGuid();
        await using (var seedDb = NewDb())
        {
            var doc = CreateDoc(docId, "Test", "Content", sourceCulture: "en-US");
            await SeedAsync(seedDb, doc);
        }

        // Accessor that returns null HttpContext (no culture feature)
        var accessor = new NullHttpContextAccessor();
        var db = NewDb();
        var service = new DocumentLocalizationService(db, accessor);

        var docInMemory = await db.MarkdownDocuments.FirstAsync(d => d.Id == docId);

        var (titles, contents) = await service.LoadLocalizedStringsAsync([docInMemory]);

        Assert.AreEqual(0, titles.Count);
        Assert.AreEqual(0, contents.Count);
    }

    // ═════════════════════════════════════════════════════════════════════
    // LoadLocalizedAbstractsAsync
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When request culture matches SourceCulture, the document is
    /// excluded — caller falls back to BuildExcerpt from original content.
    /// </summary>
    [TestMethod]
    public async Task LoadLocalizedAbstracts_SameCulture_SkipsQueryAndReturnsEmpty()
    {
        var docId = Guid.NewGuid();
        await using (var seedDb = NewDb())
        {
            var doc = CreateDoc(docId, "中文标题", "中文正文内容", sourceCulture: "zh-CN");
            var abstract_ = new LocalizedAbstract
            {
                DocumentId = docId,
                Culture = "zh-CN",
                Abstract = "这是一段中文摘要。",
                LastGeneratedAt = DateTime.UtcNow
            };
            await SeedAsync(seedDb, doc, abstract_);
        }

        var accessor = new FakeHttpContextAccessor("zh-CN");
        var db = NewDb();
        var service = new DocumentLocalizationService(db, accessor);

        var docInMemory = await db.MarkdownDocuments.FirstAsync(d => d.Id == docId);

        // Act
        var abstracts = await service.LoadLocalizedAbstractsAsync([docInMemory]);

        // Assert: excluded → also excluded from dictionary keys entirely
        Assert.IsFalse(abstracts.ContainsKey(docId),
            "Abstract for same-culture document should be excluded; caller builds excerpt from original.");
    }

    /// <summary>
    /// When request culture differs from SourceCulture, the localized
    /// abstract is returned.
    /// </summary>
    [TestMethod]
    public async Task LoadLocalizedAbstracts_DifferentCulture_ReturnsLocalized()
    {
        var docId = Guid.NewGuid();
        await using (var seedDb = NewDb())
        {
            var doc = CreateDoc(docId, "中文标题", "中文正文内容", sourceCulture: "zh-CN");
            var abstract_ = new LocalizedAbstract
            {
                DocumentId = docId,
                Culture = "ja-JP",
                Abstract = "これは日本語の要約です。",
                LastGeneratedAt = DateTime.UtcNow
            };
            await SeedAsync(seedDb, doc, abstract_);
        }

        var accessor = new FakeHttpContextAccessor("ja-JP");
        var db = NewDb();
        var service = new DocumentLocalizationService(db, accessor);

        var docInMemory = await db.MarkdownDocuments.FirstAsync(d => d.Id == docId);

        // Act
        var abstracts = await service.LoadLocalizedAbstractsAsync([docInMemory]);

        // Assert
        Assert.IsTrue(abstracts.ContainsKey(docId));
        Assert.AreEqual("これは日本語の要約です。", abstracts[docId]);
    }

    /// <summary>
    /// Mixed batch of abstracts: same-culture docs excluded, different-culture docs included.
    /// </summary>
    [TestMethod]
    public async Task LoadLocalizedAbstracts_MixedBatch_SkipsOnlyMatching()
    {
        var zhDocId = Guid.NewGuid();
        var enDocId = Guid.NewGuid();
        await using (var seedDb = NewDb())
        {
            var zhDoc = CreateDoc(zhDocId, "你好", "中文", sourceCulture: "zh-CN");
            var enDoc = CreateDoc(enDocId, "Hello", "English", sourceCulture: "en-US");
            // Seed zh-CN abstract for enDoc (en-US document needs zh-CN abstract)
            var enAbstract = new LocalizedAbstract
            {
                DocumentId = enDocId, Culture = "zh-CN",
                Abstract = "英文文章的摘要", LastGeneratedAt = DateTime.UtcNow
            };
            await SeedAsync(seedDb, zhDoc, enDoc, enAbstract);
        }

        // Request culture = zh-CN
        // → zhDoc excluded (SourceCulture matches), enDoc gets zh-CN abstract
        var accessor = new FakeHttpContextAccessor("zh-CN");
        var db = NewDb();
        var service = new DocumentLocalizationService(db, accessor);

        var docs = await db.MarkdownDocuments
            .Where(d => d.Id == zhDocId || d.Id == enDocId)
            .ToListAsync();

        var abstracts = await service.LoadLocalizedAbstractsAsync(docs);

        Assert.IsFalse(abstracts.ContainsKey(zhDocId),
            "zh-CN doc abstract should be excluded (same culture).");
        Assert.IsTrue(abstracts.ContainsKey(enDocId),
            "en-US doc should get localized abstract (zh-CN).");
    }

    // ═════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════

    private sealed class NullHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext
        {
            get => null;
            set => throw new NotSupportedException();
        }
    }
}
