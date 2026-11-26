using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Kms.Render.Theming;
using Kms.Render.Tokens;

namespace Kms.Golden.Tests;

public sealed class ThemeResourcesTests
{
    [Fact]
    public void Create_ExportsAllTokenKeys()
    {
        ResourceDictionary resources = ThemeResources.Create(ThemeCatalog.Get(0));

        Assert.IsType<SolidColorBrush>(resources[ThemeResources.Background]);
        Assert.IsType<SolidColorBrush>(resources[ThemeResources.Accent]);
        Assert.IsType<Color>(resources[ThemeResources.AccentColor]);
        Assert.IsType<CornerRadius>(resources[ThemeResources.Radius]);
        Assert.Equal(18, resources.Count);
    }

    [Fact]
    public void Apply_UpdatesColorsAndRadiusWithoutChangingKeyCount()
    {
        ResourceDictionary resources = ThemeResources.Create(ThemeCatalog.Get(0));
        int count = resources.Count;

        ThemeResources.Apply(resources, ThemeCatalog.Get(5));

        SolidColorBrush background = Assert.IsType<SolidColorBrush>(resources[ThemeResources.Background]);
        SolidColorBrush accent = Assert.IsType<SolidColorBrush>(resources[ThemeResources.Accent]);
        CornerRadius radius = Assert.IsType<CornerRadius>(resources[ThemeResources.Radius]);

        Assert.Equal(ThemeCatalog.Get(5).Background, background.Color);
        Assert.Equal(ThemeCatalog.Get(5).Accent, accent.Color);
        Assert.Equal(18, radius.TopLeft);
        Assert.Equal(count, resources.Count);
    }

    [Fact]
    public void ApplyThirteenTimes_IsStable()
    {
        ResourceDictionary resources = ThemeResources.Create(ThemeCatalog.Get(0));

        for (int i = 0; i < 13; i++)
        {
            ThemeResources.Apply(resources, ThemeCatalog.Get(i));
        }

        ThemeResources.Apply(resources, ThemeCatalog.Get(12));

        SolidColorBrush background = Assert.IsType<SolidColorBrush>(resources[ThemeResources.Background]);
        Assert.Equal(ThemeCatalog.Get(12).Background, background.Color);
        Assert.Equal(18, resources.Count);
    }
}
