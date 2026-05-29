using Aiursoft.MoongladeV2.Configuration;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Services;
using Aiursoft.MoongladeV2.Services.BackgroundJobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiursoft.MoongladeV2.Tests;

[TestClass]
public class GenerateAbstractDocumentsJobTests
{
    private sealed class SqliteTestContext(DbContextOptions<SqliteTestContext> options)
        : TemplateDbContext(options)
    {
    }

    private SqliteConnection _connection = null!;
    private DbContextOptions<SqliteTestContext> _dbOptions = null!;
    private IMemoryCache _cache = null!;

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

        _cache = new MemoryCache(new MemoryCacheOptions());

        using var db = new SqliteTestContext(_dbOptions);
        db.Database.EnsureCreated();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _connection.Close();
        _connection.Dispose();
    }

    private sealed class FakeAbstractGenerationService(
        GlobalSettingsService settingsService,
        ILogger<AbstractGenerationService> logger)
        : AbstractGenerationService(settingsService, null!, null!, logger)
    {
        public override Task<string> GenerateAbstractAsync(string content, string language)
            => Task.FromResult($"[{language}] Summary of: {content[..Math.Min(content.Length, 20)]}...");
    }

    private sealed class FakeDocumentTranslationService(
        GlobalSettingsService settingsService,
        ILogger<DocumentTranslationService> logger)
        : DocumentTranslationService(settingsService, null!, null!, null!, null!, logger)
    {
        public override Task<string> TranslateAsync(string text, string targetLanguage)
            => Task.FromResult($"[{targetLanguage}] {text}");
    }

    private static async Task SeedAsync(TemplateDbContext db, params object[] entities)
    {
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private static MarkdownDocument CreateDoc(Guid id, string title = "Test", string content = "Content here",
        bool isPublic = true, DateTime? updatedAt = null, string sourceCulture = "en-US")
    {
        return new MarkdownDocument
        {
            Id = id,
            Title = title,
            Content = content,
            UserId = "test-user",
            IsPublic = isPublic,
            SourceCulture = sourceCulture,
            UpdatedAt = updatedAt ?? DateTime.UtcNow
        };
    }

    private static LocalizedAbstract CreateAbstract(Guid docId, string culture, DateTime? lastGenerated = null)
    {
        return new LocalizedAbstract
        {
            DocumentId = docId,
            Culture = culture,
            Abstract = $"Old abstract in {culture}",
            LastGeneratedAt = lastGenerated ?? DateTime.UtcNow
        };
    }

    private async Task<GenerateAbstractDocumentsJob> CreateJobAsync(
        string languages = "en-US,ja-JP",
        string endpoint = "https://ollama.example.com/v1/chat/completions",
        string model = "qwen3")
    {
        await using (var seedDb = new SqliteTestContext(_dbOptions))
        {
            seedDb.GlobalSettings.Add(new GlobalSetting { Key = SettingsMap.LocalizationLanguages, Value = languages });
            seedDb.GlobalSettings.Add(new GlobalSetting { Key = SettingsMap.OpenAiChatEndpoint, Value = endpoint });
            seedDb.GlobalSettings.Add(new GlobalSetting { Key = SettingsMap.OpenAiModel, Value = model });
            await seedDb.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var db = new SqliteTestContext(_dbOptions);
        var settings = new GlobalSettingsService(db, config, null!, _cache);
        var generator = new FakeAbstractGenerationService(settings, NullLogger<AbstractGenerationService>.Instance);
        var translator = new FakeDocumentTranslationService(settings, NullLogger<DocumentTranslationService>.Instance);

        return new GenerateAbstractDocumentsJob(
            db,
            settings,
            generator,
            translator,
            NullLogger<GenerateAbstractDocumentsJob>.Instance);
    }

    // ── 1. Skip when AI is not enabled ──────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_AiDisabled_Skips()
    {
        var job = await CreateJobAsync(endpoint: "");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid());
        await SeedAsync(db, doc);

        await job.ExecuteAsync();

        var abstracts = await db.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(0, abstracts.Count);
    }

    // ── 2. Skip when no languages configured ────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_EmptyLanguages_Skips()
    {
        var job = await CreateJobAsync(languages: "");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid());
        await SeedAsync(db, doc);

        await job.ExecuteAsync();

        var abstracts = await db.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(0, abstracts.Count);
    }

    // ── 3. Skip when SourceCulture is null ──────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_SkipsNullSourceCulture()
    {
        var job = await CreateJobAsync();
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid(), sourceCulture: null!);
        await SeedAsync(db, doc);

        await job.ExecuteAsync();

        var abstracts = await db.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(0, abstracts.Count,
            "Documents with null SourceCulture should be skipped.");
    }

    // ── 4. Source abstract always generated, even when source not in configured list ──

    [TestMethod]
    public async Task ExecuteAsync_GeneratesEvenWhenSourceNotConfigured()
    {
        var job = await CreateJobAsync(languages: "ja-JP,zh-TW");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid(), sourceCulture: "en-US");
        await SeedAsync(db, doc);

        await job.ExecuteAsync();

        var abstracts = await db.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(3, abstracts.Count,
            "Source abstract (en-US) should still be generated, then translated to ja-JP and zh-TW.");
    }

    // ── 5. Skips non-public documents ───────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_SkipsNonPublicDocuments()
    {
        var job = await CreateJobAsync();
        await using var db = new SqliteTestContext(_dbOptions);

        var privateDoc = CreateDoc(Guid.NewGuid(), isPublic: false);
        await SeedAsync(db, privateDoc);

        await job.ExecuteAsync();

        var abstracts = await db.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(0, abstracts.Count);
    }

    // ── 6. Generates source abstract and translates ─────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_GeneratesForDocumentWithNoAbstract()
    {
        var job = await CreateJobAsync();
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid(), content: "This is a blog post about AI and vector search.");
        await SeedAsync(db, doc);

        await job.ExecuteAsync();

        db.ChangeTracker.Clear();

        var abstracts = await db.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(2, abstracts.Count);

        var enRow = abstracts.First(a => a.Culture == "en-US");
        Assert.IsTrue(enRow.Abstract.StartsWith("[en-US]"));

        var jaRow = abstracts.First(a => a.Culture == "ja-JP");
        Assert.IsTrue(jaRow.Abstract.StartsWith("[ja-JP]"),
            "JA abstract should be translated from en-US source.");
    }

    // ── 7. Skips documents with up-to-date abstracts ────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_SkipsAlreadyUpToDateAbstracts()
    {
        var job = await CreateJobAsync();
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid(), updatedAt: DateTime.UtcNow.AddHours(-1));
        var freshEn = CreateAbstract(doc.Id, "en-US", lastGenerated: DateTime.UtcNow);
        await SeedAsync(db, doc, freshEn);

        await job.ExecuteAsync();

        db.ChangeTracker.Clear();

        var abstracts = await db.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(2, abstracts.Count);
        var enRow = abstracts.First(a => a.Culture == "en-US");
        Assert.AreEqual("Old abstract in en-US", enRow.Abstract,
            "Up-to-date en-US abstract should not be overwritten.");
        var jaRow = abstracts.First(a => a.Culture == "ja-JP");
        Assert.IsTrue(jaRow.Abstract.StartsWith("[ja-JP]"),
            "Missing JA abstract should be translated from en-US.");
    }

    // ── 8. Re-generates stale abstracts ─────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_ReGeneratesStaleAbstracts()
    {
        var job = await CreateJobAsync();
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid(), content: "Updated content", updatedAt: DateTime.UtcNow);
        var staleEn = CreateAbstract(doc.Id, "en-US", lastGenerated: DateTime.UtcNow.AddHours(-2));
        await SeedAsync(db, doc, staleEn);

        await job.ExecuteAsync();

        db.ChangeTracker.Clear();

        var abstracts = await db.LocalizedAbstracts.ToListAsync();
        var enRow = abstracts.First(a => a.Culture == "en-US");
        Assert.IsTrue(enRow.Abstract.StartsWith("[en-US]"),
            "Stale en-US abstract should be regenerated.");
    }

    // ── 9. Only source culture configured ───────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_OnlySourceCulture_GeneratesOne()
    {
        var job = await CreateJobAsync(languages: "en-US");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid(), content: "Blog post.");
        await SeedAsync(db, doc);

        await job.ExecuteAsync();

        db.ChangeTracker.Clear();

        var abstracts = await db.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(1, abstracts.Count);
        Assert.AreEqual("en-US", abstracts[0].Culture);
    }

    // ── 10. Multiple languages ──────────────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_ProcessesMultipleLanguages()
    {
        var job = await CreateJobAsync("en-US,ja-JP,zh-TW");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid(), content: "Multi-language blog post.");
        await SeedAsync(db, doc);

        await job.ExecuteAsync();

        db.ChangeTracker.Clear();

        var abstracts = await db.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(3, abstracts.Count);
        Assert.IsTrue(abstracts.Any(a => a.Culture == "en-US"));
        Assert.IsTrue(abstracts.Any(a => a.Culture == "ja-JP"));
        Assert.IsTrue(abstracts.Any(a => a.Culture == "zh-TW"));
    }
}
