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
public class DetectSourceCultureJobTests
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

    private sealed class FakeDetectSourceCultureJob(
        TemplateDbContext db,
        GlobalSettingsService settingsService,
        ILogger<DetectSourceCultureJob> logger,
        string? fakeCulture)
        : DetectSourceCultureJob(db, settingsService, null!, null!, logger)
    {
        protected override Task<string?> DetectCultureAsync(MarkdownDocument doc)
            => Task.FromResult(fakeCulture);
    }

    private static async Task SeedAsync(TemplateDbContext db, params object[] entities)
    {
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private static MarkdownDocument CreateDoc(Guid id, string title = "Test", string? content = "Hello world",
        bool isPublic = true, string? sourceCulture = null)
    {
        return new MarkdownDocument
        {
            Id = id,
            Title = title,
            Content = content,
            UserId = "test-user",
            IsPublic = isPublic,
            SourceCulture = sourceCulture
        };
    }

    private async Task<FakeDetectSourceCultureJob> CreateJobAsync(
        string? fakeCulture = "en-US",
        string endpoint = "https://ollama.example.com/v1/chat/completions")
    {
        await using (var seedDb = new SqliteTestContext(_dbOptions))
        {
            seedDb.GlobalSettings.Add(new GlobalSetting { Key = SettingsMap.OpenAiChatEndpoint, Value = endpoint });
            await seedDb.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var db = new SqliteTestContext(_dbOptions);
        var settings = new GlobalSettingsService(db, config, null!, _cache);

        return new FakeDetectSourceCultureJob(
            db, settings, NullLogger<DetectSourceCultureJob>.Instance, fakeCulture);
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

        var reloaded = await db.MarkdownDocuments.FirstAsync(d => d.Id == doc.Id);
        Assert.IsNull(reloaded.SourceCulture);
    }

    // ── 2. Detects and sets SourceCulture on null document ──────────────────────

    [TestMethod]
    public async Task ExecuteAsync_DetectsSourceCulture()
    {
        var job = await CreateJobAsync(fakeCulture: "zh-CN");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid(), sourceCulture: null);
        await SeedAsync(db, doc);

        await job.ExecuteAsync();

        var reloaded = await db.MarkdownDocuments.AsNoTracking().FirstAsync(d => d.Id == doc.Id);
        Assert.AreEqual("zh-CN", reloaded.SourceCulture);
    }

    // ── 3. Skips document that already has SourceCulture ────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_SkipsAlreadyClassified()
    {
        var job = await CreateJobAsync(fakeCulture: "should-not-be-called");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid(), sourceCulture: "ja-JP");
        await SeedAsync(db, doc);

        await job.ExecuteAsync();

        var reloaded = await db.MarkdownDocuments.AsNoTracking().FirstAsync(d => d.Id == doc.Id);
        Assert.AreEqual("ja-JP", reloaded.SourceCulture,
            "Already-classified document should keep its original SourceCulture.");
    }

    // ── 4. Handles null return from detection (AI failure) ─────────────────────

    [TestMethod]
    public async Task ExecuteAsync_HandlesNullDetectionResult()
    {
        var job = await CreateJobAsync(fakeCulture: null);
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid(), sourceCulture: null);
        await SeedAsync(db, doc);

        await job.ExecuteAsync();

        var reloaded = await db.MarkdownDocuments.AsNoTracking().FirstAsync(d => d.Id == doc.Id);
        Assert.IsNull(reloaded.SourceCulture,
            "Null detection result should leave SourceCulture as null.");
    }

    // ── 5. Processes multiple documents in batches ─────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_ProcessesMultipleDocuments()
    {
        var job = await CreateJobAsync(fakeCulture: "fr-FR");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc1 = CreateDoc(Guid.NewGuid(), sourceCulture: null);
        var doc2 = CreateDoc(Guid.NewGuid(), sourceCulture: null);
        var doc3 = CreateDoc(Guid.NewGuid(), sourceCulture: "de-DE"); // already classified
        await SeedAsync(db, doc1, doc2, doc3);

        await job.ExecuteAsync();

        db.ChangeTracker.Clear();

        var docs = await db.MarkdownDocuments.ToListAsync();
        Assert.AreEqual("fr-FR", docs.First(d => d.Id == doc1.Id).SourceCulture);
        Assert.AreEqual("fr-FR", docs.First(d => d.Id == doc2.Id).SourceCulture);
        Assert.AreEqual("de-DE", docs.First(d => d.Id == doc3.Id).SourceCulture,
            "Already-classified should be unchanged.");
    }

    // ── 6. Handles empty content gracefully ────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_EmptyContent_StillProcesses()
    {
        var job = await CreateJobAsync(fakeCulture: "en-US");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid(), content: "", sourceCulture: null);
        await SeedAsync(db, doc);

        await job.ExecuteAsync();

        var reloaded = await db.MarkdownDocuments.AsNoTracking().FirstAsync(d => d.Id == doc.Id);
        Assert.AreEqual("en-US", reloaded.SourceCulture);
    }
}
