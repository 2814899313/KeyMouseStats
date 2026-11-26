using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Kms.Render.Tokens;

namespace Kms.Render.Theming;

/// <summary>
/// 把 <see cref="ThemeTokens"/> 投影成 Avalonia 资源字典。
/// 键名固定为 <c>Kms.*</c>，页面通过 DynamicResource 绑定，切主题不重建控件树。
/// </summary>
public static class ThemeResources
{
    public const string Background = "Kms.Background";
    public const string Card = "Kms.Card";
    public const string Raised = "Kms.Raised";
    public const string Line = "Kms.Line";
    public const string Text = "Kms.Text";
    public const string Muted = "Kms.Muted";
    public const string Accent = "Kms.Accent";
    public const string OnAccent = "Kms.OnAccent";
    public const string AccentColor = "Kms.AccentColor";
    public const string CyanColor = "Kms.CyanColor";
    public const string PurpleColor = "Kms.PurpleColor";
    public const string Green = "Kms.Green";
    public const string Orange = "Kms.Orange";
    public const string Purple = "Kms.Purple";
    public const string Red = "Kms.Red";
    public const string Cyan = "Kms.Cyan";
    public const string Sidebar = "Kms.Sidebar";
    public const string Radius = "Kms.Radius";

    public static ResourceDictionary Create(ThemeTokens theme)
    {
        ResourceDictionary dictionary = new();
        Apply(dictionary, theme);
        return dictionary;
    }

    public static void Apply(IResourceDictionary target, ThemeTokens theme)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(theme);

        target[Background] = Brush(theme.Background);
        target[Card] = Brush(theme.Card);
        target[Raised] = Brush(theme.Raised);
        target[Line] = Brush(theme.Line);
        target[Text] = Brush(theme.Text);
        target[Muted] = Brush(theme.Muted);
        target[Accent] = Brush(theme.Accent);
        target[OnAccent] = Brush(theme.OnAccent);
        target[AccentColor] = theme.Accent;
        target[CyanColor] = theme.Cyan;
        target[PurpleColor] = theme.Purple;
        target[Green] = Brush(theme.Green);
        target[Orange] = Brush(theme.Orange);
        target[Purple] = Brush(theme.Purple);
        target[Red] = Brush(theme.Red);
        target[Cyan] = Brush(theme.Cyan);
        target[Sidebar] = Brush(theme.Sidebar);
        target[Radius] = new CornerRadius(theme.Radius);
    }

    private static IBrush Brush(Color color) => new SolidColorBrush(color);
}

/// <summary>当前主题与切换入口。只做资源字典替换，不触碰控件树。</summary>
public static class ThemeManager
{
    public static ThemeTokens Current { get; private set; } = ThemeCatalog.Get(0);

    public static event Action<ThemeTokens>? Changed;

    public static void Apply(Application application, int themeId)
    {
        ArgumentNullException.ThrowIfNull(application);
        ThemeTokens theme = ThemeCatalog.Get(themeId);
        ThemeResources.Apply(application.Resources, theme);
        Current = theme;
        Changed?.Invoke(theme);
    }

    /// <summary>应用一套运行时生成的主题（自定义壁纸调色板）。</summary>
    public static void ApplyTokens(Application application, ThemeTokens theme)
    {
        ArgumentNullException.ThrowIfNull(application);
        ThemeResources.Apply(application.Resources, theme);
        Current = theme;
        Changed?.Invoke(theme);
    }
}
