using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.App.Pages;
using Kms.Render.Tokens;
using TextBlock = Avalonia.Controls.TextBlock;

namespace Kms.App.Widgets;

/// <summary>
/// 模块统一外框：圆角、主题毛玻璃底色、描边令牌与标题栏槽位。
/// 具体变体只放进内嵌的 ContentControl，保证任意模块混搭时视觉一致。
/// </summary>
public sealed class BentoCardFrame : UserControl
{
    private readonly Border _root;
    private readonly Grid _layout;
    private readonly Grid _header;
    private readonly StackPanel _titles;
    private readonly TextBlock _titleText;
    private readonly TextBlock _hintText;
    private readonly ContentControl _trailingHost;
    private readonly Border _globalBadge;
    private readonly ContentControl _variantSelectorHost;
    private readonly ContentControl _editorHost;
    private readonly StackPanel _trailingPanel;
    private readonly ContentControl _bodyHost;

    private string _title = "";
    private string? _headerHint;
    private Control? _headerTrailing;
    private bool _globalOnly;
    private Control? _body;
    private bool _showHeader = true;
    private bool _showVariantSelector;
    private bool _showEditorControls;
    private bool _elevated;
    private Thickness _contentPadding = new(16);
    private VerticalAlignment _bodyVerticalAlignment = VerticalAlignment.Stretch;
    private double _fillOpacity = 1;
    private double _borderOpacity = 0.9;

    public BentoCardFrame()
    {
        Focusable = false;
        ClipToBounds = true;

        _titleText = new TextBlock
        {
            Text = "",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = PageKit.Brush(PageKit.T.Text),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _hintText = new TextBlock
        {
            Text = "",
            IsVisible = false,
            FontSize = 9.5,
            Foreground = PageKit.Brush(PageKit.T.Muted),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _titles = new StackPanel
        {
            Spacing = 2,
            Background = Brushes.Transparent,
            Children =
            {
                _titleText,
                _hintText
            }
        };

        _trailingHost = new ContentControl
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _variantSelectorHost = new ContentControl
        {
            IsVisible = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        _editorHost = new ContentControl
        {
            IsVisible = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        _trailingPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _trailingHost,
                _variantSelectorHost,
                _editorHost
            }
        };
        _globalBadge = PageKit.Badge("全生命周期·全局", PageKit.T.Muted);
        _globalBadge.IsVisible = false;
        _trailingPanel.Children.Add(_globalBadge);

        _header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                _titles,
                _trailingPanel
            }
        };
        Grid.SetColumn(_trailingPanel, 1);

        _bodyHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            ClipToBounds = true
        };

        _layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                _header,
                _bodyHost
            }
        };
        Grid.SetRow(_bodyHost, 1);

        _root = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PageKit.R(10, 14)),
            ClipToBounds = true,
            Child = _layout
        };

        Content = _root;
        ApplyChrome();
    }

    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? "";
            _titleText.Text = _title;
            ToolTip.SetTip(_titleText, _title);
            UpdateHeaderVisibility();
        }
    }

    public string? HeaderHint
    {
        get => _headerHint;
        set
        {
            _headerHint = value;
            _hintText.Text = value ?? "";
            _hintText.IsVisible = !string.IsNullOrWhiteSpace(value);
        }
    }

    public Control? HeaderTrailing
    {
        get => _headerTrailing;
        set
        {
            _headerTrailing = value;
            _trailingHost.Content = value;
            _trailingHost.IsVisible = value is not null;
        }
    }

    /// <summary>全生命周期模块角标；由页面构建器按描述符注入。</summary>
    public bool GlobalOnly
    {
        get => _globalOnly;
        set
        {
            _globalOnly = value;
            _globalBadge.IsVisible = value;
            UpdateHeaderVisibility();
        }
    }

    public Control? Body
    {
        get => _body;
        set
        {
            _body = value;
            _bodyHost.Content = value;
        }
    }

    public bool ShowHeader
    {
        get => _showHeader;
        set
        {
            _showHeader = value;
            UpdateHeaderVisibility();
        }
    }

    public Thickness ContentPadding
    {
        get => _contentPadding;
        set
        {
            _contentPadding = value;
            _root.Padding = value;
        }
    }

    public VerticalAlignment BodyVerticalAlignment
    {
        get => _bodyVerticalAlignment;
        set
        {
            _bodyVerticalAlignment = value;
            _bodyHost.VerticalContentAlignment = value;
        }
    }

    public bool Elevated
    {
        get => _elevated;
        set
        {
            _elevated = value;
            _root.BoxShadow = value ? PageKit.Shadow(16, 5, 0.12) : default;
        }
    }

    public double FillOpacity
    {
        get => _fillOpacity;
        set
        {
            _fillOpacity = Math.Clamp(value, 0, 1);
            ApplyChrome();
        }
    }

    public double BorderOpacity
    {
        get => _borderOpacity;
        set
        {
            _borderOpacity = Math.Clamp(value, 0, 1);
            ApplyChrome();
        }
    }

    /// <summary>当前变体。默认锁定态不显示选择器；Step 4 编辑器可打开。</summary>
    public string CurrentVariant { get; private set; } = "";

    public void SetCurrentVariant(string variant)
    {
        CurrentVariant = variant ?? "";
    }

    /// <summary>编辑态拖动标题栏的鼠标捕获区域。</summary>
    public Control DragHandle => _titles;

    /// <summary>编辑态开关；默认 false，锁定态完全隐藏切换器。</summary>
    public bool ShowVariantSelector
    {
        get => _showVariantSelector;
        set
        {
            _showVariantSelector = value;
            _variantSelectorHost.IsVisible = value && _variantSelectorHost.Content is not null;
            UpdateHeaderVisibility();
        }
    }

    /// <summary>
    /// 编辑态尺寸循环与移除按钮。仅在 Step 4 编辑器中挂载；
    /// 单变体/锁定态不会出现任何额外装饰。
    /// </summary>
    public void AttachEditorControls(WidgetSpan span, Action onCycleSpan, Action onRemove)
    {
        ArgumentNullException.ThrowIfNull(onCycleSpan);
        ArgumentNullException.ThrowIfNull(onRemove);

        Button spanButton = EditorButton($"{span.Columns}×{span.Rows}", "切换尺寸");
        spanButton.Click += (_, _) => onCycleSpan();

        Button removeButton = EditorButton("×", "移除模块");
        removeButton.Foreground = PageKit.Brush(PageKit.T.Red);
        removeButton.Click += (_, _) => onRemove();

        _editorHost.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                spanButton,
                removeButton
            }
        };
        _showEditorControls = true;
        _editorHost.IsVisible = true;
        ToolTip.SetTip(_titles, "按住标题栏拖动卡片");
        _titles.Cursor = new Cursor(StandardCursorType.SizeAll);
        UpdateHeaderVisibility();
    }

    private static Button EditorButton(string text, string tip)
    {
        Button button = new()
        {
            Content = text,
            FontSize = 9.5,
            Height = 22,
            MinWidth = 20,
            Padding = new Thickness(4, 0),
            CornerRadius = new CornerRadius(6),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, 0.9)),
            Foreground = PageKit.Brush(PageKit.T.Muted),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.8)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private void UpdateHeaderVisibility()
    {
        _header.IsVisible = _showHeader
                            || _globalOnly
                            || (_showVariantSelector && _variantSelectorHost.Content is not null)
                            || (_showEditorControls && _editorHost.Content is not null);
    }

    /// <summary>
    /// 挂载轻量变体选择器。回调只重建当前卡片内容，复用页面级只读模型快照。
    /// </summary>
    public void AttachVariantSelector(
        IReadOnlyList<string> variants,
        string currentVariant,
        Action<string>? onSelected)
    {
        ArgumentNullException.ThrowIfNull(variants);
        if (variants.Count == 0)
        {
            _variantSelectorHost.Content = null;
            return;
        }

        CurrentVariant = string.IsNullOrWhiteSpace(currentVariant) || !variants.Contains(currentVariant, StringComparer.Ordinal)
            ? variants[0]
            : currentVariant;
        ComboBox selector = new()
        {
            ItemsSource = variants.ToArray(),
            SelectedItem = CurrentVariant,
            FontSize = 9.5,
            MinWidth = 66,
            Height = 22,
            Padding = new Thickness(5, 0, 1, 0),
            CornerRadius = new CornerRadius(6),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, 0.9)),
            Foreground = PageKit.Brush(PageKit.T.Muted),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.8)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(selector, "切换图表样式");
        selector.SelectionChanged += (_, _) =>
        {
            if (selector.SelectedItem is not string selected
                || string.Equals(selected, CurrentVariant, StringComparison.Ordinal))
            {
                return;
            }

            CurrentVariant = selected;
            onSelected?.Invoke(selected);
        };
        _variantSelectorHost.Content = selector;
        _variantSelectorHost.IsVisible = _showVariantSelector;
        UpdateHeaderVisibility();
    }

    /// <summary>用同一模块的新变体视图刷新外框，不重建页面或重新聚合数据。</summary>
    public void ApplyView(WidgetView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        HeaderHint = view.HeaderHint;
        HeaderTrailing = view.HeaderTrailing;
        Body = view.Content;
        ShowHeader = view.ShowHeader;
        ContentPadding = view.ContentPadding;
        BodyVerticalAlignment = view.BodyVerticalAlignment;
        Elevated = view.Elevated;
        FillOpacity = view.FillOpacity;
        BorderOpacity = view.BorderOpacity;
    }

    public static BentoCardFrame Create(WidgetDescriptor descriptor, WidgetView view)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(view);

        return new BentoCardFrame
        {
            Title = descriptor.Title,
            HeaderHint = view.HeaderHint,
            HeaderTrailing = view.HeaderTrailing,
            GlobalOnly = descriptor.GlobalOnly,
            Body = view.Content,
            ShowHeader = view.ShowHeader,
            ContentPadding = view.ContentPadding,
            BodyVerticalAlignment = view.BodyVerticalAlignment,
            Elevated = view.Elevated,
            FillOpacity = view.FillOpacity,
            BorderOpacity = view.BorderOpacity
        };
    }

    private void ApplyChrome()
    {
        ThemeTokens theme = PageKit.T;
        Color fill = _fillOpacity >= 0.999
            ? PageKit.CardFill
            : PageKit.Alpha(theme.Card, _fillOpacity);
        _root.Background = PageKit.Brush(fill);
        _root.BorderBrush = PageKit.Brush(PageKit.Alpha(theme.Line, _borderOpacity));
    }
}
