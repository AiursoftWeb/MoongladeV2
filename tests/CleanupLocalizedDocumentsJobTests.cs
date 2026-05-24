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
public class CleanupLocalizedDocumentsJobTests
{
    // Concrete context for SQLite in-memory tests — enables ExecuteDeleteAsync
    // which the InMemory provider does not support.
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

    private async Task<CleanupLocalizedDocumentsJob> CreateJobAsync(string languages = "en,ja")
    {
        await using (var seedDb = new SqliteTestContext(_dbOptions))
        {
            seedDb.GlobalSettings.Add(new GlobalSetting
            {
                Key = SettingsMap.LocalizationLanguages,
                Value = languages
            });
            await seedDb.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var db = new SqliteTestContext(_dbOptions);
        var settings = new GlobalSettingsService(db, config, null!, _cache);

        return new CleanupLocalizedDocumentsJob(
            db,
            settings,
            NullLogger<CleanupLocalizedDocumentsJob>.Instance);
    }

    private static async Task SeedAsync(TemplateDbContext db, params object[] entities)
    {
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private static MarkdownDocument CreateDoc(Guid id, string title = "Test Doc")
    {
        return new MarkdownDocument
        {
            Id = id,
            Title = title,
            Content = "test",
            UserId = "test-user",
            IsPublic = true
        };
    }

    private static LocalizedDocument CreateLocalized(int id, Guid docId, string culture, DateTime? lastLocalized = null)
    {
        return new LocalizedDocument
        {
            Id = id,
            DocumentId = docId,
            Culture = culture,
            LocalizedTitle = $"Doc {docId} in {culture}",
            LastLocalizedAt = lastLocalized ?? DateTime.UtcNow
        };
    }

    // ── 1. Deletes orphaned rows (parent document deleted) ──────────────────────

    [TestMethod]
    public async Task ExecuteAsync_DeletesOrphanedLocalizedDocuments()
    {
        var job = await CreateJobAsync();
        await using var db = new SqliteTestContext(_dbOptions);

        var doc1 = CreateDoc(Guid.NewGuid());
        var doc2 = CreateDoc(Guid.NewGuid());
        var orphaned = CreateLocalized(1, Guid.NewGuid(), "en", DateTime.UtcNow.AddHours(-1)); // no parent doc
        var validRow = CreateLocalized(2, doc2.Id, "en", DateTime.UtcNow.AddHours(-1));

        await SeedAsync(db, doc1, doc2, orphaned, validRow);

        await job.ExecuteAsync();

        var remaining = await db.LocalizedDocuments.ToListAsync();
        Assert.AreEqual(1, remaining.Count, "Only the valid row should remain.");
        Assert.AreEqual(doc2.Id, remaining[0].DocumentId, "Valid localized row should survive.");
    }

    // ── 2. Deletes rows for cultures no longer configured ───────────────────────

    [TestMethod]
    public async Task ExecuteAsync_DeletesStaleCultureRows()
    {
        var job = await CreateJobAsync("en"); // only "en" configured
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid());
        var enRow = CreateLocalized(1, doc.Id, "en", DateTime.UtcNow.AddHours(-1));
        var jaRow = CreateLocalized(2, doc.Id, "ja", DateTime.UtcNow.AddHours(-1));

        await SeedAsync(db, doc, enRow, jaRow);

        await job.ExecuteAsync();

        var remaining = await db.LocalizedDocuments.ToListAsync();
        Assert.AreEqual(1, remaining.Count);
        Assert.AreEqual("en", remaining[0].Culture, "Only the configured culture row should survive.");
    }

    // ── 3. Does NOT delete rows for active cultures ─────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_KeepsActiveCultureRows()
    {
        var job = await CreateJobAsync("en,ja,zh");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid());
        var enRow = CreateLocalized(1, doc.Id, "en", DateTime.UtcNow.AddHours(-2));
        var jaRow = CreateLocalized(2, doc.Id, "ja", DateTime.UtcNow.AddHours(-2));

        await SeedAsync(db, doc, enRow, jaRow);

        await job.ExecuteAsync();

        var remaining = await db.LocalizedDocuments.ToListAsync();
        Assert.AreEqual(2, remaining.Count, "All active culture rows should remain.");
    }

    // ── 4. Staleness guard: fresh orphaned rows survive ─────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_KeepsFreshOrphanedRows()
    {
        var job = await CreateJobAsync("en");
        await using var db = new SqliteTestContext(_dbOptions);

        var orphanId = Guid.NewGuid(); // no parent document
        var freshOrphan = CreateLocalized(1, orphanId, "en", DateTime.UtcNow);

        await SeedAsync(db, freshOrphan);

        await job.ExecuteAsync();

        var remaining = await db.LocalizedDocuments.ToListAsync();
        Assert.AreEqual(1, remaining.Count,
            "Freshly-created orphan row must survive the staleness guard.");
    }

    // ── 5. Staleness guard: fresh stale-culture rows survive ────────────────────

    [TestMethod]
    public async Task ExecuteAsync_KeepsFreshStaleCultureRows()
    {
        var job = await CreateJobAsync("en"); // "ja" is not configured
        await using var db = new SqliteTestContext(_dbOptions);

        var doc1 = CreateDoc(Guid.NewGuid());
        var doc2 = CreateDoc(Guid.NewGuid());
        var freshJaRow = CreateLocalized(1, doc1.Id, "ja", DateTime.UtcNow);
        var oldJaRow = CreateLocalized(2, doc2.Id, "ja", DateTime.UtcNow.AddHours(-2));

        await SeedAsync(db, doc1, doc2, freshJaRow, oldJaRow);

        await job.ExecuteAsync();

        var remaining = await db.LocalizedDocuments.ToListAsync();
        Assert.AreEqual(1, remaining.Count,
            "Only the fresh stale-culture row should survive the staleness guard.");
        Assert.AreEqual(freshJaRow.Id, remaining[0].Id);
    }

    // ── 6. Empty languages configuration — culture cleanup skipped ──────────────

    [TestMethod]
    public async Task ExecuteAsync_EmptyLanguagesSetting_DoesNotDeleteByCulture()
    {
        var job = await CreateJobAsync("");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid());
        var enRow = CreateLocalized(1, doc.Id, "en", DateTime.UtcNow.AddHours(-2));

        await SeedAsync(db, doc, enRow);

        await job.ExecuteAsync();

        var remaining = await db.LocalizedDocuments.ToListAsync();
        Assert.AreEqual(1, remaining.Count,
            "Rows for non-orphaned docs must survive when no languages are configured.");
    }

    // ── 7. Both deletions happen in one run ─────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_DeletesBothOrphanedAndStaleCultureInOneRun()
    {
        var job = await CreateJobAsync("en"); // only "en" configured
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid());
        var orphanId = Guid.NewGuid(); // no parent document
        var orphanedRow = CreateLocalized(1, orphanId, "en", DateTime.UtcNow.AddHours(-2));
        var staleJaRow = CreateLocalized(2, doc.Id, "ja", DateTime.UtcNow.AddHours(-2));
        var validRow = CreateLocalized(3, doc.Id, "en", DateTime.UtcNow.AddHours(-2));

        await SeedAsync(db, doc, orphanedRow, staleJaRow, validRow);

        await job.ExecuteAsync();

        var remaining = await db.LocalizedDocuments.ToListAsync();
        Assert.AreEqual(1, remaining.Count, "Only the valid row should survive.");
        Assert.AreEqual(validRow.Id, remaining[0].Id);
    }
}
