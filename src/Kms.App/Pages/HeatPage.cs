using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Kms.App.Apps;
using Kms.App.Layout;
using Kms.App.Widgets;
using Kms.Core.Model;
using Kms.Core.Time;

namespace Kms.App.Pages;

/// <summary>热区：全局范围点击格聚合、三通道筛选、九宫格与通道构成。</summary>
public sealed class HeatPage : UserControl
{
    public const string PageId = LayoutPresets.HeatmapPageId;

    private readonly StatsData _data;
    private readonly TimeScopeRange _scope;
    private readonly string _title;
    private readonly string _subtitle;
    private readonly AppPageContext _appContext;
    private readonly HeatWidgetModel _model;
    private readonly LayoutConfigService _layouts;
    private readonly Dictionary<string, WidgetPageModel> _models = new(StringComparer.Ordinal);
    private readonly BentoPageEditor _editor;
    private readonly ContentControl _headerHost = new();
    private readonly StackPanel _filters = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly Grid _root;
    private bool _compact;
    private bool _tall;

    public HeatPage(
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
        _title = title;
        _subtitle = subtitle;
        _model = new HeatWidgetModel(data, _scope, _appContext);
        _layouts = layouts ?? LayoutConfigService.Default;
        _editor = new BentoPageEditor(PageId, _layouts, CreateContext);

        _root = new Grid
        {
            Margin = new Thickness(20, 16, 20, 12),
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 10,
            ClipToBounds = true
        };
        _root.Children.Add(_headerHost);
        Grid.SetRow(_filters, 1);
        _root.Children.Add(_filters);

        Grid.SetRow(_editor, 2);
        _root.Children.Add(_editor);

        Content = _root;
        _headerHost.Content = BuildHeader();
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

    private Control BuildHeader()
    {
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (_model.Monitors.Count > 0)
        {
            string[] labels = new string[_model.Monitors.Count + 1];
            labels[0] = "跨屏全览";
            for (int i = 0; i < _model.Monitors.Count; i++)
            {
                labels[i + 1] = $"屏幕 {i + 1}";
            }

            actions.Children.Add(PageKit.Segmented(labels, _model.FocusedScreen + 1, index =>
            {
                _model.SetFocusedScreen(index - 1);
                _headerHost.Content = BuildHeader();
                RebuildBento();
                BuildFilters();
            }));
        }

        actions.Children.Add(_editor.CreateEntryButton());
        return PageKit.PageHeader("CLICK / HEATMAP", _title, _subtitle, _scope.Caption, actions);
    }

    private void RebuildBento()
    {
        _editor.Refresh();
    }

    private void BuildFilters()
    {
        _filters.Children.Clear();
        (string Label, int Channel)[] items =
        [
            ("全部", 0), ("左键", 1), ("右键", 2), ("中键", 3), ("侧键", 4)
        ];
        foreach ((string label, int channel) in items)
        {
            int selected = channel;
            _filters.Children.Add(PageKit.Filter(label, _model.Channel == channel, () =>
            {
                _model.SetChannel(selected);
                RebuildBento();
                BuildFilters();
            }, channel switch
            {
                1 => PageKit.T.Accent,
                2 => PageKit.T.Green,
                3 => PageKit.T.Orange,
                4 => PageKit.T.Purple,
                _ => PageKit.T.Cyan
            }));
        }

        _filters.Children.Add(new Border { Width = 8 });
        _filters.Children.Add(PageKit.Filter(_model.Cloud ? "平滑热力" : "像素网格", _model.Cloud, () =>
        {
            _model.ToggleCloud();
            RebuildBento();
            BuildFilters();
        }, PageKit.T.Purple));
        _filters.Children.Add(new Border { Width = 8 });
        _filters.Children.Add(PageKit.Muted(
            $"{_model.Matrix.Total:N0} 次点击 · {_model.Matrix.Cells:N0} 个采样格", 10));
    }

    /// <summary>供诊断/golden 选择单屏视图；-1 表示跨屏全览。</summary>
    internal void SelectScreen(int focusedScreen)
    {
        _model.SetFocusedScreen(focusedScreen);
        _headerHost.Content = BuildHeader();
        RebuildBento();
        BuildFilters();
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
