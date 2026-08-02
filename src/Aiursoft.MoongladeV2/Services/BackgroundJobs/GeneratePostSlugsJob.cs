using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services.BackgroundJobs;

public class GeneratePostSlugsJob(TemplateDbContext db, SlugGenerationService generator,
    PostUrlService postUrls, ILogger<GeneratePostSlugsJob> logger) : IBackgroundJob
{
    public string Name => "Generate Post Slugs";
    public string Description => "Generates readable slugs for up to 20 public posts which do not have one.";

    public async Task ExecuteAsync()
    {
        var documents = await db.MarkdownDocuments.Where(d => d.IsPublic && d.Slug == null)
            .OrderBy(d => d.CreationTime).ThenBy(d => d.Id).Take(20).ToListAsync();
        foreach (var document in documents)
        {
            try
            {
                var generated = PostUrlService.Normalize(await generator.GenerateAsync(document.Title ?? "Untitled"));
                if (!PostUrlService.IsValid(generated))
                {
                    logger.LogWarning("AI returned an invalid slug for document {DocumentId}.", document.Id);
                    continue;
                }

                // The author may have saved a slug while the AI request was in flight.
                await db.Entry(document).ReloadAsync();
                if (document.Slug != null) continue;

                var baseSlug = generated!;
                var candidate = baseSlug;
                for (var suffix = 1; suffix <= 100; suffix++)
                {
                    var result = await postUrls.ChangeAsync(document, candidate, false);
                    if (result == SlugChangeResult.Success) break;
                    if (result is not (SlugChangeResult.Occupied or SlugChangeResult.ConfirmationRequired)) break;
                    var ending = $"-{suffix + 1}";
                    candidate = baseSlug[..Math.Min(baseSlug.Length, PostUrlService.MaxSlugLength - ending.Length)] + ending;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to generate a slug for document {DocumentId}.", document.Id);
            }
        }
    }
}
