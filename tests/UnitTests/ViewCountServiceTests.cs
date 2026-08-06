using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.InMemory;
using Aiursoft.MoongladeV2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiursoft.MoongladeV2.Tests.UnitTests;

[TestClass]
public class ViewCountServiceTests
{
    [TestMethod]
    public async Task ArchiveAsync_PersistsIncrementsAcrossServiceInstances()
    {
        var services = new ServiceCollection();
        var databaseName = $"view-count-{Guid.NewGuid()}";
        services.AddDbContext<InMemoryContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddScoped<TemplateDbContext>(provider => provider.GetRequiredService<InMemoryContext>());
        await using var provider = services.BuildServiceProvider();
        var documentId = Guid.NewGuid();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = documentId,
                UserId = "test-user",
                IsPublic = true
            });
            await db.SaveChangesAsync();
        }

        var viewCounts = CreateService(provider);
        await viewCounts.InitializeAsync();
        viewCounts.Increment(documentId);
        viewCounts.Increment(documentId);
        await viewCounts.ArchiveAsync();

        var restartedViewCounts = CreateService(provider);
        await restartedViewCounts.InitializeAsync();
        Assert.AreEqual(2L, restartedViewCounts.GetCount(documentId));
    }

    private static ViewCountService CreateService(IServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ViewCountService>.Instance);
}
