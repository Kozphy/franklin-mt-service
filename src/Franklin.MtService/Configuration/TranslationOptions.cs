namespace Franklin.MtService.Configuration;

public sealed class TranslationOptions
{
    public const string SectionName = "Translation";

    public string Provider { get; init; } = "Local";

    public int MaxTextLength { get; init; } = 5_000;

    public int CacheTtlMinutes { get; init; } = 60;

    public int MaxCacheEntries { get; init; } = 10_000;

    public int RequestTimeoutSeconds { get; init; } = 10;

    public int RetryCount { get; init; } = 2;
}
