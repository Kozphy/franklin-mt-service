using Franklin.MtService.Abstractions;
using Franklin.MtService.Configuration;
using Franklin.MtService.Domain;
using Franklin.MtService.Models;
using Franklin.MtService.Providers;
using Franklin.MtService.Services;
using Franklin.MtService.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Franklin.MtService.Specs;

public static class Program
{
    public static async Task<int> Main()
    {
        var specifications = new (string Name, Func<Task> Execute)[]
        {
            ("local provider translates a known phrase", LocalProviderTranslatesKnownPhrase),
            ("translation service reuses a cached result", TranslationServiceUsesCache),
            ("translation service coalesces concurrent work", TranslationServiceCoalescesWork),
            ("request validation rejects invalid input", ValidationRejectsInvalidInput),
            ("store evicts its oldest record at capacity", StoreEvictsAtCapacity),
            ("resilient provider retries transient failures", ResilientProviderRetries)
        };

        var failures = 0;
        foreach (var specification in specifications)
        {
            try
            {
                await specification.Execute();
                Console.WriteLine($"PASS {specification.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine(
                    $"FAIL {specification.Name}: {exception.Message}");
            }
        }

        Console.WriteLine(
            $"{specifications.Length - failures}/{specifications.Length} specifications passed.");
        return failures == 0 ? 0 : 1;
    }

    private static async Task LocalProviderTranslatesKnownPhrase()
    {
        var provider = new LocalDictionaryTranslationProvider();
        var result = await provider.TranslateAsync(
            "Hello world",
            "en",
            "es",
            CancellationToken.None);

        Equal("Hola mundo", result.Text);
        Equal("en", result.DetectedSourceLanguage);
    }

    private static async Task TranslationServiceUsesCache()
    {
        var options = CreateOptions();
        var store = new InMemoryTranslationStore(
            options.MaxCacheEntries,
            TimeProvider.System);
        var provider = new LocalDictionaryTranslationProvider();
        var metrics = new ServiceMetrics(TimeProvider.System);
        var service = new TranslationService(
            provider,
            store,
            Options.Create(options),
            metrics,
            TimeProvider.System,
            NullLogger<TranslationService>.Instance);
        var request = new TranslationRequest("Thank you", "fr", "en");

        var first = await service.TranslateAsync(request, CancellationToken.None);
        var second = await service.TranslateAsync(request, CancellationToken.None);

        False(first.Cached, "The initial request must not be marked as cached.");
        True(second.Cached, "The repeated request must be marked as cached.");
        Equal(first.Id, second.Id);
        Equal("Merci", second.TranslatedText);
    }

    private static Task ValidationRejectsInvalidInput()
    {
        try
        {
            TranslationRequestValidator.Validate(
                new TranslationRequest(" ", "not_a_language"),
                CreateOptions());
        }
        catch (TranslationValidationException exception)
        {
            True(exception.Errors.ContainsKey("text"), "A text error is required.");
            True(
                exception.Errors.ContainsKey("targetLanguage"),
                "A target-language error is required.");
            return Task.CompletedTask;
        }

        throw new InvalidOperationException("Expected validation to fail.");
    }

    private static async Task TranslationServiceCoalescesWork()
    {
        var options = CreateOptions();
        var provider = new DelayedProvider();
        var service = new TranslationService(
            provider,
            new InMemoryTranslationStore(options.MaxCacheEntries, TimeProvider.System),
            Options.Create(options),
            new ServiceMetrics(TimeProvider.System),
            TimeProvider.System,
            NullLogger<TranslationService>.Instance);
        var request = new TranslationRequest("Concurrent request", "es", "en");

        var results = await Task.WhenAll(
            service.TranslateAsync(request, CancellationToken.None),
            service.TranslateAsync(request, CancellationToken.None));

        Equal(1, provider.Attempts);
        Equal(results[0].Id, results[1].Id);
        True(
            results.Count(result => result.Cached) == 1,
            "Exactly one coalesced response should be marked as reused.");
    }

    private static async Task StoreEvictsAtCapacity()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var clock = new ManualTimeProvider(now);
        var store = new InMemoryTranslationStore(1, clock);
        var first = CreateRecord("first", now);
        var second = CreateRecord("second", now.AddSeconds(1));

        await store.SaveAsync(first, CancellationToken.None);
        await store.SaveAsync(second, CancellationToken.None);

        var evicted = await store.GetByIdAsync(first.Id, CancellationToken.None);
        var retained = await store.GetByIdAsync(second.Id, CancellationToken.None);
        True(evicted is null, "The oldest record should be evicted.");
        True(retained is not null, "The newest record should be retained.");
        Equal(1, store.Count);
    }

    private static async Task ResilientProviderRetries()
    {
        var inner = new EventuallySuccessfulProvider(2);
        var provider = new ResilientTranslationProvider(
            inner,
            CreateOptions(),
            NullLogger<ResilientTranslationProvider>.Instance);

        var result = await provider.TranslateAsync(
            "Hello",
            "en",
            "es",
            CancellationToken.None);

        Equal("translated", result.Text);
        Equal(3, inner.Attempts);
    }

    private static TranslationOptions CreateOptions() =>
        new()
        {
            Provider = "Local",
            MaxTextLength = 5_000,
            CacheTtlMinutes = 60,
            MaxCacheEntries = 100,
            RequestTimeoutSeconds = 2,
            RetryCount = 2
        };

    private static TranslationRecord CreateRecord(
        string cacheKey,
        DateTimeOffset createdAt) =>
        new(
            Guid.NewGuid(),
            cacheKey,
            "source",
            "translated",
            "en",
            "es",
            "test",
            createdAt,
            createdAt.AddMinutes(5));

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Expected '{expected}', but received '{actual}'.");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void False(bool condition, string message) => True(!condition, message);

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class EventuallySuccessfulProvider(
        int failuresBeforeSuccess) : ITranslationProvider
    {
        public int Attempts { get; private set; }

        public string Name => "eventual";

        public bool IsConfigured => true;

        public Task<ProviderTranslation> TranslateAsync(
            string text,
            string? sourceLanguage,
            string targetLanguage,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attempts++;
            if (Attempts <= failuresBeforeSuccess)
            {
                throw new TransientProviderException("Try again.");
            }

            return Task.FromResult(new ProviderTranslation("translated", sourceLanguage));
        }
    }

    private sealed class DelayedProvider : ITranslationProvider
    {
        public int Attempts { get; private set; }

        public string Name => "delayed";

        public bool IsConfigured => true;

        public async Task<ProviderTranslation> TranslateAsync(
            string text,
            string? sourceLanguage,
            string targetLanguage,
            CancellationToken cancellationToken)
        {
            Attempts++;
            await Task.Delay(50, cancellationToken);
            return new ProviderTranslation("translated once", sourceLanguage);
        }
    }
}
