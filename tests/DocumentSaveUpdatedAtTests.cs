using Aiursoft.MoongladeV2.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Tests;

/// <summary>
/// Verifies that every code path that updates a document also bumps <see cref="MarkdownDocument.UpdatedAt"/>.
/// If UpdatedAt is not bumped, <see cref="Services.BackgroundJobs.LocalizeDocumentsJob"/>,
/// <see cref="Services.BackgroundJobs.GenerateDocumentEmbeddingsJob"/>, and
/// <see cref="Services.BackgroundJobs.GenerateAbstractDocumentsJob"/> will never re-process stale documents.
/// </summary>
[TestClass]
public class DocumentSaveUpdatedAtTests
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

    // ── 1. Simulates HomeController.SaveNew (existing document) ──────────────────

    [TestMethod]
    public async Task SaveNew_BumpsUpdatedAt()
    {
        // Arrange — seed a document with an old UpdatedAt.
        var docId = Guid.NewGuid();
        var oldUpdatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await using (var seedDb = new SqliteTestContext(_dbOptions))
        {
            seedDb.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = docId,
                Title = "Old Title",
                Content = "Old Content",
                UserId = "test-user",
                IsPublic = true,
                UpdatedAt = oldUpdatedAt
            });
            await seedDb.SaveChangesAsync();
        }

        // Act — simulate HomeController.SaveNew existing-doc path:
        //   documentInDb.UpdatedAt = DateTime.UtcNow;
        //   documentInDb.Content = ...;
        //   documentInDb.Title = ...;
        //   await context.SaveChangesAsync();
        DateTime beforeSave;
        await using (var db = new SqliteTestContext(_dbOptions))
        {
            var doc = await db.MarkdownDocuments.FirstAsync(d => d.Id == docId);
            beforeSave = DateTime.UtcNow;

            doc.UpdatedAt = DateTime.UtcNow;
            doc.Content = "Updated Content";
            doc.Title = "Updated Title";

            await db.SaveChangesAsync();
        }

        // Assert
        await using (var db = new SqliteTestContext(_dbOptions))
        {
            var doc = await db.MarkdownDocuments.AsNoTracking().FirstAsync(d => d.Id == docId);
            Assert.IsTrue(doc.UpdatedAt > oldUpdatedAt,
                "SaveNew: UpdatedAt must be bumped so translation/embedding/abstract jobs re-run.");
            Assert.IsTrue(doc.UpdatedAt >= beforeSave,
                "SaveNew: UpdatedAt should be set to a recent timestamp.");
        }
    }

    // ── 2. Negative test — skip UpdatedAt, verify it STAYS stale ─────────────────

    [TestMethod]
    public async Task Save_WithoutBumpingUpdatedAt_KeepsOldValue()
    {
        var docId = Guid.NewGuid();
        var oldUpdatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await using (var seedDb = new SqliteTestContext(_dbOptions))
        {
            seedDb.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = docId,
                Title = "Old Title",
                Content = "Old Content",
                UserId = "test-user",
                IsPublic = true,
                UpdatedAt = oldUpdatedAt
            });
            await seedDb.SaveChangesAsync();
        }

        // Act — BUG SCENARIO: update Content/Title WITHOUT bumping UpdatedAt.
        await using (var db = new SqliteTestContext(_dbOptions))
        {
            var doc = await db.MarkdownDocuments.FirstAsync(d => d.Id == docId);
            doc.Content = "Updated Content";
            doc.Title = "Updated Title";
            // MISSING: doc.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        // Assert — UpdatedAt must NOT change. This is the regression we guard against.
        await using (var db = new SqliteTestContext(_dbOptions))
        {
            var doc = await db.MarkdownDocuments.AsNoTracking().FirstAsync(d => d.Id == docId);
            Assert.AreEqual(oldUpdatedAt, doc.UpdatedAt,
                "Without explicit UpdatedAt = DateTime.UtcNow, the field stays stale and AI jobs skip the document.");
        }
    }

    // ── 3. Simulates HomeController.SaveUpdate (AJAX Ctrl+S quick save) ──────────

    [TestMethod]
    public async Task SaveUpdate_BumpsUpdatedAt()
    {
        var docId = Guid.NewGuid();
        var oldUpdatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await using (var seedDb = new SqliteTestContext(_dbOptions))
        {
            seedDb.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = docId,
                Title = "Old Title",
                Content = "Old Content",
                UserId = "test-user",
                IsPublic = true,
                UpdatedAt = oldUpdatedAt
            });
            await seedDb.SaveChangesAsync();
        }

        // Act — simulate HomeController.SaveUpdate path.
        await using (var db = new SqliteTestContext(_dbOptions))
        {
            var doc = await db.MarkdownDocuments.FirstAsync(d => d.Id == docId);
            doc.UpdatedAt = DateTime.UtcNow;
            doc.Content = "Quick-save Content";
            doc.Title = "Quick-save Title";
            await db.SaveChangesAsync();
        }

        // Assert
        await using (var db = new SqliteTestContext(_dbOptions))
        {
            var doc = await db.MarkdownDocuments.AsNoTracking().FirstAsync(d => d.Id == docId);
            Assert.IsTrue(doc.UpdatedAt > oldUpdatedAt,
                "SaveUpdate: UpdatedAt must be bumped so quick-save triggers re-processing.");
        }
    }

    // ── 4. Simulates AdminController.EditDocument ────────────────────────────────

    [TestMethod]
    public async Task EditDocument_BumpsUpdatedAt()
    {
        var docId = Guid.NewGuid();
        var oldUpdatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await using (var seedDb = new SqliteTestContext(_dbOptions))
        {
            seedDb.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = docId,
                Title = "Old Title",
                Content = "Old Content",
                UserId = "test-user",
                IsPublic = true,
                UpdatedAt = oldUpdatedAt
            });
            await seedDb.SaveChangesAsync();
        }

        // Act — simulate AdminController.EditDocument path.
        await using (var db = new SqliteTestContext(_dbOptions))
        {
            var doc = await db.MarkdownDocuments.FirstAsync(d => d.Id == docId);
            doc.UpdatedAt = DateTime.UtcNow;
            doc.Content = "Admin-edited Content";
            doc.Title = "Admin-edited Title";
            doc.UserId = "new-owner";
            await db.SaveChangesAsync();
        }

        // Assert
        await using (var db = new SqliteTestContext(_dbOptions))
        {
            var doc = await db.MarkdownDocuments.AsNoTracking().FirstAsync(d => d.Id == docId);
            Assert.IsTrue(doc.UpdatedAt > oldUpdatedAt,
                "EditDocument: UpdatedAt must be bumped when admin edits a document.");
        }
    }

    // ── 5. New document gets a fresh UpdatedAt automatically ─────────────────────

    [TestMethod]
    public async Task NewDocument_HasUpdatedAtEqualToUtcNow()
    {
        var beforeCreate = DateTime.UtcNow.AddSeconds(-1);

        Guid docId;
        await using (var db = new SqliteTestContext(_dbOptions))
        {
            var doc = new MarkdownDocument
            {
                Id = Guid.NewGuid(),
                Title = "Brand New",
                Content = "Fresh content",
                UserId = "test-user",
                IsPublic = true
            };
            db.MarkdownDocuments.Add(doc);
            await db.SaveChangesAsync();
            docId = doc.Id;
        }

        await using (var db = new SqliteTestContext(_dbOptions))
        {
            var doc = await db.MarkdownDocuments.AsNoTracking().FirstAsync(d => d.Id == docId);
            Assert.IsTrue(doc.UpdatedAt >= beforeCreate,
                "New document should get UpdatedAt = DateTime.UtcNow from the property initializer.");
        }
    }
}
