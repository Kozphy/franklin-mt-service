using Franklin.MtService.Models;

namespace Franklin.MtService.Abstractions;

public interface ITranslationProvider
{
    string Name { get; }

    bool IsConfigured { get; }

    Task<ProviderTranslation> TranslateAsync(
        string text,
        string? sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken);
}
