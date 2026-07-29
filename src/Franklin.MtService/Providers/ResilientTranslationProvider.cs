using Franklin.MtService.Abstractions;
using Franklin.MtService.Configuration;
using Franklin.MtService.Domain;
using Franklin.MtService.Models;

namespace Franklin.MtService.Providers;

public sealed class ResilientTranslationProvider(
    ITranslationProvider inner,
    TranslationOptions options,
    ILogger<ResilientTranslationProvider> logger) : ITranslationProvider
{
    public string Name => inner.Name;

    public bool IsConfigured => inner.IsConfigured;

    public async Task<ProviderTranslation> TranslateAsync(
        string text,
        string? sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        var attempts = options.RetryCount + 1;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var attemptCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCancellation.CancelAfter(
                TimeSpan.FromSeconds(options.RequestTimeoutSeconds));

            try
            {
                return await inner.TranslateAsync(
                    text,
                    sourceLanguage,
                    targetLanguage,
                    attemptCancellation.Token);
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

            if (attempt >= attempts)
            {
                break;
            }

            var delay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1));
            logger.LogWarning(
                lastException,
                "Translation provider attempt {Attempt} of {Attempts} failed. Retrying in {DelayMs} ms.",
                attempt,
                attempts,
                delay.TotalMilliseconds);
            await Task.Delay(delay, cancellationToken);
        }

        throw new ProviderUnavailableException(
            $"Translation provider '{Name}' did not respond after {attempts} attempts.",
            lastException);
    }
}
