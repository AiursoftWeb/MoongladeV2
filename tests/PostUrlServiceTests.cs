using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Aiursoft.MoongladeV2.Tests;

[TestClass]
public class PostUrlServiceTests
{
    private sealed class TestContext(DbContextOptions<TestContext> options) : TemplateDbContext(options);
    private SqliteConnection _connection = null!;
    private DbContextOptions<TestContext> _options = null!;

    [TestInitialize]
    public void Initialize()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = OFF;";
            command.ExecuteNonQuery();
        }
        _options = new DbContextOptionsBuilder<TestContext>().UseSqlite(_connection).Options;
        using var db = new TestContext(_options);
        db.Database.EnsureCreated();
    }

    [TestCleanup]
    public void Cleanup() => _connection.Dispose();

    [TestMethod]
    [DataRow("hello-world", true)]
    [DataRow("post-2", true)]
    [DataRow("Hello", false)]
    [DataRow("two--hyphens", false)]
    [DataRow("-leading", false)]
    [DataRow("中文", false)]
    public void IsValid_EnforcesAsciiSlugRules(string slug, bool expected) =>
        Assert.AreEqual(expected, PostUrlService.IsValid(slug));

    [TestMethod]
    public void BuildUrl_UsesDatedSlug_WhenSlugExists()
    {
        var document = Document("something-cool", new DateTime(2026, 6, 28));

        Assert.AreEqual("/post/2026/06/28/something-cool", PostUrlService.BuildUrl(document));
    }

    [TestMethod]
    public void BuildUrl_UsesDocumentId_WhenSlugIsMissing()
    {
        var document = Document(null, new DateTime(2026, 6, 28));

        Assert.AreEqual($"/post/{document.Id}", PostUrlService.BuildUrl(document));
    }

    [TestMethod]
    public async Task ChangeAsync_CreatesAlias_AndRequiresConfirmationToRestoreIt()
    {
        await using var db = new TestContext(_options);
        var document = Document("first-slug");
        db.MarkdownDocuments.Add(document);
        await db.SaveChangesAsync();
        var service = new PostUrlService(db);

        Assert.AreEqual(SlugChangeResult.Success, await service.ChangeAsync(document, "second-slug", false));
        var alias = await db.PostSlugAliases.SingleAsync();
        Assert.AreEqual("first-slug", alias.Slug);
        Assert.AreEqual(alias.RetiredAt.AddDays(180), alias.ExpiresAt);

        Assert.AreEqual(SlugChangeResult.ConfirmationRequired,
            await service.ChangeAsync(document, "first-slug", false));
        Assert.AreEqual(SlugChangeResult.Success,
            await service.ChangeAsync(document, "first-slug", true));
        Assert.AreEqual("first-slug", document.Slug);
        Assert.AreEqual("second-slug", (await db.PostSlugAliases.SingleAsync()).Slug);
    }

    [TestMethod]
    public async Task ChangeAsync_AllowsSameSlugOnAnotherDate_ButRejectsActiveAlias()
    {
        await using var db = new TestContext(_options);
        var first = Document("shared", new DateTime(2026, 6, 30));
        var nextDay = Document(null, new DateTime(2026, 7, 1));
        var sameDay = Document(null, new DateTime(2026, 6, 30));
        db.AddRange(first, nextDay, sameDay);
        await db.SaveChangesAsync();
        var service = new PostUrlService(db);

        Assert.AreEqual(SlugChangeResult.Success, await service.ChangeAsync(nextDay, "shared", false));
        Assert.AreEqual(SlugChangeResult.Success, await service.ChangeAsync(first, "new", false));
        Assert.AreEqual(SlugChangeResult.Occupied, await service.ChangeAsync(sameDay, "shared", false));
    }

    [TestMethod]
    public async Task ChangeAsync_WorksWithRetryingExecutionStrategy()
    {
        var options = new DbContextOptionsBuilder<TestContext>()
            .UseSqlite(_connection)
            .ReplaceService<IExecutionStrategyFactory, TestRetryingExecutionStrategyFactory>()
            .Options;
        await using var db = new TestContext(options);
        var document = Document(null);
        db.MarkdownDocuments.Add(document);
        await db.SaveChangesAsync();

        var result = await new PostUrlService(db).ChangeAsync(document, "generated-slug", false);

        Assert.AreEqual(SlugChangeResult.Success, result);
        Assert.AreEqual("generated-slug", await db.MarkdownDocuments
            .Where(d => d.Id == document.Id)
            .Select(d => d.Slug)
            .SingleAsync());
    }

    private sealed class TestRetryingExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new TestRetryingExecutionStrategy(dependencies);
    }

    private sealed class TestRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }

    private static MarkdownDocument Document(string? slug, DateTime? created = null) => new()
    {
        Id = Guid.NewGuid(), UserId = "test", CreationTime = created ?? DateTime.UtcNow,
        Title = "Test", Content = "Test", IsPublic = true, Slug = slug,
        SlugDate = slug == null ? null : (created ?? DateTime.UtcNow).Date
    };
}
