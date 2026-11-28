using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Kms.App.Layout;
using Kms.App.Pages;
using Kms.Core.Apps;

namespace Kms.App.Widgets;

/// <summary>一次 Bento 构建的结果，页面可保留动态视图引用（分页、时间线等）。</summary>
public sealed class BuiltWidget
{
    public required WidgetDescriptor Descriptor { get; init; }

    public required WidgetPlacement Placement { get; init; }

    public required BentoCardFrame Frame { get; init; }

    public required WidgetView View { get; set; }
}

/// <summary>把声明式布局映射为 BentoCardFrame 子项；只构建布局中的模块。</summary>
public static class BentoPageBuilder
{
    public static IReadOnlyDictionary<string, BuiltWidget> Build(
        BentoGridPanel grid,
        PageLayoutConfig layout,
        WidgetContext context,
        bool editMode = false,
        Action<string, string>? onVariantChanged = null)
    {
        grid.Children.Clear();
        Dictionary<string, BuiltWidget> built = new(StringComparer.Ordinal);
        foreach (WidgetPlacement placement in layout.Placements)
        {
            if (!WidgetRegistry.TryGet(placement.ModuleId, out IStatWidgetModule module))
            {
                // 即使模块注册缺失也保留原坐标占位，避免整行塌陷。
                WidgetDescriptor missingDescriptor = MissingDescriptor(placement);
                WidgetView missingView = new()
                {
                    Content = PageKit.Empty("模块不可用"),
                    HeaderHint = placement.ModuleId
                };
                BentoCardFrame missing = BentoCardFrame.Create(missingDescriptor, missingView);
                BentoGridPanel.SetCol(missing, placement.Col);
                BentoGridPanel.SetRow(missing, placement.Row);
                BentoGridPanel.SetColSpan(missing, placement.ColSpan);
                BentoGridPanel.SetRowSpan(missing, placement.RowSpan);
                missing.IsVisible = true;
                grid.Children.Add(missing);
                built[placement.ModuleId] = new BuiltWidget
                {
                    Descriptor = missingDescriptor,
                    Placement = placement,
                    Frame = missing,
                    View = missingView
                };
                continue;
            }

            string variant = WidgetRegistry.ResolveVariant(module.Descriptor, placement.VariantId);
            placement.VariantId = variant;
            WidgetContext placementContext = context
                .WithVariant(variant)
                .WithSpan(new WidgetSpan(placement.ColSpan, placement.RowSpan));
            WidgetView view = CreateView(module, placementContext);
            BentoCardFrame frame = BentoCardFrame.Create(module.Descriptor, view);
            frame.SetCurrentVariant(variant);
            if (editMode && module.Descriptor.SupportedVariants.Count > 1)
            {
                frame.ShowVariantSelector = true;
                frame.AttachVariantSelector(module.Descriptor.SupportedVariants, variant, selected =>
                {
                    placement.VariantId = selected;
                    WidgetView updated = CreateView(module, context
                        .WithVariant(selected)
                        .WithSpan(new WidgetSpan(placement.ColSpan, placement.RowSpan)));
                    frame.ApplyView(updated);
                    if (built.TryGetValue(module.Descriptor.ModuleId, out BuiltWidget? target))
                    {
                        target.View = updated;
                    }

                    onVariantChanged?.Invoke(module.Descriptor.ModuleId, selected);
                });
            }

            // 降级视图同样占用原布局坐标：只替换内容，不删除/隐藏/重排卡片。
            BentoGridPanel.SetCol(frame, placement.Col);
            BentoGridPanel.SetRow(frame, placement.Row);
            BentoGridPanel.SetColSpan(frame, placement.ColSpan);
            BentoGridPanel.SetRowSpan(frame, placement.RowSpan);
            frame.IsVisible = true;
            grid.Children.Add(frame);
            built[placement.ModuleId] = new BuiltWidget
            {
                Descriptor = module.Descriptor,
                Placement = placement,
                Frame = frame,
                View = view
            };
        }

        return built;
    }

    private static WidgetDescriptor MissingDescriptor(WidgetPlacement placement) => new()
    {
        ModuleId = placement.ModuleId,
        Title = "模块不可用",
        Category = "系统",
        OwnerPage = PageId.Overview,
        DefaultSpan = new WidgetSpan(placement.ColSpan, placement.RowSpan),
        SupportedSpans = [new WidgetSpan(placement.ColSpan, placement.RowSpan)],
        SupportedVariants = [""],
        Factory = static _ => new WidgetView
        {
            Content = PageKit.Empty("模块不可用"),
            HeaderHint = "布局引用了未注册模块"
        }
    };

    private static WidgetView CreateView(IStatWidgetModule module, WidgetContext context)
    {
        WidgetDescriptor descriptor = module.Descriptor;
        if (!descriptor.GlobalOnly
            && !context.AppFilter.IsAll()
            && !context.Capabilities.IsExact(descriptor.RequiredCapabilities))
        {
            return DegradedView(descriptor, context);
        }

        return module.CreateView(context);
    }

    private static WidgetView DegradedView(WidgetDescriptor descriptor, WidgetContext context)
    {
        // 3×1 微卡只有一行内容区：把多行说明压成单行徽标，避免上下裁切。
        if (context.Span.Rows <= 1)
        {
            StackPanel compact = new()
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    PageKit.Label(
                        "暂未采集",
                        10.5,
                        PageKit.T.Orange,
                        Avalonia.Media.FontWeight.SemiBold,
                        maxWidth: 220,
                        trimming: Avalonia.Media.TextTrimming.CharacterEllipsis)
                }
            };
            Border compactCard = new()
            {
                Padding = new Thickness(8, 4),
                CornerRadius = new CornerRadius(8),
                Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, 0.45)),
                BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.6)),
                BorderThickness = new Thickness(1),
                Child = compact,
                VerticalAlignment = VerticalAlignment.Center
            };
            return new WidgetView
            {
                Content = compactCard,
                ShowHeader = true,
                HeaderHint = descriptor.Title + " · 能力门控",
                HeaderTrailing = PageKit.Badge("PENDING", PageKit.T.Orange),
                ContentPadding = new Thickness(6)
            };
        }

        string appName = context.AppFilter.AppKey ?? "该应用";
        StackPanel body = new()
        {
            Spacing = 7,
            Children =
            {
                PageKit.Label("该维度暂未采集", 13, PageKit.T.Text, Avalonia.Media.FontWeight.SemiBold),
                PageKit.Label(
                    "该维度自 v4.2.0 起按应用独立采集，当前所选区间暂无该应用的分桶明细。",
                    10.5,
                    PageKit.T.Muted),
                PageKit.Label(
                    "筛选：" + appName,
                    9,
                    PageKit.Alpha(PageKit.T.Muted, 0.85),
                    maxWidth: 260,
                    trimming: Avalonia.Media.TextTrimming.CharacterEllipsis)
            }
        };

        Border card = new()
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, 0.45)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.6)),
            BorderThickness = new Thickness(1),
            Child = body,
            VerticalAlignment = VerticalAlignment.Center
        };
        return new WidgetView
        {
            Content = card,
            ShowHeader = true,
            HeaderHint = descriptor.Title + " · 能力门控",
            HeaderTrailing = PageKit.Badge("PENDING", PageKit.T.Orange),
            ContentPadding = new Thickness(10)
        };
    }

    private static bool IsAll(this AppFilterScope filter) =>
        filter.Kind == AppFilterKind.All;

    public static PageLayoutConfig Layout(
        string pageId,
        params WidgetPlacement[] placements) => new()
    {
        PageId = pageId,
        Placements = [.. placements]
    };

    public static WidgetPlacement Place(
        string moduleId,
        string variantId,
        int col,
        int row,
        int colSpan,
        int rowSpan) => new()
    {
        ModuleId = moduleId,
        VariantId = variantId,
        Col = col,
        Row = row,
        ColSpan = colSpan,
        RowSpan = rowSpan
    };
}
