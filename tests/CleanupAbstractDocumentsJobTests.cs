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
public class CleanupAbstractDocumentsJobTests
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

    private async Task<CleanupAbstractDocumentsJob> CreateJobAsync(string languages = "en,ja")
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

        return new CleanupAbstractDocumentsJob(
            db,
            settings,
            NullLogger<CleanupAbstractDocumentsJob>.Instance);
    }

    private static async Task SeedAsync(TemplateDbContext db, params object[] entities)
    {
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private static MarkdownDocument CreateDoc(Guid id)
    {
        return new MarkdownDocument
        {
            Id = id,
            Title = "Test",
            Content = "test",
            UserId = "test-user",
            IsPublic = true
        };
    }

    private static LocalizedAbstract CreateAbstract(int id, Guid docId, string culture, DateTime? lastGenerated = null)
    {
        return new LocalizedAbstract
        {
            Id = id,
            DocumentId = docId,
            Culture = culture,
            Abstract = $"Abstract for {docId} in {culture}",
            LastGeneratedAt = lastGenerated ?? DateTime.UtcNow
        };
    }

    [TestMethod]
    public async Task ExecuteAsync_DeletesOrphanedAbstracts()
    {
        var job = await CreateJobAsync();
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid());
        var orphaned = CreateAbstract(1, Guid.NewGuid(), "en", DateTime.UtcNow.AddHours(-1));
        var validRow = CreateAbstract(2, doc.Id, "en", DateTime.UtcNow.AddHours(-1));

        await SeedAsync(db, doc, orphaned, validRow);

        await job.ExecuteAsync();

        var remaining = await db.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(1, remaining.Count);
        Assert.AreEqual(doc.Id, remaining[0].DocumentId);
    }

    [TestMethod]
    public async Task ExecuteAsync_DeletesStaleCultureRows()
    {
        var job = await CreateJobAsync("en");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid());
        var enRow = CreateAbstract(1, doc.Id, "en", DateTime.UtcNow.AddHours(-1));
        var jaRow = CreateAbstract(2, doc.Id, "ja", DateTime.UtcNow.AddHours(-1));

        await SeedAsync(db, doc, enRow, jaRow);

        await job.ExecuteAsync();

        var remaining = await db.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(1, remaining.Count);
        Assert.AreEqual("en", remaining[0].Culture);
    }

    [TestMethod]
    public async Task ExecuteAsync_KeepsActiveCultureRows()
    {
        var job = await CreateJobAsync("en,ja,zh");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid());
        var enRow = CreateAbstract(1, doc.Id, "en", DateTime.UtcNow.AddHours(-2));
        var jaRow = CreateAbstract(2, doc.Id, "ja", DateTime.UtcNow.AddHours(-2));

        await SeedAsync(db, doc, enRow, jaRow);

        await job.ExecuteAsync();

        var remaining = await db.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(2, remaining.Count);
    }

    [TestMethod]
    public async Task ExecuteAsync_KeepsFreshOrphanedRows()
    {
        var job = await CreateJobAsync("en");
        await using var db = new SqliteTestContext(_dbOptions);

        var orphanId = Guid.NewGuid();
        var freshOrphan = CreateAbstract(1, orphanId, "en", DateTime.UtcNow);

        await SeedAsync(db, freshOrphan);

        await job.ExecuteAsync();

        var remaining = await db.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(1, remaining.Count,
            "Freshly-created orphan row must survive the staleness guard.");
    }

    [TestMethod]
    public async Task ExecuteAsync_KeepsFreshStaleCultureRows()
    {
        var job = await CreateJobAsync("en");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc1 = CreateDoc(Guid.NewGuid());
        var doc2 = CreateDoc(Guid.NewGuid());
        var freshJaRow = CreateAbstract(1, doc1.Id, "ja", DateTime.UtcNow);
        var oldJaRow = CreateAbstract(2, doc2.Id, "ja", DateTime.UtcNow.AddHours(-2));

        await SeedAsync(db, doc1, doc2, freshJaRow, oldJaRow);

        await job.ExecuteAsync();

        var remaining = await db.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(1, remaining.Count);
        Assert.AreEqual(freshJaRow.Id, remaining[0].Id);
    }

    [TestMethod]
    public async Task ExecuteAsync_EmptyLanguagesSetting_DoesNotDeleteByCulture()
    {
        var job = await CreateJobAsync("");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid());
        var enRow = CreateAbstract(1, doc.Id, "en", DateTime.UtcNow.AddHours(-2));

        await SeedAsync(db, doc, enRow);

        await job.ExecuteAsync();

        var remaining = await db.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(1, remaining.Count);
    }

    [TestMethod]
    public async Task ExecuteAsync_DeletesBothOrphanedAndStaleCultureInOneRun()
    {
        var job = await CreateJobAsync("en");
        await using var db = new SqliteTestContext(_dbOptions);

        var doc = CreateDoc(Guid.NewGuid());
        var orphanId = Guid.NewGuid();
        var orphanedRow = CreateAbstract(1, orphanId, "en", DateTime.UtcNow.AddHours(-2));
        var staleJaRow = CreateAbstract(2, doc.Id, "ja", DateTime.UtcNow.AddHours(-2));
        var validRow = CreateAbstract(3, doc.Id, "en", DateTime.UtcNow.AddHours(-2));

        await SeedAsync(db, doc, orphanedRow, staleJaRow, validRow);

        await job.ExecuteAsync();

        var remaining = await db.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(1, remaining.Count);
        Assert.AreEqual(validRow.Id, remaining[0].Id);
    }
}
