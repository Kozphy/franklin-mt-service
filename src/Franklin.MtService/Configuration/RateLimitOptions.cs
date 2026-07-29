namespace Franklin.MtService.Configuration;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    public int PermitLimit { get; init; } = 60;

    public int WindowSeconds { get; init; } = 60;

    public int QueueLimit { get; init; } = 10;
}
