using System.Diagnostics.CodeAnalysis;
using Aiursoft.MoongladeV2.Entities;
using Aiursoft.MoongladeV2.Util;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services;

/// <summary>
/// In-memory cache of document embedding vectors for fast cosine-similarity search.
/// Loaded at startup and refreshed periodically via <see cref="BackgroundJobs.RefreshDocumentEmbeddingCacheJob"/>.
/// Registered as a singleton — thread-safe via an atomic snapshot swap.
/// </summary>
[ExcludeFromCodeCoverage]
public class DocumentEmbeddingCache(ILogger<DocumentEmbeddingCache> logger)
{
    private const int MaxEntries = 10_000;
    private Dictionary<Guid, float[]> _cache = [];
    private readonly Lock _lock = new();

    public int Count
    {
        get { lock (_lock) return _cache.Count; }
    }

    /// <summary>Returns an immutable snapshot of the current cache for a single search run.</summary>
    public Dictionary<Guid, float[]> Snapshot()
    {
        lock (_lock) return new Dictionary<Guid, float[]>(_cache);
    }

    public async Task LoadAsync(TemplateDbContext db)
    {
        var embeddings = await db.MarkdownDocuments
            .AsNoTracking()
            .Where(d => d.Embedding != null)
            .Select(d => new { d.Id, d.Embedding })
            .ToListAsync();

        var newCache = new Dictionary<Guid, float[]>();
        foreach (var item in embeddings)
        {
            var vector = EmbeddingHelper.Deserialize(item.Embedding!);
            if (vector != null)
            {
                newCache[item.Id] = vector;
            }
            else
            {
                logger.LogWarning("Failed to deserialize embedding for document {DocumentId}: byte length {Length} is not a multiple of 4.",
                    item.Id, item.Embedding!.Length);
            }
        }

        if (newCache.Count > MaxEntries)
        {
            logger.LogWarning(
                "DocumentEmbeddingCache: loaded {Count} embeddings exceeds limit of {Limit}. Capping.",
                newCache.Count, MaxEntries);
            newCache = newCache.Take(MaxEntries).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        lock (_lock)
        {
            _cache = newCache;
        }
    }
}
