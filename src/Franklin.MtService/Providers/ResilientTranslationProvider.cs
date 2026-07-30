using Franklin.MtService.Abstractions;
using Franklin.MtService.Configuration;
using Franklin.MtService.Domain;
using Franklin.MtService.Models;

namespace Franklin.MtService.Providers;

public sealed class ResilientTranslationProvider(
    ITranslationProvider inner,
    TranslationOptions options,
    TimeProvider timeProvider,
    ILogger<ResilientTranslationProvider> logger) : ITranslationProvider
{
    private readonly object _circuitLock = new();
    private int _consecutiveFailures;
    private DateTimeOffset? _circuitOpenUntil;

    public string Name => inner.Name;

    public bool IsConfigured => inner.IsConfigured;

    public async Task<ProviderTranslation> TranslateAsync(
        string text,
        string? sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        ThrowIfCircuitOpen();

        using var overallCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overallCancellation.CancelAfter(TimeSpan.FromSeconds(options.OverallTimeoutSeconds));

        Exception? lastException = null;
        var attempts = options.RetryCount + 1;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var attemptCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(overallCancellation.Token);
            attemptCancellation.CancelAfter(
                TimeSpan.FromSeconds(options.RequestTimeoutSeconds));

            try
            {
                var result = await inner.TranslateAsync(
                    text,
                    sourceLanguage,
                    targetLanguage,
                    attemptCancellation.Token);
                RecordSuccess();
                return result;
            }
            catch (ProviderUnavailableException)
            {
                throw;
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                lastException = exception;
            }
            catch (TransientProviderException exception)
            {
                lastException = exception;
            }
            catch (HttpRequestException exception)
            {
                lastException = exception;
            }

            if (attempt >= attempts || overallCancellation.IsCancellationRequested)
            {
                break;
            }

            var delay = CalculateDelay(attempt);
            logger.LogWarning(
                lastException,
                "Translation provider attempt {Attempt} of {Attempts} failed. Retrying in {DelayMs} ms.",
                attempt,
                attempts,
                delay.TotalMilliseconds);
            await Task.Delay(delay, overallCancellation.Token);
        }

        RecordFailure();
        throw new ProviderUnavailableException(
            $"Translation provider '{Name}' did not respond after {attempts} attempts.",
            lastException);
    }

    private TimeSpan CalculateDelay(int attempt)
    {
        var exponentialDelay = options.RetryBaseDelayMilliseconds * Math.Pow(2, attempt - 1);
        var boundedDelay = Math.Min(exponentialDelay, options.RetryMaxDelayMilliseconds);
        var jitterMultiplier = 0.75 + (Random.Shared.NextDouble() * 0.5);
        return TimeSpan.FromMilliseconds(boundedDelay * jitterMultiplier);
    }

    private void ThrowIfCircuitOpen()
    {
        lock (_circuitLock)
        {
            if (_circuitOpenUntil is null)
            {
                return;
            }

            var now = timeProvider.GetUtcNow();
            if (now >= _circuitOpenUntil.Value)
            {
                _circuitOpenUntil = null;
                _consecutiveFailures = 0;
                logger.LogInformation(
                    "Translation provider circuit moved to half-open state for {Provider}.",
                    Name);
                return;
            }

            throw new ProviderUnavailableException(
                $"Translation provider '{Name}' circuit is open until {_circuitOpenUntil:O}.");
        }
    }

    private void RecordSuccess()
    {
        lock (_circuitLock)
        {
            _consecutiveFailures = 0;
            _circuitOpenUntil = null;
        }
    }

    private void RecordFailure()
    {
        lock (_circuitLock)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures < options.CircuitBreakerFailureThreshold)
            {
                return;
            }

            _circuitOpenUntil = timeProvider.GetUtcNow()
                .AddSeconds(options.CircuitBreakerBreakSeconds);
            logger.LogError(
                "Translation provider circuit opened for {Provider} until {OpenUntil} after {FailureCount} consecutive failures.",
                Name,
                _circuitOpenUntil,
                _consecutiveFailures);
        }
    }
}
