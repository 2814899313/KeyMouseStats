using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Kms.App.Apps;
using Kms.App.Layout;
using Kms.App.Widgets;
using Kms.Core.Model;
using Kms.Core.Time;

namespace Kms.App.Pages;

/// <summary>按键分析：全局范围排行、按住分布、组合键、键位构成与键盘热力。</summary>
public sealed class KeysPage : UserControl
{
    public const string PageId = LayoutPresets.KeysPageId;

    private readonly StatsData _data;
    private readonly string _title;
    private readonly string _subtitle;
    private readonly TimeScopeRange _scope;
    private readonly AppPageContext _appContext;
    private readonly KeysWidgetModel _model;
    private readonly LayoutConfigService _layouts;
    private readonly Dictionary<string, WidgetPageModel> _models = new(StringComparer.Ordinal);
    private readonly BentoPageEditor _editor;
    private readonly ContentControl _headerHost = new();
    private readonly Grid _root;
    private int _mode;
    private int _view;
    private bool _compact;
    private bool _tall;

    public KeysPage(
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
        _model = new KeysWidgetModel(data, _scope, _appContext);
        _layouts = layouts ?? LayoutConfigService.Default;
        _editor = new BentoPageEditor(PageId, _layouts, () => CreateContext());

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
        actions.Children.Add(PageKit.Segmented(["统计", "外设健康"], _mode, mode =>
        {
            SelectMode(mode);
        }));
        if (_mode == 0)
        {
            actions.Children.Add(PageKit.Segmented(["按键排行", "组合键", "全键热力"], _view, view =>
            {
                SelectView(view);
            }));
        }

        actions.Children.Add(PageKit.Badge(_mode == 1 ? "全生命周期物理累计" : _scope.Caption, PageKit.T.Accent));
        actions.Children.Add(_editor.CreateEntryButton());
        return PageKit.PageHeader("INPUT / KEYBOARD", _title, _subtitle, null, actions);
    }

    internal void SelectMode(int mode)
    {
        if (_editor.IsEditMode)
        {
            _editor.CancelEdit();
        }

        _mode = Math.Clamp(mode, 0, 1);
        RebuildBento();
        _headerHost.Content = BuildHeader();
    }

    internal void SelectView(int view)
    {
        if (_editor.IsEditMode)
        {
            _editor.CancelEdit();
        }

        _view = Math.Clamp(view, 0, 2);
        _mode = 0;
        RebuildBento();
        _headerHost.Content = BuildHeader();
    }

    private void RebuildBento()
    {
        if (_mode == 1 || _view != 0)
        {
            PageLayoutConfig layout = _mode == 1
                ? LayoutPresets.CreateKeysHealth()
                : _view == 1
                    ? LayoutPresets.CreateKeysCombos()
                    : LayoutPresets.CreateKeysHeat();
            _editor.ShowAlternateLayout(layout, CreateContext().WithRequestReload(RebuildBento));
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
            ? new Thickness(16, 8, 16, 4)
            : tall
                ? new Thickness(26, 20, 26, 16)
                : new Thickness(20, 16, 20, 12);
        _root.RowSpacing = compact ? 5 : tall ? 14 : 10;
        _editor.Spacing = compact ? 6 : tall ? 14 : 12;
    }
}
