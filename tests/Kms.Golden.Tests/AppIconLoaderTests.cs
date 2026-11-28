using Avalonia.Media.Imaging;
using Kms.App.Platform;

namespace Kms.Golden.Tests;

public sealed class AppIconLoaderTests
{
    [Fact]
    public void SystemExecutableIcon_IsExtractedAndCached()
    {
        GoldenHarness.EnsureAvalonia();
        string? executable = new[] { "cmd.exe", "notepad.exe", "explorer.exe" }
            .Select(name => Path.Combine(Environment.SystemDirectory, name))
            .FirstOrDefault(File.Exists);
        Assert.NotNull(executable);

        bool previous = AppIconLoader.Enabled;
        AppIconLoader.Enabled = true;
        try
        {
            Bitmap? icon = AppIconLoader.Get(executable);
            Assert.NotNull(icon);
            Assert.True(icon!.Size.Width >= 16);
            Assert.True(icon.Size.Height >= 16);
            Assert.Same(icon, AppIconLoader.Get(executable));
        }
        finally
        {
            AppIconLoader.Enabled = previous;
            AppIconLoader.Clear();
        }
    }

    [Fact]
    public void MissingPath_ReturnsNullWithoutThrowing()
    {
        GoldenHarness.EnsureAvalonia();
        bool previous = AppIconLoader.Enabled;
        AppIconLoader.Enabled = true;
        try
        {
            string missing = Path.Combine(Path.GetTempPath(), $"kms-missing-{Guid.NewGuid():N}.exe");
            Assert.Null(AppIconLoader.Get(missing));
        }
        finally
        {
            AppIconLoader.Enabled = previous;
            AppIconLoader.Clear();
        }
    }
}
