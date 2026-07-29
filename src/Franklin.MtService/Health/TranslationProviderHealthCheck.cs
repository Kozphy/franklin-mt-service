using Franklin.MtService.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Franklin.MtService.Health;

public sealed class TranslationProviderHealthCheck(
    ITranslationProvider provider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = provider.IsConfigured
            ? HealthCheckResult.Healthy(
                $"Translation provider '{provider.Name}' is configured.")
            : HealthCheckResult.Unhealthy(
                $"Translation provider '{provider.Name}' is not configured.");
        return Task.FromResult(result);
    }
}
