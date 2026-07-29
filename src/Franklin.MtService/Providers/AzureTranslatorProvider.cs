using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Franklin.MtService.Abstractions;
using Franklin.MtService.Configuration;
using Franklin.MtService.Domain;
using Franklin.MtService.Models;
using Microsoft.Extensions.Options;

namespace Franklin.MtService.Providers;

public sealed class AzureTranslatorProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<AzureTranslatorOptions> options) : ITranslationProvider
{
    private readonly AzureTranslatorOptions _options = options.Value;

    public string Name => "azure";

    public bool IsConfigured => _options.IsConfigured;

    public async Task<ProviderTranslation> TranslateAsync(
        string text,
        string? sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new ProviderUnavailableException(
                "Azure Translator is selected but its endpoint, key, or region is missing.");
        }

        var query = $"api-version=3.0&to={Uri.EscapeDataString(targetLanguage)}";
        if (!string.IsNullOrWhiteSpace(sourceLanguage))
        {
            query += $"&from={Uri.EscapeDataString(sourceLanguage)}";
        }

        var requestUri = $"{_options.Endpoint.TrimEnd('/')}/translate?{query}";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _options.Key);
        request.Headers.Add("Ocp-Apim-Subscription-Region", _options.Region);
        request.Content = JsonContent.Create(new[] { new { Text = text } });

        var client = httpClientFactory.CreateClient(nameof(AzureTranslatorProvider));
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests ||
            (int)response.StatusCode >= 500)
        {
            throw new TransientProviderException(
                $"Azure Translator returned HTTP {(int)response.StatusCode}.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderUnavailableException(
                $"Azure Translator rejected the request with HTTP {(int)response.StatusCode}.");
        }

        try
        {
            await using var content = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                content,
                cancellationToken: cancellationToken);
            var result = document.RootElement[0];
            var translatedText = result
                .GetProperty("translations")[0]
                .GetProperty("text")
                .GetString();

            var detectedLanguage = sourceLanguage;
            if (result.TryGetProperty("detectedLanguage", out var detected))
            {
                detectedLanguage = detected.GetProperty("language").GetString();
            }

            if (string.IsNullOrWhiteSpace(translatedText))
            {
                throw new JsonException("The provider response did not include translated text.");
            }

            return new ProviderTranslation(translatedText, detectedLanguage);
        }
        catch (JsonException exception)
        {
            throw new ProviderUnavailableException(
                "Azure Translator returned an unexpected response.",
                exception);
        }
    }
}
