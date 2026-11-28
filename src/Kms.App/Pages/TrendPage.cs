using Avalonia;
using Avalonia.Controls;
using Kms.App.Apps;
using Kms.App.Layout;
using Kms.App.Widgets;
using Kms.Core.Model;
using Kms.Core.Time;

namespace Kms.App.Pages;

/// <summary>
/// 趋势对比：全局今日 → 24 小时逐时曲线；本周/本月 → 对应天数日级曲线。
/// 4.1.0 起由 BentoGridPanel + LayoutConfigService 声明式驱动，指标切换保留在图表模块内。
/// </summary>
public sealed class TrendPage : UserControl
{
    public const string PageId = LayoutPresets.TrendsPageId;

    private readonly StatsData _data;
    private readonly string _title;
    private readonly string _subtitle;
    private readonly TimeScopeRange _scope;
    private readonly AppPageContext _appContext;
    private readonly TrendsWidgetModel _model;
    private readonly LayoutConfigService _layouts;
    private readonly Dictionary<string, WidgetPageModel> _models = new(StringComparer.Ordinal);
    private readonly BentoPageEditor _editor;
    private readonly Grid _root;
    private bool _compact;
    private bool _tall;

    public TrendPage(
        StatsData data,
        string title,
        string subtitle,
        TimeScopeRange? scope = null,
        LayoutConfigService? layouts = null,
        AppPageContext? appContext = null)
    {
        _data = data;
        _title = title;
        _subtitle = subtitle;
        _scope = scope ?? TimeScopeRange.Create(TimeScope.Today, DateTime.Now);
        _appContext = appContext ?? AppPageContext.All;
        _model = new TrendsWidgetModel(data, _scope, _appContext);
        _layouts = layouts ?? LayoutConfigService.Default;
        _editor = new BentoPageEditor(PageId, _layouts, CreateContext);

        _root = new Grid
        {
            Margin = new Thickness(20, 16, 20, 12),
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10,
            ClipToBounds = true
        };
        _root.Children.Add(PageKit.PageHeader(
            _model.Hourly ? "RANGE / HOURLY" : "RANGE / COMPARE",
            title,
            _model.Hourly ? subtitle + " · 今日逐时视图" : subtitle,
            _scope.Caption,
            _editor.CreateEntryButton()));

        Grid.SetRow(_editor, 1);
        _root.Children.Add(_editor);

        Content = _root;
        SizeChanged += (_, _) => ApplyDensity(Bounds.Height);
    }

    private WidgetContext CreateContext() => new()
    {
        Data = _data,
        Scope = _scope,
        AppFilter = _appContext.Filter,
        Capabilities = _appContext.Capabilities,
        Profile = _appContext.Profile,
        AppPage = _appContext,
        VariantId = "",
        Model = _model,
        Models = _models
    };

    private void ApplyDensity(double height)
    {
        bool compact = PageKit.IsCompact(height);
        bool tall = PageKit.IsTall(height);
        if (_compact == compact && _tall == tall)
        {
            return;
        }

        _compact = compact;
        _tall = tall;
        _root.Margin = compact
            ? new Thickness(16, 8, 16, 4)
            : tall
                ? new Thickness(26, 20, 26, 16)
                : new Thickness(20, 16, 20, 12);
        _root.RowSpacing = compact ? 5 : tall ? 14 : 10;
        _editor.Spacing = compact ? 6 : tall ? 14 : 12;
    }
}
