using Franklin.MtService.Models;

namespace Franklin.MtService.Abstractions;

public interface ITranslationStore
{
    int Count { get; }

    ValueTask<TranslationRecord?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    ValueTask<TranslationRecord?> GetByCacheKeyAsync(
        string cacheKey,
        CancellationToken cancellationToken);

    ValueTask SaveAsync(
        TranslationRecord record,
        CancellationToken cancellationToken);
}
