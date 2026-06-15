using Microsoft.Extensions.Options;

namespace Tidewatch.Ingestion.Configuration;

/// <summary>
/// Validates the threshold configuration once at startup. A bad configuration must
/// fail at startup, not at the first surge.
/// </summary>
public sealed class SurgeThresholdOptionsValidator : IValidateOptions<SurgeThresholdOptions>
{
    public ValidateOptionsResult Validate(string? name, SurgeThresholdOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Reference))
            errors.Add("Reference must be set.");

        if (options.TrendWindow <= TimeSpan.Zero)
            errors.Add("TrendWindow must be positive.");

        if (options.Stages.Count == 0)
        {
            errors.Add("At least one stage is required.");
            return Result(errors);
        }

        for (var i = 1; i < options.Stages.Count; i++)
        {
            if (options.Stages[i].MinMeters <= options.Stages[i - 1].MinMeters)
                errors.Add(
                    $"Stages must be strictly ascending by MinMeters: '{options.Stages[i].Name}' " +
                    $"({options.Stages[i].MinMeters}) is not greater than " +
                    $"'{options.Stages[i - 1].Name}' ({options.Stages[i - 1].MinMeters}).");
        }

        var normal = options.Stages.FirstOrDefault(s =>
            string.Equals(s.Name, "normal", StringComparison.OrdinalIgnoreCase));
        if (normal is null)
            errors.Add("A 'normal' stage is required.");
        else if (normal.MinMeters != 0)
            errors.Add("The 'normal' stage must start at 0 metres.");

        return Result(errors);
    }

    private static ValidateOptionsResult Result(List<string> errors) =>
        errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
}
