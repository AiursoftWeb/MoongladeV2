using System.Collections.Concurrent;
using Aiursoft.MoongladeV2.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MoongladeV2.Services;

public sealed class ViewCountService(
    IServiceScopeFactory scopeFactory,
    ILogger<ViewCountService> logger)
{
    private sealed class Counter(long value = 0)
    {
        private long _value = value;

        public long Value => Interlocked.Read(ref _value);

        public long IncrementSaturating()
        {
            while (true)
            {
                var current = Value;
                if (current == long.MaxValue) return current;
                if (Interlocked.CompareExchange(ref _value, current + 1, current) == current) return current + 1;
            }
        }
    }

    private readonly ReaderWriterLockSlim _swapLock = new();
    private readonly SemaphoreSlim _archiveGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, Counter> _totals = new();
    private readonly Queue<ConcurrentDictionary<Guid, Counter>> _secondaryBuffers = new();
    private ConcurrentDictionary<Guid, Counter> _primary = new();
    private int _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0) return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var counts = await db.MarkdownDocuments.AsNoTracking()
                .Select(document => new { document.Id, document.ViewCount })
                .ToListAsync(cancellationToken);
            foreach (var count in counts)
            {
                _totals[count.Id] = new Counter(count.ViewCount);
            }
        }
        catch
        {
            Volatile.Write(ref _initialized, 0);
            throw;
        }
    }

    public long Increment(Guid documentId)
    {
        _swapLock.EnterReadLock();
        try
        {
            var increment = _primary.GetOrAdd(documentId, static _ => new Counter()).IncrementSaturating();
            if (increment == long.MaxValue)
            {
                logger.LogWarning("The unarchived view count for document {DocumentId} reached Int64.MaxValue.", documentId);
            }

            return _totals.GetOrAdd(documentId, static _ => new Counter()).IncrementSaturating();
        }
        finally
        {
            _swapLock.ExitReadLock();
        }
    }

    public long GetCount(Guid documentId) =>
        _totals.TryGetValue(documentId, out var counter) ? counter.Value : 0;

    public async Task ArchiveAsync(CancellationToken cancellationToken = default)
    {
        if (!await _archiveGate.WaitAsync(0, cancellationToken)) return;

        try
        {
            SwapPrimary();
            while (_secondaryBuffers.TryPeek(out var secondary))
            {
                var increments = secondary
                    .Select(pair => new KeyValuePair<Guid, long>(pair.Key, pair.Value.Value))
                    .Where(pair => pair.Value > 0)
                    .ToArray();
                if (increments.Length == 0)
                {
                    _secondaryBuffers.Dequeue();
                    continue;
                }

                try
                {
                    var missingIds = await PersistAsync(increments, cancellationToken);
                    foreach (var missingId in missingIds) _totals.TryRemove(missingId, out _);
                    _secondaryBuffers.Dequeue();
                    logger.LogInformation("Archived {ViewCount} views for {DocumentCount} documents.",
                        increments.Sum(pair => pair.Value), increments.Length - missingIds.Count);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Failed to archive view counts. The secondary buffer will be retried.");
                    break;
                }
            }
        }
        finally
        {
            _archiveGate.Release();
        }
    }

    private void SwapPrimary()
    {
        _swapLock.EnterWriteLock();
        try
        {
            var secondary = _primary;
            _primary = new ConcurrentDictionary<Guid, Counter>();
            if (!secondary.IsEmpty) _secondaryBuffers.Enqueue(secondary);
        }
        finally
        {
            _swapLock.ExitWriteLock();
        }
    }

    private async Task<HashSet<Guid>> PersistAsync(
        IReadOnlyCollection<KeyValuePair<Guid, long>> increments,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var missingIds = new HashSet<Guid>();

        if (!db.Database.IsRelational())
        {
            foreach (var increment in increments)
            {
                var document = await db.MarkdownDocuments.FindAsync([increment.Key], cancellationToken);
                if (document == null) missingIds.Add(increment.Key);
                else document.ViewCount = SaturatingAdd(document.ViewCount, increment.Value);
            }
            await db.SaveChangesAsync(cancellationToken);
            return missingIds;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var increment in increments)
        {
            var id = increment.Key;
            var delta = increment.Value;
            var updated = await db.MarkdownDocuments
                .Where(document => document.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    document => document.ViewCount,
                    document => document.ViewCount > long.MaxValue - delta
                        ? long.MaxValue
                        : document.ViewCount + delta), cancellationToken);
            if (updated == 0) missingIds.Add(id);
        }
        await transaction.CommitAsync(cancellationToken);
        return missingIds;
    }

    private static long SaturatingAdd(long value, long increment) =>
        value > long.MaxValue - increment ? long.MaxValue : value + increment;
}
