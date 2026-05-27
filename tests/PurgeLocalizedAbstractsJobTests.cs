using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Services.BackgroundJobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiursoft.MoongladeV2.Tests;

[TestClass]
public class PurgeLocalizedAbstractsJobTests
{
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

    private static LocalizedAbstract CreateAbstract(Guid docId, string culture)
    {
        return new LocalizedAbstract
        {
            DocumentId = docId,
            Culture = culture,
            Abstract = $"Abstract in {culture}",
            LastGeneratedAt = DateTime.UtcNow
        };
    }

    private static PurgeLocalizedAbstractsJob CreateJob(DbContextOptions<SqliteTestContext> options)
    {
        var db = new SqliteTestContext(options);
        return new PurgeLocalizedAbstractsJob(db, NullLogger<PurgeLocalizedAbstractsJob>.Instance);
    }

    // ── Deletes all LocalizedAbstract rows ─────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_DeletesAllRows()
    {
        await using var seedDb = new SqliteTestContext(_dbOptions);
        var doc1 = CreateDoc(Guid.NewGuid());
        var doc2 = CreateDoc(Guid.NewGuid());
        var abs1 = CreateAbstract(doc1.Id, "en-US");
        var abs2 = CreateAbstract(doc2.Id, "ja-JP");
        var abs3 = CreateAbstract(doc2.Id, "zh-CN");
        await SeedAsync(seedDb, doc1, doc2, abs1, abs2, abs3);

        var job = CreateJob(_dbOptions);
        await job.ExecuteAsync();

        await using var checkDb = new SqliteTestContext(_dbOptions);
        var remaining = await checkDb.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(0, remaining.Count);
    }

    // ── Does not delete MarkdownDocuments ──────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_DoesNotDeleteDocuments()
    {
        await using var seedDb = new SqliteTestContext(_dbOptions);
        var doc = CreateDoc(Guid.NewGuid());
        var abs = CreateAbstract(doc.Id, "en-US");
        await SeedAsync(seedDb, doc, abs);

        var job = CreateJob(_dbOptions);
        await job.ExecuteAsync();

        await using var checkDb = new SqliteTestContext(_dbOptions);
        var docs = await checkDb.MarkdownDocuments.ToListAsync();
        Assert.AreEqual(1, docs.Count);
        Assert.AreEqual(doc.Id, docs[0].Id);
    }

    // ── Handles empty table gracefully ─────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_EmptyTable_NoError()
    {
        await using var seedDb = new SqliteTestContext(_dbOptions);
        var doc = CreateDoc(Guid.NewGuid());
        await SeedAsync(seedDb, doc);

        var job = CreateJob(_dbOptions);
        await job.ExecuteAsync();

        await using var checkDb = new SqliteTestContext(_dbOptions);
        var remaining = await checkDb.LocalizedAbstracts.ToListAsync();
        Assert.AreEqual(0, remaining.Count);
    }
}
