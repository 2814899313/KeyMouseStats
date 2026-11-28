using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Kms.App.Apps;
using Kms.App.Layout;
using Kms.App.Widgets;
using Kms.Core.Model;
using Kms.Core.Text;
using Kms.Core.Time;

namespace Kms.App.Pages;

/// <summary>字符内容：全局范围内的 Rime 上屏统计、字/词组排行、隐私开关与来源说明。</summary>
public sealed class TextPage : UserControl
{
    public const string PageId = LayoutPresets.CharsPageId;

    private readonly string _title;
    private readonly string _subtitle;
    private readonly TimeScopeRange _scope;
    private readonly AppPageContext _appContext;
    private readonly CharsWidgetModel _model;
    private readonly LayoutConfigService _layouts;
    private readonly Dictionary<string, WidgetPageModel> _models = new(StringComparer.Ordinal);
    private readonly BentoPageEditor _editor;
    private readonly StatsData _data;
    private readonly ContentControl _headerHost = new();
    private readonly Button _toggle;
    private readonly Grid _root;
    private int _mode;
    private bool _compact;
    private bool _tall;

    public TextPage(
        string title,
        string subtitle,
        RimeLogStats? stats = null,
        TimeScopeRange? scope = null,
        LayoutConfigService? layouts = null,
        StatsData? data = null,
        AppPageContext? appContext = null)
    {
        _title = title;
        _subtitle = subtitle;
        _scope = scope ?? TimeScopeRange.Create(TimeScope.Today, DateTime.Now);
        _appContext = appContext ?? AppPageContext.All;
        RimeLogStats resolved = stats ?? RimeLog.Load();
        _model = new CharsWidgetModel(resolved, _scope, _appContext);
        _layouts = layouts ?? LayoutConfigService.Default;
        _data = data ?? new StatsData();
        _editor = new BentoPageEditor(PageId, _layouts, () => CreateContext());

        _toggle = new Button
        {
            Content = "显示明文",
            Padding = new Thickness(13, 6),
            CornerRadius = new CornerRadius(20),
            FontSize = 11,
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Accent, 0.12)),
            Foreground = PageKit.Brush(PageKit.T.Accent),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Accent, 0.4)),
            BorderThickness = new Thickness(1)
        };
        _toggle.Click += (_, _) => ToggleRevealed();

        _root = new Grid
        {
            Margin = new Thickness(20, 16, 20, 12),
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 10,
            ClipToBounds = true
        };
        _root.Children.Add(_headerHost);

        RebuildBento();
        Grid.SetRow(_editor, 1);
        _root.Children.Add(_editor);

        Content = _root;
        _headerHost.Content = BuildHeader();
        UpdateToggle();
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
        Rime = _model.Stats,
        Models = _models
    };

    private Control BuildHeader()
    {
        if (_toggle.Parent is Panel oldParent)
        {
            oldParent.Children.Remove(_toggle);
        }

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(PageKit.Segmented(["字词", "图表", "隐私"], _mode, mode =>
        {
            SelectMode(mode);
        }));
        actions.Children.Add(_toggle);
        actions.Children.Add(_editor.CreateEntryButton());
        return PageKit.PageHeader(
            "TEXT / PRIVATE BY DEFAULT",
            _title,
            _subtitle,
            _model.Stats.Missing ? "未找到 Rime 日志" : _scope.Caption,
            actions);
    }

    internal void SelectMode(int mode)
    {
        if (_editor.IsEditMode)
        {
            _editor.CancelEdit();
        }

        _mode = Math.Clamp(mode, 0, 2);
        RebuildBento();
        _headerHost.Content = BuildHeader();
    }

    internal void ToggleRevealed()
    {
        _model.Revealed = !_model.Revealed;
        UpdateToggle();
        _headerHost.Content = BuildHeader();
        if (_editor.IsEditMode && _mode == 0)
        {
            _editor.Refresh();
            return;
        }

        RebuildBento();
    }

    private void RebuildBento()
    {
        if (_mode != 0)
        {
            PageLayoutConfig layout = _mode == 1
                ? LayoutPresets.CreateCharsCharts()
                : LayoutPresets.CreateCharsPrivacy();
            _editor.ShowAlternateLayout(layout, CreateContext().WithRequestReload(RebuildBento));
            return;
        }

        _editor.ShowPageLayout();
    }

    private void UpdateToggle()
    {
        _toggle.Content = _model.Revealed ? "隐藏明文" : "显示明文";
        _toggle.Background = PageKit.Brush(PageKit.Alpha(
            _model.Revealed ? PageKit.T.Orange : PageKit.T.Accent, 0.12));
        _toggle.Foreground = PageKit.Brush(_model.Revealed ? PageKit.T.Orange : PageKit.T.Accent);
        _toggle.BorderBrush = PageKit.Brush(PageKit.Alpha(
            _model.Revealed ? PageKit.T.Orange : PageKit.T.Accent, 0.4));
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
