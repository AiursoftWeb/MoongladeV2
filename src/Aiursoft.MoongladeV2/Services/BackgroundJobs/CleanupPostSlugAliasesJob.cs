using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services.BackgroundJobs;

public class CleanupPostSlugAliasesJob(TemplateDbContext db, ILogger<CleanupPostSlugAliasesJob> logger) : IBackgroundJob
{
    public string Name => "Cleanup Post Slug Aliases";
    public string Description => "Permanently deletes up to 500 post slug aliases which expired after 180 days.";

    public async Task ExecuteAsync()
    {
        var expired = await db.PostSlugAliases.Where(a => a.ExpiresAt <= DateTime.UtcNow)
            .OrderBy(a => a.ExpiresAt).ThenBy(a => a.Id).Take(500).ToListAsync();
        db.PostSlugAliases.RemoveRange(expired);
        await db.SaveChangesAsync();
        logger.LogInformation("Deleted {Count} expired post slug aliases.", expired.Count);
    }
}
