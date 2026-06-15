using Microsoft.Extensions.Options;

namespace Tidewatch.Source.Configuration;

/// <summary>
/// Validates the configured reading source once at startup. A missing or unrecognised
/// <c>ReadingSource</c> must fail at startup, not silently run nothing — the same fail-fast
/// posture as the threshold options.
/// </summary>
public sealed class ReadingSourceOptionsValidator : IValidateOptions<ReadingSourceOptions>
{
    public ValidateOptionsResult Validate(string? name, ReadingSourceOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Active))
            return ValidateOptionsResult.Fail(
                $"ReadingSource must be set ({ReadingSourceParser.ValidValues}).");

        if (!ReadingSourceParser.TryParse(options.Active, out _))
            return ValidateOptionsResult.Fail(
                $"ReadingSource '{options.Active}' is not a valid source ({ReadingSourceParser.ValidValues}).");

        return ValidateOptionsResult.Success;
    }
}
