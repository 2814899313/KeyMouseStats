namespace Kms.Input.Tests;

public sealed class InputMessagesTests
{
    [Theory]
    [InlineData(InputMessages.WM_LBUTTONDOWN, 0u, 1)]
    [InlineData(InputMessages.WM_LBUTTONUP, 0u, 1)]
    [InlineData(InputMessages.WM_RBUTTONDOWN, 0u, 2)]
    [InlineData(InputMessages.WM_MBUTTONDOWN, 0u, 3)]
    [InlineData(InputMessages.WM_XBUTTONDOWN, 0x0001_0000u, 4)]
    [InlineData(InputMessages.WM_XBUTTONDOWN, 0x0002_0000u, 5)]
    [InlineData(InputMessages.WM_XBUTTONDOWN, 0x0003_0000u, 0)]
    [InlineData(InputMessages.WM_MOUSEMOVE, 0u, 0)]
    public void MouseButtonId_MatchesV3(int message, uint mouseData, int expected)
    {
        Assert.Equal(expected, InputMessages.MouseButtonId(message, mouseData));
    }
}
