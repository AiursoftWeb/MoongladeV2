using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Services.FileStorage;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services.BackgroundJobs;

/// <summary>
/// Scans the markdown-images storage directory and deletes any image file that is no longer
/// referenced by any document's markdown content in the database. Also skips files newer than
/// seven hours to avoid deleting images that were just uploaded but not yet saved.
/// </summary>
public class OrphanMarkdownImageCleanupJob(
    TemplateDbContext db,
    FeatureFoldersProvider folders,
    ILogger<OrphanMarkdownImageCleanupJob> logger) : IBackgroundJob
{
    // 7h = one job cycle (6h) + 1h safety buffer, ensuring every image survives
    // at least one full cleanup pass before being eligible for deletion.
    private static readonly TimeSpan GracePeriod = TimeSpan.FromHours(7);

    public string Name => "Orphan Markdown Image Cleanup";

    public string Description =>
        "Scans the markdown-images storage directory and deletes image files " +
        "that are no longer referenced by any document, freeing disk space. " +
        "Files newer than seven hours are always kept.";

    public async Task ExecuteAsync()
    {
        logger.LogInformation("OrphanMarkdownImageCleanupJob started.");

        // Load both source and localized documents. Do not parse Markdown here: an image can be
        // referenced by an absolute URL, HTML, reference-style Markdown, or another valid syntax.
        // A conservative path search is safer for a destructive cleanup job.
        var allContent = await db.MarkdownDocuments
            .AsNoTracking()
            .Select(d => d.Content)
            .ToListAsync();
        allContent.AddRange(await db.LocalizedDocuments
            .AsNoTracking()
            .Select(d => d.LocalizedContent)
            .ToListAsync());

        // 2. Scan the workspace for files inside the 'markdown-images/' subdirectory.
        var workspace = folders.GetWorkspaceFolder();
        var imagesDir = Path.Combine(workspace, "markdown-images");

        if (!Directory.Exists(imagesDir))
        {
            logger.LogInformation(
                "OrphanMarkdownImageCleanupJob: markdown-images directory does not exist — nothing to clean.");
            return;
        }

        var allImageFiles = Directory
            .EnumerateFiles(imagesDir, "*", SearchOption.AllDirectories)
            .ToList();

        logger.LogInformation(
            "OrphanMarkdownImageCleanupJob: {Count} file(s) found in markdown-images directory.",
            allImageFiles.Count);

        var referencedPaths = allImageFiles
            .Select(physicalPath => Path
                .GetRelativePath(workspace, physicalPath)
                .Replace('\\', '/'))
            .Where(relativePath => IsReferenced(relativePath, allContent))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        logger.LogInformation(
            "OrphanMarkdownImageCleanupJob: {Count} markdown-images path(s) are referenced in the database.",
            referencedPaths.Count);

        // 3. Delete files that are not referenced AND older than the grace period.
        var cutoff = DateTime.UtcNow - GracePeriod;
        var deletedCount = 0;
        foreach (var physicalPath in allImageFiles)
        {
            var relativePath = Path
                .GetRelativePath(workspace, physicalPath)
                .Replace('\\', '/');

            if (referencedPaths.Contains(relativePath))
                continue;

            // Grace period: keep files that were recently uploaded (not yet saved to a document).
            var lastWriteTime = File.GetLastWriteTimeUtc(physicalPath);
            if (lastWriteTime >= cutoff)
            {
                logger.LogInformation(
                    "OrphanMarkdownImageCleanupJob: skipping '{RelativePath}' — within grace period (uploaded {Age:N0}h ago).",
                    relativePath, (DateTime.UtcNow - lastWriteTime).TotalHours);
                continue;
            }

            try
            {
                File.Delete(physicalPath);
                deletedCount++;
                logger.LogInformation(
                    "OrphanMarkdownImageCleanupJob: deleted orphan image '{RelativePath}'.",
                    relativePath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "OrphanMarkdownImageCleanupJob: failed to delete '{RelativePath}'.",
                    relativePath);
            }
        }

        logger.LogInformation(
            "OrphanMarkdownImageCleanupJob finished. {Deleted}/{Total} orphan file(s) removed.",
            deletedCount, allImageFiles.Count);
    }

    private static bool IsReferenced(string relativePath, IEnumerable<string?> allContent)
    {
        var fullyEscapedPath = Uri.EscapeDataString(relativePath);
        var escapedPath = fullyEscapedPath
            .Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
        var windowsPath = relativePath.Replace('/', '\\');

        foreach (var content in allContent)
        {
            if (string.IsNullOrEmpty(content)) continue;
            if (ContainsCompletePath(content, relativePath) ||
                ContainsCompletePath(content, escapedPath) ||
                ContainsCompletePath(content, fullyEscapedPath) ||
                ContainsCompletePath(content, windowsPath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsCompletePath(string content, string path)
    {
        var searchFrom = 0;
        while (searchFrom < content.Length)
        {
            var index = content.IndexOf(path, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return false;

            var characterAfterPath = index + path.Length;
            if (characterAfterPath == content.Length ||
                !IsPathContinuationCharacter(content[characterAfterPath]))
            {
                return true;
            }

            searchFrom = index + 1;
        }

        return false;
    }

    private static bool IsPathContinuationCharacter(char character) =>
        char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or '~' or '%' or '/' or '\\';
}
