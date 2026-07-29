using Franklin.MtService.Abstractions;
using Franklin.MtService.Models;

namespace Franklin.MtService.Storage;

public sealed class InMemoryTranslationStore(
    int maximumEntries,
    TimeProvider timeProvider) : ITranslationStore
{
    private readonly Dictionary<Guid, TranslationRecord> _records = [];
    private readonly Dictionary<string, Guid> _cacheIndex =
        new(StringComparer.Ordinal);
    private readonly Queue<Guid> _insertionOrder = [];
    private readonly object _sync = new();
    private readonly int _maximumEntries = Math.Max(1, maximumEntries);

    public int Count
    {
        get
        {
            lock (_sync)
            {
                SweepExpired();
                return _records.Count;
            }
        }
    }

    public ValueTask<TranslationRecord?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            SweepExpired();
            return ValueTask.FromResult(
                _records.TryGetValue(id, out var record) ? record : null);
        }
    }

    public ValueTask<TranslationRecord?> GetByCacheKeyAsync(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            SweepExpired();
            if (_cacheIndex.TryGetValue(cacheKey, out var id) &&
                _records.TryGetValue(id, out var record))
            {
                return ValueTask.FromResult<TranslationRecord?>(record);
            }

            return ValueTask.FromResult<TranslationRecord?>(null);
        }
    }

    public ValueTask SaveAsync(
        TranslationRecord record,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            SweepExpired();

            if (_cacheIndex.TryGetValue(record.CacheKey, out var existingId))
            {
                Remove(existingId);
            }

            while (_records.Count >= _maximumEntries)
            {
                EvictOldest();
            }

            _records[record.Id] = record;
            _cacheIndex[record.CacheKey] = record.Id;
            _insertionOrder.Enqueue(record.Id);
        }

        return ValueTask.CompletedTask;
    }

    private void SweepExpired()
    {
        var now = timeProvider.GetUtcNow();
        var expiredIds = _records
            .Where(pair => pair.Value.ExpiresAt <= now)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var id in expiredIds)
        {
            Remove(id);
        }
    }

    private void EvictOldest()
    {
        while (_insertionOrder.TryDequeue(out var id))
        {
            if (_records.ContainsKey(id))
            {
                Remove(id);
                return;
            }
        }
    }

    private void Remove(Guid id)
    {
        if (_records.Remove(id, out var record))
        {
            _cacheIndex.Remove(record.CacheKey);
        }
    }
}
