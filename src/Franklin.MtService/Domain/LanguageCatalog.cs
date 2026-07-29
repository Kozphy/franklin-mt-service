using System.Text.RegularExpressions;
using Franklin.MtService.Models;

namespace Franklin.MtService.Domain;

public static partial class LanguageCatalog
{
    private static readonly IReadOnlyList<LanguageInfo> Catalog =
    [
        new("de", "German"),
        new("en", "English"),
        new("es", "Spanish"),
        new("fr", "French"),
        new("it", "Italian"),
        new("ja", "Japanese"),
        new("ko", "Korean"),
        new("pt", "Portuguese"),
        new("zh-Hans", "Chinese (Simplified)"),
        new("zh-Hant", "Chinese (Traditional)")
    ];

    public static IReadOnlyList<LanguageInfo> All => Catalog;

    public static bool IsValidCode(string value) => LanguageCodePattern().IsMatch(value);

    public static string Normalize(string value)
    {
        var segments = value.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return string.Empty;
        }

        segments[0] = segments[0].ToLowerInvariant();
        for (var index = 1; index < segments.Length; index++)
        {
            segments[index] = segments[index].Length switch
            {
                2 => segments[index].ToUpperInvariant(),
                4 => char.ToUpperInvariant(segments[index][0]) +
                     segments[index][1..].ToLowerInvariant(),
                _ => segments[index]
            };
        }

        return string.Join('-', segments);
    }

    [GeneratedRegex(
        "^[A-Za-z]{2,3}(?:-[A-Za-z]{4})?(?:-(?:[A-Za-z]{2}|[0-9]{3}))?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex LanguageCodePattern();
}
