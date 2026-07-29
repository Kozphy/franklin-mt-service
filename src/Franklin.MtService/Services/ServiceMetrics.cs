using Franklin.MtService.Abstractions;
using Franklin.MtService.Models;

namespace Franklin.MtService.Services;

public sealed class ServiceMetrics(TimeProvider timeProvider)
{
    private long _requests;
    private long _cacheHits;
    private long _successfulTranslations;
    private long _failedTranslations;

    public DateTimeOffset StartedAt { get; } = timeProvider.GetUtcNow();

    public void RecordRequest() => Interlocked.Increment(ref _requests);

    public void RecordCacheHit() => Interlocked.Increment(ref _cacheHits);

    public void RecordSuccess() => Interlocked.Increment(ref _successfulTranslations);

    public void RecordFailure() => Interlocked.Increment(ref _failedTranslations);

    public ServiceStatus Snapshot(
        ITranslationProvider provider,
        ITranslationStore store)
    {
        var now = timeProvider.GetUtcNow();
        return new ServiceStatus(
            provider.IsConfigured ? "ready" : "degraded",
            provider.Name,
            provider.IsConfigured,
            StartedAt,
            Math.Max(0, (long)(now - StartedAt).TotalSeconds),
            store.Count,
            Interlocked.Read(ref _requests),
            Interlocked.Read(ref _cacheHits),
            Interlocked.Read(ref _successfulTranslations),
            Interlocked.Read(ref _failedTranslations));
    }
}
