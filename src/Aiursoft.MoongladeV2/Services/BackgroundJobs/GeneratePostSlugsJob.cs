using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services.BackgroundJobs;

public class GeneratePostSlugsJob(TemplateDbContext db, SlugGenerationService generator,
    PostUrlService postUrls, ILogger<GeneratePostSlugsJob> logger) : IBackgroundJob
{
    public string Name => "Generate Post Slugs";
    public string Description => "Generates readable slugs for up to 20 published posts or non-empty drafts without a slug.";

    public async Task ExecuteAsync()
    {
        var documents = await db.MarkdownDocuments.ForPreparation()
            .Where(d => d.Slug == null)
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

                // The author may have edited the draft or saved a slug during generation.
                var sourceUpdatedAt = document.UpdatedAt;
                await db.Entry(document).ReloadAsync();
                if (db.Entry(document).State == EntityState.Detached || document.Slug != null ||
                    document.UpdatedAt != sourceUpdatedAt) continue;

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
