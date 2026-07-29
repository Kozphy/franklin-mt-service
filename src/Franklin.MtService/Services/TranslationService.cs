using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Franklin.MtService.Abstractions;
using Franklin.MtService.Configuration;
using Franklin.MtService.Domain;
using Franklin.MtService.Models;
using Microsoft.Extensions.Options;

namespace Franklin.MtService.Services;

public sealed class TranslationService(
    ITranslationProvider provider,
    ITranslationStore store,
    IOptions<TranslationOptions> options,
    ServiceMetrics metrics,
    TimeProvider timeProvider,
    ILogger<TranslationService> logger)
{
    private readonly TranslationOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, Lazy<Task<TranslationRecord>>> _inFlight =
        new(StringComparer.Ordinal);

    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        metrics.RecordRequest();
        var timer = Stopwatch.StartNew();
        var validated = TranslationRequestValidator.Validate(request, _options);
        var cacheKey = CreateCacheKey(validated, provider.Name);

        var cached = await store.GetByCacheKeyAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            metrics.RecordCacheHit();
            return TranslationResponse.FromRecord(
                cached,
                cached: true,
                timer.ElapsedMilliseconds);
        }

        var candidate = new Lazy<Task<TranslationRecord>>(
            () => TranslateAndStoreAsync(validated, cacheKey),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var operation = _inFlight.GetOrAdd(cacheKey, candidate);
        var ownsOperation = ReferenceEquals(candidate, operation);
        var operationTask = operation.Value;

        _ = operationTask.ContinueWith(
            completedTask =>
            {
                if (_inFlight.TryGetValue(cacheKey, out var current) &&
                    ReferenceEquals(current, operation))
                {
                    _inFlight.TryRemove(cacheKey, out _);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        var translatedRecord = await operationTask.WaitAsync(cancellationToken);
        if (!ownsOperation)
        {
            metrics.RecordCacheHit();
        }

        return TranslationResponse.FromRecord(
            translatedRecord,
            cached: !ownsOperation,
            timer.ElapsedMilliseconds);
    }

    public async Task<TranslationResponse?> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var record = await store.GetByIdAsync(id, cancellationToken);
        return record is null
            ? null
            : TranslationResponse.FromRecord(record, cached: true, durationMilliseconds: 0);
    }

    private static string CreateCacheKey(
        ValidatedTranslationRequest request,
        string providerName)
    {
        var input = string.Join(
            '\u001F',
            providerName.ToLowerInvariant(),
            request.SourceLanguage?.ToLowerInvariant() ?? "auto",
            request.TargetLanguage.ToLowerInvariant(),
            request.Text);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<TranslationRecord> TranslateAndStoreAsync(
        ValidatedTranslationRequest request,
        string cacheKey)
    {
        var timer = Stopwatch.StartNew();

        try
        {
            var providerResult = await provider.TranslateAsync(
                request.Text,
                request.SourceLanguage,
                request.TargetLanguage,
                CancellationToken.None);
            var createdAt = timeProvider.GetUtcNow();
            var record = new TranslationRecord(
                Guid.NewGuid(),
                cacheKey,
                request.Text,
                providerResult.Text,
                providerResult.DetectedSourceLanguage ?? request.SourceLanguage,
                request.TargetLanguage,
                provider.Name,
                createdAt,
                createdAt.AddMinutes(_options.CacheTtlMinutes));

            await store.SaveAsync(record, CancellationToken.None);
            metrics.RecordSuccess();

            logger.LogInformation(
                "Translation {TranslationId} completed with provider {Provider} in {DurationMs} ms.",
                record.Id,
                provider.Name,
                timer.ElapsedMilliseconds);
            return record;
        }
        catch
        {
            metrics.RecordFailure();
            throw;
        }
    }
}
