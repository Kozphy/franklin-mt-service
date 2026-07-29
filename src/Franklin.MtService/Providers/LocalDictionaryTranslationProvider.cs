using System.Text.RegularExpressions;
using Franklin.MtService.Abstractions;
using Franklin.MtService.Models;

namespace Franklin.MtService.Providers;

public sealed partial class LocalDictionaryTranslationProvider : ITranslationProvider
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>
        Phrases = new Dictionary<string, IReadOnlyDictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["de"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["hello world"] = "Hallo Welt",
                ["good morning"] = "Guten Morgen",
                ["thank you"] = "Danke",
                ["welcome"] = "Willkommen"
            },
            ["es"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["hello world"] = "Hola mundo",
                ["good morning"] = "Buenos días",
                ["thank you"] = "Gracias",
                ["welcome"] = "Bienvenido"
            },
            ["fr"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["hello world"] = "Bonjour le monde",
                ["good morning"] = "Bonjour",
                ["thank you"] = "Merci",
                ["welcome"] = "Bienvenue"
            },
            ["ja"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["hello world"] = "こんにちは世界",
                ["good morning"] = "おはようございます",
                ["thank you"] = "ありがとうございます",
                ["welcome"] = "ようこそ"
            },
            ["zh-Hans"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["hello world"] = "你好，世界",
                ["good morning"] = "早上好",
                ["thank you"] = "谢谢",
                ["welcome"] = "欢迎"
            },
            ["zh-Hant"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["hello world"] = "你好，世界",
                ["good morning"] = "早安",
                ["thank you"] = "謝謝",
                ["welcome"] = "歡迎"
            }
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>
        Words = new Dictionary<string, IReadOnlyDictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["de"] = CreateWordDictionary(
                ("hello", "hallo"),
                ("world", "Welt"),
                ("good", "gut"),
                ("morning", "Morgen"),
                ("service", "Dienst"),
                ("translation", "Übersetzung")),
            ["es"] = CreateWordDictionary(
                ("hello", "hola"),
                ("world", "mundo"),
                ("good", "bueno"),
                ("morning", "mañana"),
                ("service", "servicio"),
                ("translation", "traducción")),
            ["fr"] = CreateWordDictionary(
                ("hello", "bonjour"),
                ("world", "monde"),
                ("good", "bon"),
                ("morning", "matin"),
                ("service", "service"),
                ("translation", "traduction"))
        };

    public string Name => "local";

    public bool IsConfigured => true;

    public Task<ProviderTranslation> TranslateAsync(
        string text,
        string? sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Phrases.TryGetValue(targetLanguage, out var phraseCatalog) &&
            phraseCatalog.TryGetValue(text.Trim(), out var phrase))
        {
            return Task.FromResult(
                new ProviderTranslation(phrase, sourceLanguage ?? "en"));
        }

        var translated = TranslateKnownWords(text, targetLanguage);
        return Task.FromResult(
            new ProviderTranslation(translated, sourceLanguage ?? "en"));
    }

    private static string TranslateKnownWords(string text, string targetLanguage)
    {
        if (!Words.TryGetValue(targetLanguage, out var catalog))
        {
            return $"[local:{targetLanguage}] {text}";
        }

        var translatedAny = false;
        var result = TokenPattern().Replace(
            text,
            match =>
            {
                if (!catalog.TryGetValue(match.Value, out var replacement))
                {
                    return match.Value;
                }

                translatedAny = true;
                return ApplyCasing(match.Value, replacement);
            });

        return translatedAny ? result : $"[local:{targetLanguage}] {text}";
    }

    private static string ApplyCasing(string source, string translation)
    {
        if (source.All(char.IsUpper))
        {
            return translation.ToUpperInvariant();
        }

        if (char.IsUpper(source[0]) && translation.Length > 0)
        {
            return char.ToUpperInvariant(translation[0]) + translation[1..];
        }

        return translation;
    }

    private static IReadOnlyDictionary<string, string> CreateWordDictionary(
        params (string Source, string Translation)[] entries) =>
        entries.ToDictionary(
            entry => entry.Source,
            entry => entry.Translation,
            StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\p{L}+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
