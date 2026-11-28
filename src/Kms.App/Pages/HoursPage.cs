using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Kms.App.Apps;
using Kms.App.Layout;
using Kms.App.Widgets;
using Kms.Core.Model;
using Kms.Core.Time;

namespace Kms.App.Pages;

/// <summary>小时分布：全局范围内的 24 小时聚合柱、可点选小时详情与活动带。</summary>
public sealed class HoursPage : UserControl
{
    public const string PageId = LayoutPresets.HourlyPageId;

    private readonly StatsData _data;
    private readonly TimeScopeRange _scope;
    private readonly AppPageContext _appContext;
    private readonly HourlyWidgetModel _model;
    private readonly LayoutConfigService _layouts;
    private readonly Dictionary<string, WidgetPageModel> _models = new(StringComparer.Ordinal);
    private readonly BentoPageEditor _editor;
    private readonly StackPanel _filters = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly Grid _root;
    private bool _compact;
    private bool _tall;

    public HoursPage(
        StatsData data,
        string title,
        string subtitle,
        TimeScopeRange? scope = null,
        LayoutConfigService? layouts = null,
        AppPageContext? appContext = null)
    {
        _data = data;
        _scope = scope ?? TimeScopeRange.Create(TimeScope.Today, DateTime.Now);
        _appContext = appContext ?? AppPageContext.All;
        _model = new HourlyWidgetModel(data, _scope, _appContext);
        _layouts = layouts ?? LayoutConfigService.Default;
        _editor = new BentoPageEditor(PageId, _layouts, CreateContext);

        _root = new Grid
        {
            Margin = new Thickness(20, 16, 20, 12),
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 10,
            ClipToBounds = true
        };
        _root.Children.Add(PageKit.PageHeader(
            "DAY / 24 HOURS",
            title,
            subtitle,
            _scope.Caption,
            _editor.CreateEntryButton()));

        Grid.SetRow(_filters, 1);
        _root.Children.Add(_filters);

        Grid.SetRow(_editor, 2);
        _root.Children.Add(_editor);

        Content = _root;
        BuildFilters();
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

    private void BuildFilters()
    {
        _filters.Children.Clear();
        string[] labels = ["击键", "点击", "活跃分钟"];
        for (int i = 0; i < labels.Length; i++)
        {
            int selected = i;
            _filters.Children.Add(PageKit.Filter(labels[i], _model.Metric == i, () =>
            {
                _model.Metric = selected;
                _model.SelectedHour = -1;
                _editor.Refresh();
                BuildFilters();
            }, i switch { 1 => PageKit.T.Green, 2 => PageKit.T.Purple, _ => PageKit.T.Accent }));
        }

        _filters.Children.Add(new Border { Width = 8 });
        _filters.Children.Add(PageKit.Muted(_scope.Caption, 10));
    }

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
