using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.App.Pages;
using TextBlock = Avalonia.Controls.TextBlock;

namespace Kms.App.Widgets;

/// <summary>
/// 3×1 / 3×2 细颗粒指标卡。内容只承载一个主数字、一个短标签和一条辅助说明，
/// 尺寸变化时自适应字号；不显示模块标题栏，避免 3×1 高度被标题吃掉。
/// </summary>
internal sealed class MicroKpiCard : UserControl
{
    private readonly TextBlock _label;
    private readonly TextBlock _value;
    private readonly TextBlock _hint;
    private readonly string _valueText;

    public MicroKpiCard(string label, string value, string hint, Color tone)
    {
        _valueText = value ?? "";
        _label = PageKit.Label(label, 9.5, PageKit.T.Muted, FontWeight.Medium);
        _label.TextTrimming = TextTrimming.CharacterEllipsis;
        _value = PageKit.Label(_valueText, 26, tone, FontWeight.SemiBold);
        _value.TextTrimming = TextTrimming.CharacterEllipsis;
        _hint = PageKit.Label(hint, 8.8, PageKit.Alpha(PageKit.T.Muted, 0.92), null, null,
            TextTrimming.CharacterEllipsis);

        Grid body = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                _label,
                _value,
                _hint
            }
        };
        Grid.SetRow(_value, 1);
        Grid.SetRow(_hint, 2);
        _value.VerticalAlignment = VerticalAlignment.Center;

        Content = body;
        SizeChanged += (_, _) => ApplyDensity();
        ApplyDensity();
    }

    private void ApplyDensity()
    {
        double height = Bounds.Height;
        bool tiny = height > 0 && height < 76;
        bool tall = height >= 150;
        _label.FontSize = tall ? 11 : 9.5;
        _value.FontSize = tiny ? 19 : tall ? 34 : 26;
        _hint.FontSize = tall ? 10 : 8.8;
        _hint.IsVisible = height <= 0 || height >= 70;
    }
}

internal static class MicroKpi
{
    public static WidgetView Card(string label, string value, string hint, Color tone) => new()
    {
        Content = new MicroKpiCard(label, value, hint, tone),
        ShowHeader = false,
        ContentPadding = new Thickness(12, 9),
        Elevated = true
    };

    public static string Percent(double fraction) =>
        double.IsFinite(fraction)
            ? (fraction * 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%"
            : "--";

    public static string DurationMs(double milliseconds) =>
        PageKit.FormatDuration(Math.Max(0, milliseconds) / 1000.0);
}
