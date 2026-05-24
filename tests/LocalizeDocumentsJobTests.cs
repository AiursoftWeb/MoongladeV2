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
public class LocalizeDocumentsJobTests
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

    // Translates text by simply prepending the culture — no LLM calls.
    private sealed class FakeTranslationService(
        GlobalSettingsService settingsService)
        : DocumentTranslationService(
            settingsService,
            null!,  // MarkdownShredder
            null!,  // RetryEngine
            null!,  // ILogger<OllamaBasedTranslatorEngine>
            null!,  // ChatClient
            null!)  // ILogger<DocumentTranslationService>
    {
        public override Task<string> TranslateAsync(string text, string targetLanguage)
            => Task.FromResult($"[{targetLanguage}] {text}");
    }

    private static async Task SeedAsync(TemplateDbContext db, params object[] entities)
    {
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private static MarkdownDocument CreateDoc(Guid id, string title = "Test", bool isPublic = true,
        DateTime? updatedAt = null)
    {
        return new MarkdownDocument
        {
            Id = id,
            Title = title,
            Content = $"Content of {title}",
            UserId = "test-user",
            IsPublic = isPublic,
            UpdatedAt = updatedAt ?? DateTime.UtcNow
        };
    }

    private static LocalizedDocument CreateLocalized(Guid docId, string culture, DateTime? lastLocalized = null)
    {
        return new LocalizedDocument
        {
            DocumentId = docId,
            Culture = culture,
            LocalizedTitle = $"Old {culture} title",
            LastLocalizedAt = lastLocalized ?? DateTime.UtcNow
        };
    }

    private async Task<LocalizeDocumentsJob> CreateJobAsync(
        string languages = "en,ja",
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
        var translator = new FakeTranslationService(settings);

        return new LocalizeDocumentsJob(
            db,
            settings,
            translator,
            NullLogger<LocalizeDocumentsJob>.Instance);
    }

    // ── 1. Skip when AI is not enabled ──────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_AiDisabled_Skips()
    {
        var job = await CreateJobAsync(endpoint: ""); // empty endpoint disables AI
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid());
        await SeedAsync(db, doc);

        await job.ExecuteAsync();

        var translations = await db.LocalizedDocuments.ToListAsync();
        Assert.AreEqual(0, translations.Count, "No translations should be created when AI is disabled.");
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

        var translations = await db.LocalizedDocuments.ToListAsync();
        Assert.AreEqual(0, translations.Count, "No translations should be created when no languages are configured.");
    }

    // ── 3. Skips non-public documents ───────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_SkipsNonPublicDocuments()
    {
        var job = await CreateJobAsync();
        await using var db = new SqliteTestContext(_dbOptions);

        var privateDoc = CreateDoc(Guid.NewGuid(), isPublic: false);
        await SeedAsync(db, privateDoc);

        await job.ExecuteAsync();

        var translations = await db.LocalizedDocuments.ToListAsync();
        Assert.AreEqual(0, translations.Count, "Non-public documents should be skipped.");
    }

    // ── 4. Translates documents with no existing localization ────────────────────

    [TestMethod]
    public async Task ExecuteAsync_TranslatesDocumentWithNoLocalization()
    {
        var job = await CreateJobAsync();
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid(), title: "Hello World");
        await SeedAsync(db, doc);

        await job.ExecuteAsync();

        // Reset tracker to read fresh state
        db.ChangeTracker.Clear();

        var translations = await db.LocalizedDocuments.ToListAsync();
        Assert.AreEqual(2, translations.Count, "Should create one row per configured language (en, ja).");
        Assert.IsTrue(translations.Any(t => t.Culture == "en" && t.LocalizedTitle == "[en] Hello World"));
        Assert.IsTrue(translations.Any(t => t.Culture == "ja" && t.LocalizedTitle == "[ja] Hello World"));
    }

    // ── 5. Skips documents with up-to-date translations ─────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_SkipsAlreadyUpToDateTranslations()
    {
        var job = await CreateJobAsync();
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid(), title: "Fresh Doc", updatedAt: DateTime.UtcNow.AddHours(-1));
        var freshEn = CreateLocalized(doc.Id, "en", lastLocalized: DateTime.UtcNow); // newer than UpdatedAt
        await SeedAsync(db, doc, freshEn);

        await job.ExecuteAsync();

        db.ChangeTracker.Clear();

        var translations = await db.LocalizedDocuments.ToListAsync();
        Assert.AreEqual(2, translations.Count);
        var enRow = translations.First(t => t.Culture == "en");
        Assert.AreEqual("Old en title", enRow.LocalizedTitle,
            "Up-to-date EN translation should not be overwritten.");
        var jaRow = translations.First(t => t.Culture == "ja");
        Assert.IsTrue(jaRow.LocalizedTitle.StartsWith("[ja]"),
            "Missing JA translation should be created.");
    }

    // ── 6. Re-translates documents with stale translations ──────────────────────

    [TestMethod]
    public async Task ExecuteAsync_ReTranslatesStaleDocuments()
    {
        var job = await CreateJobAsync();
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid(), title: "Updated Doc", updatedAt: DateTime.UtcNow);
        var staleEn = CreateLocalized(doc.Id, "en",
            lastLocalized: DateTime.UtcNow.AddHours(-2)); // older than UpdatedAt
        await SeedAsync(db, doc, staleEn);

        await job.ExecuteAsync();

        db.ChangeTracker.Clear();

        var translations = await db.LocalizedDocuments.ToListAsync();
        var enRow = translations.First(t => t.Culture == "en");
        Assert.AreEqual("[en] Updated Doc", enRow.LocalizedTitle,
            "Stale translation should be overwritten with fresh content.");
    }

    // ── 7. Processes multiple languages for the same document ────────────────────

    [TestMethod]
    public async Task ExecuteAsync_ProcessesMultipleLanguages()
    {
        var job = await CreateJobAsync("en,ja,zh");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid(), title: "MultiLang");
        await SeedAsync(db, doc);

        await job.ExecuteAsync();

        db.ChangeTracker.Clear();

        var translations = await db.LocalizedDocuments.ToListAsync();
        Assert.AreEqual(3, translations.Count);
        Assert.IsTrue(translations.Any(t => t.Culture == "en"));
        Assert.IsTrue(translations.Any(t => t.Culture == "ja"));
        Assert.IsTrue(translations.Any(t => t.Culture == "zh"));
        Assert.IsTrue(translations.All(t => t.LocalizedTitle.Contains("MultiLang")));
    }
}
