using Tidewatch.Source.Pegelonline;

namespace Tidewatch.Source.UnitTests;

public class PegelMapperTests
{
    // A known St. Pauli reading: 387 cm above PNP, Hamburg PNP at NHN -5.00 m.
    // 387/100 + (-5.00) = -1.130 m NHN. Pins the cm -> m and PNP -> NHN conversion
    // end to end, the way it is applied to the live feed.
    [Fact]
    public void ToNhnMeters_KnownStPauliReading_ConvertsToNhn()
    {
        var nhn = PegelMapper.ToNhnMeters(valueCm: 387m, pnpOffsetMeters: -5.00m);

        Assert.Equal(-1.130m, nhn);
    }

    [Theory]
    [InlineData(707, -5.00, 2.070)]   // ~MThw at St. Pauli
    [InlineData(0, -5.00, -5.000)]    // gauge zero itself
    [InlineData(1050, -5.00, 5.500)]  // severe-stage level (5.50 m NHN)
    [InlineData(550, 0.00, 5.500)]    // PNP already at NHN: pure cm -> m
    public void ToNhnMeters_AppliesConversionAndOffset(double valueCm, double pnpOffset, double expected)
    {
        var nhn = PegelMapper.ToNhnMeters((decimal)valueCm, (decimal)pnpOffset);

        Assert.Equal((decimal)expected, nhn);
    }

    [Fact]
    public void ToNhnMeters_RoundsToThreeDecimals()
    {
        // 387.6 cm -> 3.876 m + (-5.00) = -1.124 m; an extra digit is rounded away.
        var nhn = PegelMapper.ToNhnMeters(valueCm: 387.64m, pnpOffsetMeters: -5.00m);

        Assert.Equal(-1.124m, nhn);
    }
}
