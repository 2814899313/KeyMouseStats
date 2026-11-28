using Kms.App.Platform;

namespace Kms.Golden.Tests;

public sealed class DisplayLayoutTests
{
    [Fact]
    public void GetMonitors_ReturnsAtLeastOneValidRect()
    {
        GoldenHarness.EnsureAvalonia();
        IReadOnlyList<DisplayRect>? previous = DisplayLayout.Override;
        DisplayLayout.Override = null;
        try
        {
            IReadOnlyList<DisplayRect> monitors = DisplayLayout.GetMonitors();
            Assert.NotEmpty(monitors);
            Assert.All(monitors, monitor => Assert.True(monitor.IsValid));
        }
        finally
        {
            DisplayLayout.Override = previous;
        }
    }
}
