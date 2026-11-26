using Kms.Render.Motion;

namespace Kms.Golden.Tests;

/// <summary>120 → 60 → 30 的自适应降级与 10 秒探测升级。</summary>
public sealed class FrameBudgetTests
{
    [Fact]
    public void StartsAt120()
    {
        FrameBudget budget = new();
        Assert.Equal(120, budget.TargetFps);
        Assert.Equal(1000.0 / 120, budget.Interval.TotalMilliseconds, 3);
    }

    [Fact]
    public void SustainedSlowFrames_DowngradeTo60()
    {
        FrameBudget budget = new();
        for (int i = 0; i < 44; i++)
        {
            budget.Sample(TimeSpan.FromMilliseconds(16));
        }

        Assert.Equal(120, budget.TargetFps);
        Assert.True(budget.Sample(TimeSpan.FromMilliseconds(16)));
        Assert.Equal(60, budget.TargetFps);
    }

    [Fact]
    public void SustainedVerySlowFrames_DowngradeDirectlyTo30()
    {
        FrameBudget budget = new();
        bool changed = false;
        for (int i = 0; i < 20; i++)
        {
            changed |= budget.Sample(TimeSpan.FromMilliseconds(40));
        }

        Assert.True(changed);
        Assert.Equal(30, budget.TargetFps);
    }

    [Fact]
    public void Probe_UpgradesAfterTenSecondsAt30()
    {
        FrameBudget budget = new();
        for (int i = 0; i < 20; i++)
        {
            budget.Sample(TimeSpan.FromMilliseconds(40));
        }

        Assert.Equal(30, budget.TargetFps);
        bool changed = false;
        for (int i = 0; i < 299; i++)
        {
            changed |= budget.Sample(TimeSpan.FromMilliseconds(40));
        }

        Assert.False(changed);
        Assert.True(budget.Sample(TimeSpan.FromMilliseconds(40)));
        Assert.Equal(60, budget.TargetFps);
    }

    [Fact]
    public void FastFrames_StayAt120()
    {
        FrameBudget budget = new();
        for (int i = 0; i < 5000; i++)
        {
            Assert.False(budget.Sample(TimeSpan.FromMilliseconds(8)));
        }

        Assert.Equal(120, budget.TargetFps);
    }

    [Fact]
    public void InvalidSamplesAreIgnored()
    {
        FrameBudget budget = new();
        Assert.False(budget.Sample(TimeSpan.Zero));
        Assert.False(budget.Sample(TimeSpan.FromMilliseconds(-5)));
        Assert.False(budget.Sample(TimeSpan.FromMilliseconds(900)));
        Assert.Equal(120, budget.TargetFps);
    }
}
