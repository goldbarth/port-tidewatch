namespace Tidewatch.Source.Configuration;

/// <summary>The reading sources the host can run. Exactly one is active per run.</summary>
public enum ReadingSourceKind
{
    /// <summary>The scripted surge simulator (demo).</summary>
    Simulator,

    /// <summary>The live WSV/PEGELONLINE Elbe feed (production-near).</summary>
    Pegelonline,
}

/// <summary>
/// Bound to the top-level <c>ReadingSource</c> config value. Selects which source the host
/// runs at startup, so the same build serves the scripted demo or the real feed without
/// recompiling. Validated at startup — a bad/empty value fails fast (see
/// <see cref="ReadingSourceOptionsValidator"/>), consistent with the threshold options.
/// </summary>
public sealed class ReadingSourceOptions
{
    public const string Key = "ReadingSource";

    /// <summary>The raw configured source name; parsed and validated at startup.</summary>
    public string? Active { get; set; }
}

/// <summary>Shared parse of the configured source name, used by both Program and the validator.</summary>
public static class ReadingSourceParser
{
    /// <summary>The accepted values, for error messages.</summary>
    public const string ValidValues = "Simulator | Pegelonline";

    public static bool TryParse(string? value, out ReadingSourceKind kind)
        // Enum.TryParse also accepts numeric strings ("1" -> Pegelonline); reject those so
        // only the named sources are valid.
        => Enum.TryParse(value, ignoreCase: true, out kind)
           && Enum.IsDefined(kind)
           && !int.TryParse(value, out _);
}
