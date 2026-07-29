using Franklin.MtService.Configuration;
using Franklin.MtService.Models;

namespace Franklin.MtService.Domain;

public static class TranslationRequestValidator
{
    public static ValidatedTranslationRequest Validate(
        TranslationRequest request,
        TranslationOptions options)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            errors["text"] = ["Text is required."];
        }
        else if (request.Text.Length > options.MaxTextLength)
        {
            errors["text"] =
            [
                $"Text cannot exceed {options.MaxTextLength} characters."
            ];
        }

        if (string.IsNullOrWhiteSpace(request.TargetLanguage))
        {
            errors["targetLanguage"] = ["Target language is required."];
        }
        else if (!LanguageCatalog.IsValidCode(request.TargetLanguage.Trim()))
        {
            errors["targetLanguage"] = ["Use a valid BCP 47 language code, such as es or zh-Hant."];
        }

        if (!string.IsNullOrWhiteSpace(request.SourceLanguage) &&
            !string.Equals(request.SourceLanguage, "auto", StringComparison.OrdinalIgnoreCase) &&
            !LanguageCatalog.IsValidCode(request.SourceLanguage.Trim()))
        {
            errors["sourceLanguage"] = ["Use a valid BCP 47 language code or auto."];
        }

        if (errors.Count > 0)
        {
            throw new TranslationValidationException(errors);
        }

        var sourceLanguage =
            string.IsNullOrWhiteSpace(request.SourceLanguage) ||
            string.Equals(request.SourceLanguage, "auto", StringComparison.OrdinalIgnoreCase)
                ? null
                : LanguageCatalog.Normalize(request.SourceLanguage);
        var targetLanguage = LanguageCatalog.Normalize(request.TargetLanguage!);

        if (string.Equals(sourceLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase))
        {
            throw new TranslationValidationException(
                new Dictionary<string, string[]>
                {
                    ["targetLanguage"] =
                    [
                        "Target language must differ from the explicit source language."
                    ]
                });
        }

        return new ValidatedTranslationRequest(
            request.Text!.Trim(),
            targetLanguage,
            sourceLanguage);
    }
}

public sealed record ValidatedTranslationRequest(
    string Text,
    string TargetLanguage,
    string? SourceLanguage);
