using Tidewatch.Source.Configuration;

namespace Tidewatch.Source.UnitTests;

public class ReadingSourceOptionsValidatorTests
{
    private readonly ReadingSourceOptionsValidator _validator = new();

    [Theory]
    [InlineData("Simulator")]
    [InlineData("Pegelonline")]
    [InlineData("pegelonline")]   // case-insensitive
    [InlineData("SIMULATOR")]
    public void Validate_KnownSource_Succeeds(string active)
    {
        var result = _validator.Validate(name: null, new ReadingSourceOptions { Active = active });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Sim")]
    [InlineData("RealFeed")]
    [InlineData("1")]
    public void Validate_MissingOrUnknownSource_Fails(string? active)
    {
        var result = _validator.Validate(name: null, new ReadingSourceOptions { Active = active });

        Assert.True(result.Failed);
        Assert.Contains(ReadingSourceParser.ValidValues, result.FailureMessage);
    }
}
