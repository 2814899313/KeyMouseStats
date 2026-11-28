using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Kms.App.Pages;
using Kms.App.Widgets;

namespace Kms.App.Layout;

/// <summary>
/// Step 4 可视化布局工作台：锁定态只渲染栅格，编辑态提供工具条、模块抽屉、
/// 标题栏拖动幽灵预览、尺寸循环、移除、场景预设以及保存/取消回滚。
/// 所有结构变更先经过 <see cref="BentoLayoutMath"/>，再统一走
/// <see cref="LayoutValidation"/>，确保 12×6 零重叠且不产生外层滚动。
/// </summary>
public sealed class BentoPageEditor : UserControl
{
    private readonly string _pageId;
    private readonly PageId _ownerPage;
    private readonly LayoutConfigService _layouts;
    private readonly Func<WidgetContext> _contextFactory;
    private readonly Grid _surface = new();
    private readonly BentoGridPanel _grid;
    private readonly Grid _overlay = new();
    private readonly Canvas _ghostLayer = new();
    private readonly Border _ghost;
    private readonly Border _toolbar;
    private readonly Button _addButton;
    private readonly Button _presetButton;
    private readonly Button _resetButton;
    private readonly Button _cancelButton;
    private readonly Button _saveButton;
    private readonly Button _previousCategoryButton;
    private readonly Button _nextCategoryButton;
    private readonly TextBlock _catalogPageText;
    private readonly Flyout _presetFlyout;
    private readonly StackPanel _presetItems = new() { Spacing = 3 };
    private readonly Border _drawer;
    private readonly ContentControl _drawerHost = new();
    private readonly StackPanel _drawerPanel = new() { Spacing = 8 };
    private readonly StackPanel _catalogBody = new() { Spacing = 4 };
    private readonly TextBlock _catalogSummary;
    private readonly TextBox _search;
    private readonly ComboBox _categoryFilter;
    private readonly Border _toast;
    private readonly TextBlock _toastText;
    private readonly DispatcherTimer _toastTimer;
    private readonly List<Button> _entryButtons = [];
    private readonly List<(string Label, string Id)> _categoryOptions;

    private IReadOnlyDictionary<string, BuiltWidget> _built = new Dictionary<string, BuiltWidget>(StringComparer.Ordinal);
    private List<WidgetPlacement> _working = [];
    private List<WidgetPlacement> _committed = [];
    private PageLayoutConfig? _alternateLayout;
    private WidgetContext? _alternateContext;
    private bool _isEditMode;
    private bool _canEdit = true;
    private bool _showingDrawer;
    private string _drawerCategory = "";
    private string _drawerQuery = "";
    private int _drawerPage;
    private string? _dragModuleId;
    private string? _catalogDragModuleId;
    private WidgetSpan _catalogDragSpan;
    private int _ghostCol = -1;
    private int _ghostRow = -1;

    public BentoPageEditor(
        string pageId,
        LayoutConfigService layouts,
        Func<WidgetContext> contextFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(contextFactory);

        _pageId = pageId;
        _ownerPage = PageIds.Parse(pageId);
        _layouts = layouts;
        _contextFactory = contextFactory;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        ClipToBounds = true;

        _grid = new BentoGridPanel { Spacing = 12 };
        _ghostLayer.IsHitTestVisible = false;
        _ghost = new Border
        {
            IsVisible = false,
            IsHitTestVisible = false,
            CornerRadius = new CornerRadius(8),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Accent, 0.16)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Accent, 0.72)),
            BorderThickness = new Thickness(1),
            Opacity = 0.9
        };
        _ghostLayer.Children.Add(_ghost);

        _toastText = PageKit.Label("", 10.5, PageKit.T.Text, FontWeight.Medium);
        _toast = new Border
        {
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(13, 7),
            CornerRadius = new CornerRadius(8),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Card, 0.97)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Accent, 0.5)),
            BorderThickness = new Thickness(1),
            BoxShadow = PageKit.Shadow(18, 5, 0.18),
            Child = _toastText
        };
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            _toast.IsVisible = false;
        };

        _addButton = ToolButton("＋  添加模块");
        _presetButton = ToolButton("套用预设  ▾");
        _resetButton = ToolButton("恢复默认");
        _cancelButton = ToolButton("取消");
        _saveButton = ToolButton("保存布局", primary: true);
        _presetFlyout = new Flyout
        {
            Content = new Border
            {
                MinWidth = 252,
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(10),
                Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Card, 0.98)),
                BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.9)),
                BorderThickness = new Thickness(1),
                Child = _presetItems
            }
        };

        _toolbar = BuildToolbar();

        _catalogSummary = PageKit.Muted("", 9);
        _catalogPageText = PageKit.Muted("1 / 1", 9);
        _previousCategoryButton = PagerButton("‹");
        _nextCategoryButton = PagerButton("›");
        _search = new TextBox
        {
            PlaceholderText = "搜索模块 / 分类",
            FontSize = 10.5,
            Height = 28,
            Padding = new Thickness(8, 0),
            CornerRadius = new CornerRadius(6),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, 0.7)),
            Foreground = PageKit.Brush(PageKit.T.Text),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.75)),
            BorderThickness = new Thickness(1)
        };
        _search.TextChanged += (_, _) =>
        {
            _drawerQuery = _search.Text ?? "";
            _drawerPage = 0;
            RefreshCatalog();
        };

        _categoryOptions = BuildCategoryOptions();
        _categoryFilter = new ComboBox
        {
            ItemsSource = _categoryOptions.Select(static option => option.Label).ToArray(),
            SelectedIndex = 0,
            FontSize = 10.5,
            Height = 28,
            Padding = new Thickness(8, 0, 2, 0),
            CornerRadius = new CornerRadius(6),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, 0.7)),
            Foreground = PageKit.Brush(PageKit.T.Text),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.75)),
            BorderThickness = new Thickness(1)
        };
        _categoryFilter.SelectionChanged += (_, _) =>
        {
            int index = Math.Clamp(_categoryFilter.SelectedIndex, 0, _categoryOptions.Count - 1);
            _drawerCategory = _categoryOptions[index].Id;
            _drawerPage = 0;
            RefreshCatalog();
        };

        _previousCategoryButton.Click += (_, _) =>
        {
            if (_drawerPage > 0)
            {
                _drawerPage--;
                RefreshCatalog();
            }
        };
        _nextCategoryButton.Click += (_, _) =>
        {
            if (_drawerPage < CatalogPageCount - 1)
            {
                _drawerPage++;
                RefreshCatalog();
            }
        };

        BuildDrawerPanel();
        _drawer = new Border
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Width = 312,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(PageKit.R(10, 14)),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Card, 0.98)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.9)),
            BorderThickness = new Thickness(1),
            BoxShadow = PageKit.Shadow(24, 8, 0.18),
            Child = _drawerHost
        };

        _overlay.Children.Add(_ghostLayer);
        _overlay.Children.Add(_toast);
        _overlay.Children.Add(_drawer);
        _surface.RowDefinitions = new RowDefinitions("Auto,*");
        _surface.Children.Add(_toolbar);
        _surface.Children.Add(_grid);
        _surface.Children.Add(_overlay);
        Grid.SetRow(_grid, 1);
        Grid.SetRow(_overlay, 1);
        Content = _surface;

        _addButton.Click += (_, _) => ToggleDrawer();
        _presetButton.Flyout = _presetFlyout;
        _resetButton.Click += (_, _) => RestoreDefault();
        _cancelButton.Click += (_, _) => CancelEdit();
        _saveButton.Click += async (_, _) => await SaveAsync();

        BuildPresetItems();
        ShowPageLayout();
    }

    /// <summary>当前是否处于编辑态。</summary>
    public bool IsEditMode => _isEditMode;

    /// <summary>当前页面是否允许进入编辑（次级视图 / 非默认布局时为 false）。</summary>
    public bool CanEdit => _canEdit;

    public BentoGridPanel PlacementGrid => _grid;

    public double Spacing
    {
        get => _grid.Spacing;
        set => _grid.Spacing = value;
    }

    public IReadOnlyDictionary<string, BuiltWidget> Built => _built;

    /// <summary>每次栅格重建后触发；页面可据此同步排行视图等动态引用。</summary>
    public event Action? Rebuilt;

    /// <summary>编辑态的修改副本；测试与保存流程都读取这里的完整快照。</summary>
    public IReadOnlyList<WidgetPlacement> WorkingPlacements =>
        _working.Select(static placement => placement.Clone()).ToArray();

    public IReadOnlyList<WidgetPlacement> CommittedPlacements =>
        _committed.Select(static placement => placement.Clone()).ToArray();

    /// <summary>标题栏右侧的克制入口按钮；编辑态或次级视图自动隐藏。</summary>
    public Button CreateEntryButton()
    {
        Button button = new()
        {
            Content = "自定义布局",
            FontSize = 10.5,
            Height = 27,
            Padding = new Thickness(11, 0),
            CornerRadius = new CornerRadius(8),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, 0.65)),
            Foreground = PageKit.Brush(PageKit.T.Muted),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.7)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(button, "拖动、缩放、添加本页模块");
        button.Click += (_, _) => EnterEditMode();
        _entryButtons.Add(button);
        button.IsVisible = _canEdit && !_isEditMode;
        return button;
    }

    /// <summary>进入编辑态：复制当前生效布局，工具条与卡片控制出现。</summary>
    public void EnterEditMode()
    {
        if (!_canEdit || _isEditMode)
        {
            return;
        }

        _committed = _layouts.GetPageLayout(_pageId).Placements
            .Select(static placement => placement.Clone())
            .ToList();
        _working = BentoLayoutMath.Clone(_committed);
        _isEditMode = true;
        CloseDrawer();
        UpdateEditingChrome();
        Rebuild();
        ShowToast("编辑中：拖动标题栏移动卡片，点击保存后写入 layouts.json。");
    }

    /// <summary>取消并恢复进入编辑前的生效布局。</summary>
    public void CancelEdit()
    {
        if (!_isEditMode)
        {
            return;
        }

        _isEditMode = false;
        _working = BentoLayoutMath.Clone(_committed);
        CloseDrawer();
        UpdateEditingChrome();
        Rebuild();
        ShowToast("已取消未保存的修改。");
    }

    /// <summary>保存当前工作布局并退出编辑态。</summary>
    public async Task SaveAsync()
    {
        if (!_isEditMode)
        {
            return;
        }

        _saveButton.IsEnabled = false;
        try
        {
            await _layouts.SavePageLayoutAsync(_pageId, _working).ConfigureAwait(true);
            _committed = BentoLayoutMath.Clone(_working);
            _isEditMode = false;
            CloseDrawer();
            UpdateEditingChrome();
            Rebuild();
            ShowToast("布局已保存。");
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or ArgumentException
                                              or NotSupportedException
                                              or System.Text.Json.JsonException)
        {
            ShowToast("保存失败：" + exception.Message);
        }
        finally
        {
            _saveButton.IsEnabled = true;
        }
    }

    /// <summary>恢复页面默认布局到工作副本；仍需保存才会持久化。</summary>
    public void RestoreDefault()
    {
        if (!_isEditMode)
        {
            return;
        }

        PageLayoutConfig fallback = LayoutConfigService.GetDefault(_pageId);
        if (!LayoutValidation.TryNormalize(fallback, out PageLayoutConfig normalized, out string? error))
        {
            ShowToast(error ?? "默认布局不合法。");
            return;
        }

        _working = BentoLayoutMath.Clone(normalized.Placements);
        Rebuild();
        if (_showingDrawer)
        {
            RefreshCatalog();
        }

        ShowToast("已载入官方默认布局，点击保存后生效。");
    }

    /// <summary>加载并显示当前生效的页面布局；可选恢复为可编辑状态。</summary>
    public void ShowPageLayout(bool editable = true)
    {
        _canEdit = editable;
        _alternateLayout = null;
        _alternateContext = null;
        _committed = _layouts.GetPageLayout(_pageId).Placements
            .Select(static placement => placement.Clone())
            .ToList();
        _working = BentoLayoutMath.Clone(_committed);
        _isEditMode = false;
        CloseDrawer();
        UpdateEditingChrome();
        Rebuild();
    }

    /// <summary>
    /// 显示非默认/次级视图布局。次级视图不提供编辑器，避免把临时布局保存到页面主布局。
    /// </summary>
    public void ShowAlternateLayout(PageLayoutConfig layout, WidgetContext context)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(context);

        _canEdit = false;
        _isEditMode = false;
        _alternateLayout = layout.Clone();
        _alternateContext = context;
        context.Live?.Clear();
        CloseDrawer();
        UpdateEditingChrome();
        _built = BentoPageBuilder.Build(_grid, layout, context, editMode: false);
        Rebuilt?.Invoke();
    }

    /// <summary>用当前工作布局重建栅格，不重新读取 layouts.json。</summary>
    public void Refresh()
    {
        if (_alternateLayout is not null && _alternateContext is not null)
        {
            ShowAlternateLayout(_alternateLayout, _alternateContext);
            return;
        }

        if (_canEdit)
        {
            Rebuild();
            return;
        }

        ShowPageLayout(editable: false);
    }

    public bool TryAddModule(string moduleId)
    {
        if (!_isEditMode)
        {
            return false;
        }

        if (!WidgetRegistry.TryGetForPage(_ownerPage, moduleId, out IStatWidgetModule module))
        {
            ShowToast($"「{moduleId}」不属于{PageIds.Label(_ownerPage)}页的模块目录。");
            return false;
        }

        if (_working.Any(placement => string.Equals(placement.ModuleId, moduleId, StringComparison.Ordinal)))
        {
            ShowToast($"「{module.Descriptor.Title}」已在当前页面。");
            return false;
        }

        WidgetSpan span = module.Descriptor.DefaultSpan;
        if (!BentoLayoutMath.TryFindFirstFit(_working, span, out int col, out int row))
        {
            ShowToast("当前页面已满：请先移除或缩小现有卡片，再添加这个模块。");
            return false;
        }

        List<WidgetPlacement> updated = BentoLayoutMath.Clone(_working);
        updated.Add(new WidgetPlacement
        {
            ModuleId = moduleId,
            VariantId = module.Descriptor.SupportedVariants[0],
            Col = col,
            Row = row,
            ColSpan = span.Columns,
            RowSpan = span.Rows
        });
        return ApplyWorking(updated, $"已添加「{module.Descriptor.Title}」。");
    }

    public bool TryRemoveModule(string moduleId)
    {
        if (!_isEditMode)
        {
            return false;
        }

        if (_working.Count <= 1)
        {
            ShowToast("页面至少保留一个模块。");
            return false;
        }

        List<WidgetPlacement> updated = _working
            .Where(placement => !string.Equals(placement.ModuleId, moduleId, StringComparison.Ordinal))
            .Select(static placement => placement.Clone())
            .ToList();
        if (updated.Count == _working.Count)
        {
            ShowToast("未找到要移除的模块。");
            return false;
        }

        return ApplyWorking(updated, "已移除模块。");
    }

    /// <summary>在 SupportedSpans 中循环，优先原位扩展，否则寻找首个可容纳空位。</summary>
    public bool TryCycleSpan(string moduleId)
    {
        if (!_isEditMode || !WidgetRegistry.TryGet(moduleId, out IStatWidgetModule module))
        {
            return false;
        }

        WidgetPlacement? placement = _working.FirstOrDefault(
            candidate => string.Equals(candidate.ModuleId, moduleId, StringComparison.Ordinal));
        if (placement is null || module.Descriptor.SupportedSpans.Count <= 1)
        {
            ShowToast("该模块只有一种尺寸。");
            return false;
        }

        int current = module.Descriptor.SupportedSpans
            .ToList()
            .FindIndex(span => span.Columns == placement.ColSpan && span.Rows == placement.RowSpan);
        if (current < 0)
        {
            current = 0;
        }

        for (int offset = 1; offset < module.Descriptor.SupportedSpans.Count; offset++)
        {
            WidgetSpan candidate = module.Descriptor.SupportedSpans[
                (current + offset) % module.Descriptor.SupportedSpans.Count];
            if (candidate.Columns == placement.ColSpan && candidate.Rows == placement.RowSpan)
            {
                continue;
            }

            if (BentoLayoutMath.TryResize(
                    _working,
                    moduleId,
                    candidate,
                    findAlternativePosition: true,
                    out List<WidgetPlacement> updated,
                    out _))
            {
                return ApplyWorking(updated, $"尺寸已切换为 {candidate.Columns}×{candidate.Rows}。");
            }
        }

        ShowToast("周围没有足够空位切换尺寸；请先移动或缩小其他卡片。");
        return false;
    }

    public bool TryMoveModule(string moduleId, int targetCol, int targetRow)
    {
        if (!_isEditMode)
        {
            return false;
        }

        if (!BentoLayoutMath.TryMoveOrSwap(_working, moduleId, targetCol, targetRow,
                out List<WidgetPlacement> updated, out string? error))
        {
            ShowToast(error ?? "无法移动到该位置。");
            return false;
        }

        return ApplyWorking(updated, "卡片位置已更新。");
    }

    public bool ApplyScenario(LayoutScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (!_isEditMode)
        {
            return false;
        }

        PageLayoutConfig layout = scenario.Create();
        if (!LayoutValidation.TryNormalize(layout, out PageLayoutConfig normalized, out string? error))
        {
            ShowToast(error ?? "场景布局不合法。");
            return false;
        }

        _working = BentoLayoutMath.Clone(normalized.Placements);
        Rebuild();
        if (_showingDrawer)
        {
            RefreshCatalog();
        }

        ShowToast($"已套用「{scenario.Name}」，保存后生效。");
        return true;
    }

    private bool ApplyWorking(List<WidgetPlacement> placements, string message)
    {
        PageLayoutConfig candidate = new()
        {
            PageId = _pageId,
            Placements = placements
        };
        if (!LayoutValidation.TryNormalize(candidate, out PageLayoutConfig normalized, out string? error))
        {
            ShowToast(error ?? "布局不合法。");
            return false;
        }

        _working = BentoLayoutMath.Clone(normalized.Placements);
        Rebuild();
        if (_showingDrawer)
        {
            RefreshCatalog();
        }

        ShowToast(message);
        return true;
    }

    private void Rebuild()
    {
        WidgetContext context = _contextFactory();
        context.Live?.Clear();
        context = context.WithRequestReload(Rebuild);
        PageLayoutConfig layout = new()
        {
            PageId = _pageId,
            Placements = _working
        };
        _built = BentoPageBuilder.Build(_grid, layout, context, _isEditMode);
        if (_isEditMode)
        {
            foreach (BuiltWidget built in _built.Values)
            {
                WidgetPlacement placement = built.Placement;
                built.Frame.AttachEditorControls(
                    new WidgetSpan(placement.ColSpan, placement.RowSpan),
                    () => TryCycleSpan(placement.ModuleId),
                    () => TryRemoveModule(placement.ModuleId));
                AttachDrag(built.Frame.DragHandle, placement.ModuleId);
            }
        }

        Rebuilt?.Invoke();
    }

    private Border BuildToolbar()
    {
        StackPanel left = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                PageKit.Label("布局编辑", 11.5, PageKit.T.Accent, FontWeight.SemiBold),
                PageKit.Muted("拖动标题栏 · 尺寸循环 · 本页模块", 9.5),
                _addButton,
                _presetButton,
                _resetButton
            }
        };
        StackPanel right = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _cancelButton,
                _saveButton
            }
        };
        Grid bar = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                left,
                right
            }
        };
        Grid.SetColumn(right, 1);
        return new Border
        {
            IsVisible = false,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(PageKit.R(8, 12)),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Card, 0.92)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Accent, 0.36)),
            BorderThickness = new Thickness(1),
            BoxShadow = PageKit.Shadow(14, 4, 0.1),
            Child = bar
        };
    }

    private void BuildPresetItems()
    {
        _presetItems.Children.Clear();
        foreach (LayoutScenario scenario in LayoutScenarios.ForPage(_pageId))
        {
            LayoutScenario captured = scenario;
            Button item = new()
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(9, 6),
                CornerRadius = new CornerRadius(7),
                Background = PageKit.Brush(Colors.Transparent),
                Foreground = PageKit.Brush(PageKit.T.Text),
                BorderThickness = new Thickness(0),
                Content = new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        PageKit.Label(captured.Name, 10.5, PageKit.T.Text, FontWeight.SemiBold),
                        PageKit.Muted(captured.Description, 8.8)
                    }
                }
            };
            item.Click += (_, _) =>
            {
                _presetFlyout.Hide();
                ApplyScenario(captured);
            };
            _presetItems.Children.Add(item);
        }
    }

    private void BuildDrawerPanel()
    {
        Grid header = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(PageKit.Label("模块抽屉", 12, PageKit.T.Text, FontWeight.SemiBold));
        Button close = PagerButton("×");
        close.Width = 26;
        close.Click += (_, _) => CloseDrawer();
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        Grid filters = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,118"),
            ColumnSpacing = 6,
            Children =
            {
                _search,
                _categoryFilter
            }
        };
        Grid.SetColumn(_categoryFilter, 1);

        _drawerPanel.Children.Add(header);
        _drawerPanel.Children.Add(filters);
        _drawerPanel.Children.Add(_catalogSummary);
        _drawerPanel.Children.Add(_catalogBody);

        StackPanel pager = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                _previousCategoryButton,
                _catalogPageText,
                _nextCategoryButton
            }
        };
        _drawerPanel.Children.Add(pager);
        _drawerPanel.Children.Add(PageKit.Muted("点击添加；按住 ⠿ 拖到画布也可添加。", 8.8));
    }

    private void ToggleDrawer()
    {
        if (_showingDrawer)
        {
            CloseDrawer();
            return;
        }

        _showingDrawer = true;
        _drawerHost.Content = _drawerPanel;
        _drawer.IsVisible = true;
        _search.Text = "";
        _drawerPage = 0;
        RefreshCatalog();
    }

    private void CloseDrawer()
    {
        _showingDrawer = false;
        _drawer.IsVisible = false;
        _drawerHost.Content = null;
        HideGhost();
    }

    private int CatalogPageCount
    {
        get
        {
            int count = FilteredModules().Count;
            return Math.Max(1, (count + 6) / 7);
        }
    }

    /// <summary>只保留当前页实际出现的分类，避免抽屉里出现空分类。</summary>
    private List<(string Label, string Id)> BuildCategoryOptions()
    {
        string[] order =
        [
            "overview", "apps", "trends", "hourly", "keys",
            "hardware", "insights", "chars", "daily", "heatmap"
        ];
        HashSet<string> present = WidgetRegistry.ForPage(_ownerPage)
            .Select(static descriptor => descriptor.Category)
            .ToHashSet(StringComparer.Ordinal);

        List<(string Label, string Id)> options = [("全部分类", "")];
        foreach (string category in order)
        {
            if (present.Contains(category))
            {
                options.Add((CategoryLabel(category), category));
            }
        }

        return options;
    }

    internal List<WidgetDescriptor> FilteredModules()
    {
        IEnumerable<WidgetDescriptor> modules = WidgetRegistry.ForPage(_ownerPage);
        if (!string.IsNullOrEmpty(_drawerCategory))
        {
            modules = modules.Where(descriptor =>
                string.Equals(descriptor.Category, _drawerCategory, StringComparison.Ordinal));
        }

        string query = _drawerQuery.Trim();
        if (query.Length > 0)
        {
            modules = modules.Where(descriptor =>
                descriptor.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || descriptor.ModuleId.Contains(query, StringComparison.OrdinalIgnoreCase)
                || CategoryLabel(descriptor.Category).Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return modules
            .OrderBy(static descriptor => descriptor.Category, StringComparer.Ordinal)
            .ThenBy(static descriptor => descriptor.Title, StringComparer.Ordinal)
            .ToList();
    }

    private void RefreshCatalog()
    {
        if (!_showingDrawer)
        {
            return;
        }

        List<WidgetDescriptor> modules = FilteredModules();
        int pageCount = Math.Max(1, (modules.Count + 6) / 7);
        _drawerPage = Math.Clamp(_drawerPage, 0, pageCount - 1);
        _catalogBody.Children.Clear();
        int start = _drawerPage * 7;
        for (int i = start; i < Math.Min(modules.Count, start + 7); i++)
        {
            _catalogBody.Children.Add(CatalogRow(modules[i]));
        }

        if (modules.Count == 0)
        {
            _catalogBody.Children.Add(PageKit.Empty("没有匹配的模块"));
        }

        _catalogSummary.Text = $"本页 {modules.Count} 个可用模块 · 当前布局 {_working.Count} 个";
        _catalogPageText.Text = $"{_drawerPage + 1} / {pageCount}";
        _previousCategoryButton.Opacity = _drawerPage > 0 ? 1 : 0.25;
        _nextCategoryButton.Opacity = _drawerPage < pageCount - 1 ? 1 : 0.25;
    }

    private Control CatalogRow(WidgetDescriptor descriptor)
    {
        bool present = _working.Any(placement =>
            string.Equals(placement.ModuleId, descriptor.ModuleId, StringComparison.Ordinal));
        Border card = new()
        {
            Padding = new Thickness(7, 4),
            CornerRadius = new CornerRadius(6),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, present ? 0.22 : 0.5)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.45)),
            BorderThickness = new Thickness(1),
            Opacity = present ? 0.6 : 1
        };
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 6
        };
        StackPanel info = new() { Spacing = 0 };
        info.Children.Add(PageKit.Label(descriptor.Title, 10.2, PageKit.T.Text, FontWeight.SemiBold));
        info.Children.Add(PageKit.Muted(
            present
                ? $"已在页面 · {descriptor.ModuleId}"
                : $"{CategoryLabel(descriptor.Category)} · {descriptor.ModuleId} · {descriptor.DefaultSpan}",
            8.2));
        grid.Children.Add(info);

        TextBlock grip = PageKit.Label("⠿", 12, PageKit.T.Muted);
        grip.VerticalAlignment = VerticalAlignment.Center;
        grip.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(grip, "拖到画布添加");
        grip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            {
                return;
            }

            _catalogDragModuleId = descriptor.ModuleId;
            _catalogDragSpan = descriptor.DefaultSpan;
            e.Pointer.Capture(grip);
            e.Handled = true;
        };
        grip.PointerMoved += (_, e) => UpdateCatalogDrag(e);
        grip.PointerReleased += (_, e) => FinishCatalogDrag(e);
        grip.PointerCaptureLost += (_, _) =>
        {
            _catalogDragModuleId = null;
            HideGhost();
        };
        Grid.SetColumn(grip, 1);
        grid.Children.Add(grip);
        card.Child = grid;
        card.Tapped += (_, _) => TryAddModule(descriptor.ModuleId);
        card.PointerEntered += (_, _) =>
        {
            if (!present)
            {
                card.Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Accent, 0.12));
            }
        };
        card.PointerExited += (_, _) =>
        {
            card.Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, present ? 0.22 : 0.5));
        };
        return card;
    }

    private void UpdateCatalogDrag(PointerEventArgs e)
    {
        if (_catalogDragModuleId is null)
        {
            return;
        }

        Point position = e.GetPosition(_grid);
        if (IsOverDrawer(e) || !IsInsideGrid(position))
        {
            HideGhost();
            return;
        }

        if (BentoLayoutMath.TryFindFirstFit(_working, _catalogDragSpan, out int col, out int row))
        {
            ShowGhost(col, row, _catalogDragSpan);
        }
        else
        {
            HideGhost();
        }
    }

    private void FinishCatalogDrag(PointerReleasedEventArgs e)
    {
        string? moduleId = _catalogDragModuleId;
        _catalogDragModuleId = null;
        HideGhost();
        if (moduleId is null)
        {
            return;
        }

        e.Pointer.Capture(null);
        if (!IsOverDrawer(e) && IsInsideGrid(e.GetPosition(_grid)))
        {
            TryAddModule(moduleId);
        }
    }

    private bool IsOverDrawer(PointerEventArgs e)
    {
        if (!_drawer.IsVisible)
        {
            return false;
        }

        Point point = e.GetPosition(_drawer);
        return point.X >= 0
               && point.Y >= 0
               && point.X <= _drawer.Bounds.Width
               && point.Y <= _drawer.Bounds.Height;
    }

    private void AttachDrag(Control handle, string moduleId)
    {
        handle.PointerPressed += (_, e) =>
        {
            if (!_isEditMode
                || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            {
                return;
            }

            _dragModuleId = moduleId;
            e.Pointer.Capture(handle);
            UpdateDrag(e);
            e.Handled = true;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (string.Equals(_dragModuleId, moduleId, StringComparison.Ordinal))
            {
                UpdateDrag(e);
            }
        };
        handle.PointerReleased += (_, e) =>
        {
            if (!string.Equals(_dragModuleId, moduleId, StringComparison.Ordinal))
            {
                return;
            }

            int col = _ghostCol;
            int row = _ghostRow;
            _dragModuleId = null;
            HideGhost();
            e.Pointer.Capture(null);
            if (col >= 0 && row >= 0)
            {
                TryMoveModule(moduleId, col, row);
            }
        };
        handle.PointerCaptureLost += (_, _) =>
        {
            if (string.Equals(_dragModuleId, moduleId, StringComparison.Ordinal))
            {
                _dragModuleId = null;
                HideGhost();
            }
        };
    }

    private void UpdateDrag(PointerEventArgs e)
    {
        string? moduleId = _dragModuleId;
        if (moduleId is null)
        {
            return;
        }

        WidgetPlacement? placement = _working.FirstOrDefault(candidate =>
            string.Equals(candidate.ModuleId, moduleId, StringComparison.Ordinal));
        if (placement is null)
        {
            HideGhost();
            return;
        }

        Point position = e.GetPosition(_grid);
        if (!IsInsideGrid(position))
        {
            HideGhost();
            return;
        }

        WidgetSpan span = new(placement.ColSpan, placement.RowSpan);
        Size size = _grid.Bounds.Size;
        Size cell = BentoGridMath.GetCellSize(size, LayoutPresets.Columns, LayoutPresets.Rows, _grid.Spacing);
        double strideX = Math.Max(1, cell.Width + _grid.Spacing);
        double strideY = Math.Max(1, cell.Height + _grid.Spacing);
        int col = Math.Clamp(
            (int)Math.Floor(position.X / strideX),
            0,
            LayoutPresets.Columns - span.Columns);
        int row = Math.Clamp(
            (int)Math.Floor(position.Y / strideY),
            0,
            LayoutPresets.Rows - span.Rows);
        ShowGhost(col, row, span);
    }

    private bool IsInsideGrid(Point position)
    {
        Size size = _grid.Bounds.Size;
        return size.Width > 0
               && size.Height > 0
               && position.X >= 0
               && position.Y >= 0
               && position.X <= size.Width
               && position.Y <= size.Height;
    }

    private void ShowGhost(int col, int row, WidgetSpan span)
    {
        Rect rect = BentoGridMath.ComputeCell(
            _grid.Bounds.Size,
            LayoutPresets.Columns,
            LayoutPresets.Rows,
            _grid.Spacing,
            col,
            row,
            span.Columns,
            span.Rows);
        Canvas.SetLeft(_ghost, rect.X);
        Canvas.SetTop(_ghost, rect.Y);
        _ghost.Width = rect.Width;
        _ghost.Height = rect.Height;
        _ghost.IsVisible = true;
        _ghostCol = col;
        _ghostRow = row;
    }

    private void HideGhost()
    {
        _ghost.IsVisible = false;
        _ghostCol = -1;
        _ghostRow = -1;
    }

    private void UpdateEditingChrome()
    {
        _toolbar.IsVisible = _isEditMode;
        foreach (Button button in _entryButtons)
        {
            button.IsVisible = _canEdit && !_isEditMode;
        }
    }

    private void ShowToast(string message)
    {
        _toastText.Text = message;
        _toast.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private static string CategoryLabel(string category) => category switch
    {
        "overview" => "总览",
        "apps" => "应用",
        "trends" => "趋势",
        "hourly" => "时段",
        "keys" => "按键",
        "hardware" => "硬件",
        "insights" => "洞察",
        "chars" => "字符",
        "daily" => "日报",
        "heatmap" => "热区",
        _ => category
    };

    private static Button ToolButton(string text, bool primary = false) => new()
    {
        Content = text,
        FontSize = 10.5,
        Height = 26,
        Padding = new Thickness(9, 0),
        CornerRadius = new CornerRadius(7),
        Background = primary
            ? PageKit.Gradient(PageKit.T.Accent, PageKit.T.Cyan, 0)
            : PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, 0.72)),
        Foreground = PageKit.Brush(primary ? PageKit.T.OnAccent : PageKit.T.Text),
        BorderBrush = primary
            ? PageKit.Brush(Colors.Transparent)
            : PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.7)),
        BorderThickness = new Thickness(1),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Button PagerButton(string text) => new()
    {
        Content = text,
        Width = 24,
        Height = 22,
        Padding = new Thickness(0),
        FontSize = 12,
        CornerRadius = new CornerRadius(5),
        Background = PageKit.Brush(Colors.Transparent),
        Foreground = PageKit.Brush(PageKit.T.Muted),
        BorderThickness = new Thickness(0)
    };

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _toastTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
