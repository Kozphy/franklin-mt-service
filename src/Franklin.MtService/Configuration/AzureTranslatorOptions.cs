namespace Franklin.MtService.Configuration;

public sealed class AzureTranslatorOptions
{
    public const string SectionName = "AzureTranslator";

    public string Endpoint { get; init; } = "https://api.cognitive.microsofttranslator.com";

    public string Key { get; init; } = string.Empty;

    public string Region { get; init; } = string.Empty;

    public bool IsConfigured =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out _) &&
        !string.IsNullOrWhiteSpace(Key) &&
        !string.IsNullOrWhiteSpace(Region);
}
