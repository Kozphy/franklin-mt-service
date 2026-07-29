namespace Franklin.MtService.Models;

public sealed record TranslationRequest(
    string? Text,
    string? TargetLanguage,
    string? SourceLanguage = null);

public sealed record TranslationResponse(
    Guid Id,
    string SourceText,
    string TranslatedText,
    string? SourceLanguage,
    string TargetLanguage,
    string Provider,
    bool Cached,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    long DurationMilliseconds)
{
    public static TranslationResponse FromRecord(
        TranslationRecord record,
        bool cached,
        long durationMilliseconds) =>
        new(
            record.Id,
            record.SourceText,
            record.TranslatedText,
            record.SourceLanguage,
            record.TargetLanguage,
            record.Provider,
            cached,
            record.CreatedAt,
            record.ExpiresAt,
            durationMilliseconds);
}

public sealed record TranslationRecord(
    Guid Id,
    string CacheKey,
    string SourceText,
    string TranslatedText,
    string? SourceLanguage,
    string TargetLanguage,
    string Provider,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record ProviderTranslation(
    string Text,
    string? DetectedSourceLanguage);

public sealed record LanguageInfo(
    string Code,
    string Name);

public sealed record ServiceStatus(
    string Status,
    string Provider,
    bool ProviderConfigured,
    DateTimeOffset StartedAt,
    long UptimeSeconds,
    int CachedTranslations,
    long Requests,
    long CacheHits,
    long SuccessfulTranslations,
    long FailedTranslations);
