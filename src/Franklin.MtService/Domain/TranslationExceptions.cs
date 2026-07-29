namespace Franklin.MtService.Domain;

public sealed class TranslationValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("The translation request is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class ProviderUnavailableException(
    string message,
    Exception? innerException = null)
    : Exception(message, innerException);

public sealed class TransientProviderException(
    string message,
    Exception? innerException = null)
    : Exception(message, innerException);
