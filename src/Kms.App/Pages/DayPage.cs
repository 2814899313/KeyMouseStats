using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Kms.App.Apps;
using Kms.App.Layout;
using Kms.App.Widgets;
using Kms.Core.Model;
using Kms.Core.Time;

namespace Kms.App.Pages;

/// <summary>
/// 日报页：全局今日 → 单日日报（保留前/后翻日）；
/// 本周/本月 → 周报/月报汇总，覆盖范围由全局时间服务决定。
/// 4.1.0 起由 BentoGridPanel + LayoutConfigService 声明式驱动。
/// </summary>
public sealed class DayPage : UserControl
{
    public const string PageId = LayoutPresets.DailyPageId;

    private readonly StatsData _data;
    private readonly TimeScopeRange _scope;
    private readonly AppPageContext _appContext;
    private readonly string _title;
    private readonly string _subtitle;
    private readonly Action<DateTime>? _onDate;
    private readonly DailyWidgetModel _model;
    private readonly LayoutConfigService _layouts;
    private readonly Dictionary<string, WidgetPageModel> _models = new(StringComparer.Ordinal);
    private readonly BentoPageEditor _editor;
    private readonly DateTime _selectedDate;
    private readonly DateTime _minDate;
    private readonly DateTime _maxDate;
    private readonly Grid _root;
    private bool _compact;
    private bool _tall;

    public DayPage(
        StatsData data,
        string title,
        string subtitle,
        TimeScopeRange? scope = null,
        DateTime? selected = null,
        Action<DateTime>? onDate = null,
        LayoutConfigService? layouts = null,
        AppPageContext? appContext = null)
    {
        _data = data;
        _scope = scope ?? TimeScopeRange.Create(TimeScope.Today, DateTime.Now);
        _appContext = appContext ?? AppPageContext.All;
        _title = title;
        _subtitle = subtitle;
        _onDate = onDate;
        _selectedDate = (selected ?? DailyWidgetModel.ResolveDate(data)).Date;
        _minDate = data.History.Count == 0 ? _selectedDate : data.History.Values.Min(day => day.Date);
        _maxDate = data.History.Count == 0 ? _selectedDate : data.History.Values.Max(day => day.Date);
        _model = new DailyWidgetModel(data, _scope, _selectedDate, _appContext);
        _layouts = layouts ?? LayoutConfigService.Default;
        _editor = new BentoPageEditor(PageId, _layouts, () => CreateContext());

        _root = new Grid
        {
            Margin = new Thickness(20, 16, 20, 12),
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10,
            ClipToBounds = true
        };
        _root.Children.Add(BuildHeader());

        RebuildBento();
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

    private Control BuildHeader()
    {
        if (_model.PeriodMode)
        {
            string periodTitle = _scope.Scope == TimeScope.ThisWeek ? "周报" : "月报";
            return PageKit.PageHeader(
                _scope.Scope == TimeScope.ThisWeek ? "WEEK / REPORT" : "MONTH / REPORT",
                periodTitle,
                _scope.Caption + " · 自动汇总",
                null,
                PageKit.Badge(_scope.RangeText, PageKit.T.Cyan));
        }

        Button export = new()
        {
            Content = "导出日报海报  ↗",
            Padding = new Thickness(14, 7),
            CornerRadius = new CornerRadius(20),
            FontSize = 11,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Background = PageKit.Gradient(PageKit.T.Accent, PageKit.T.Cyan, 0),
            Foreground = PageKit.Brush(PageKit.T.OnAccent)
        };
        export.Click += async (_, _) =>
        {
            string name = $"键鼠统计日报-{_selectedDate:yyyyMMdd}.png";
            await Kms.App.Reports.PosterExporter.SavePngAsync(
                new Kms.App.Reports.DailyPosterSurface(_model.Day, PageKit.T),
                Kms.App.Reports.DailyPosterSurface.PosterWidth,
                Kms.App.Reports.DailyPosterSurface.PosterHeight,
                3,
                name,
                TopLevel.GetTopLevel(this));
        };

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                PageKit.DateNav(_selectedDate, _minDate, _maxDate, _onDate ?? (_ => { })),
                export,
                _editor.CreateEntryButton()
            }
        };

        return PageKit.PageHeader(
            "DAY / REPORT",
            _title,
            _model.Day is null ? _subtitle : $"{_subtitle} · {DailyWidgetModel.Weekday(_selectedDate)}",
            _scope.Caption,
            actions);
    }

    private void RebuildBento()
    {
        if (_model.PeriodMode)
        {
            _editor.ShowAlternateLayout(
                LayoutPresets.CreateDailyPeriod(),
                CreateContext().WithRequestReload(RebuildBento));
            return;
        }

        _editor.ShowPageLayout();
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
            ? new Thickness(16, 10, 16, 6)
            : tall
                ? new Thickness(26, 20, 26, 16)
                : new Thickness(20, 16, 20, 12);
        _root.RowSpacing = compact ? 6 : tall ? 14 : 10;
        _editor.Spacing = compact ? 6 : tall ? 14 : 12;
    }
}
