using Kms.Core.Stats;

namespace Kms.Core.Tests;

public sealed class AxisScaleTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Nice_NonPositiveFallsBackToStepOne(double rawMax)
    {
        AxisScale.Nice(rawMax, 4, out double top, out double step);

        Assert.Equal(1, step);
        Assert.Equal(4, top);
    }

    [Fact]
    public void Nice_AlignsToCleanFractions()
    {
        AxisScale.Nice(3610, 4, out double top, out double step);

        Assert.True(top >= 3610);
        Assert.Equal(0, top % step, 9);
        Assert.Contains(step, new[] { 500.0, 1000.0, 1500.0, 2000.0 });
    }

    [Fact]
    public void Nice_IntegerTicksNeverBelowOne()
    {
        AxisScale.Nice(3, 4, integerTicks: true, out _, out double step);

        Assert.True(step >= 1);
    }

    [Fact]
    public void Nice_AllowsFractionalStepForMeters()
    {
        AxisScale.Nice(3, 4, integerTicks: false, out _, out double step);

        Assert.True(step < 1);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1k")]
    [InlineData(1234, "1.23k")]
    [InlineData(1_500_000, "1.5M")]
    public void Label_MatchesV3(double value, string expected)
    {
        Assert.Equal(expected, AxisScale.Label(value));
    }
}
