using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Kms.App.Apps;
using Kms.App.Layout;
using Kms.Core.Apps;
using Kms.Core.Model;
using Kms.Core.Text;
using Kms.Core.Time;

namespace Kms.App.Widgets;

/// <summary>
/// 模块在 12×6 Bento 栅格中的尺寸。与 JSON 布局文件中的 "3x1" 文本一一对应；
/// 解析不依赖反射，NativeAOT/Trim 下安全。
/// </summary>
public readonly record struct WidgetSpan(int Columns, int Rows)
{
    public static WidgetSpan Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("模块尺寸不能为空。");
        }

        string[] parts = text.Trim().Split(['x', 'X', '×'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int columns)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rows)
            || columns <= 0
            || rows <= 0)
        {
            throw new FormatException($"无效的模块尺寸：{text}。期望格式如 3x1。");
        }

        return new WidgetSpan(columns, rows);
    }

    public bool IsWithin(int columns, int rows) =>
        Columns > 0 && Rows > 0 && Columns <= columns && Rows <= rows;

    public override string ToString() => $"{Columns}x{Rows}";
}

/// <summary>
/// 模块元数据。所有实例都由 <see cref="WidgetRegistry"/> 静态显式注册，
/// 不通过程序集扫描发现；<see cref="Factory"/> 是编译期确定的委托。
/// </summary>
public sealed class WidgetDescriptor
{
    public required string ModuleId { get; init; }

    public required string Title { get; init; }

    public required string Category { get; init; }

    /// <summary>模块的强类型归属页。编辑抽屉默认只展示该页模块。</summary>
    public required PageId OwnerPage { get; init; }

    /// <summary>
    /// 极少数明确允许跨页复用的模块（如外设磨损、官方极客场景里的心流脉冲）。
    /// 未列入此集合的模块不会出现在其他页面的编辑抽屉中。
    /// </summary>
    public IReadOnlySet<PageId> AllowedPages { get; init; } = new HashSet<PageId>();

    public bool IsAvailableOn(PageId page) =>
        OwnerPage == page || AllowedPages.Contains(page);

    public required WidgetSpan DefaultSpan { get; init; }

    public required IReadOnlyList<WidgetSpan> SupportedSpans { get; init; }

    public required IReadOnlyList<string> SupportedVariants { get; init; }

    /// <summary>该模块精确渲染所需的应用维度能力；缺能力时统一降级。</summary>
    public AppDataCapability RequiredCapabilities { get; set; } = AppDataCapability.AppTotals;

    /// <summary>全生命周期/全局模块，不参与应用过滤与能力门控。</summary>
    public bool GlobalOnly { get; set; }

    public required Func<WidgetContext, WidgetView> Factory { get; init; }
}

/// <summary>统一模块协议：元数据 + 视图工厂。</summary>
public interface IStatWidgetModule
{
    WidgetDescriptor Descriptor { get; }

    WidgetView CreateView(WidgetContext context);
}

/// <summary>把描述符中的静态工厂委托适配为 <see cref="IStatWidgetModule"/>。</summary>
public sealed class DelegateWidgetModule(WidgetDescriptor descriptor) : IStatWidgetModule
{
    public WidgetDescriptor Descriptor { get; } = descriptor;

    public WidgetView CreateView(WidgetContext context) => Descriptor.Factory(context);
}

/// <summary>
/// 页面创建的模块上下文。页面级聚合模型（<see cref="Model"/>）只计算一次，
/// 各模块只消费自己需要的数据；未放入布局的模块不会被创建，也不会订阅任何东西。
/// </summary>
public sealed class WidgetContext
{
    public required StatsData Data { get; init; }

    public required TimeScopeRange Scope { get; init; }

    /// <summary>当前模块在栅格中的实际尺寸；未经过编辑器构建时为 0×0。</summary>
    public WidgetSpan Span { get; init; }

    /// <summary>全局应用过滤；未注入时为 All。</summary>
    public AppFilterScope AppFilter { get; init; }

    /// <summary>当前过滤条件下各维度是否可按应用精确查询。</summary>
    public AppDataCapabilities Capabilities { get; init; } = AppDataCapabilities.AllExact;

    /// <summary>页面已异步取得的单应用快照；All 时为 null。</summary>
    public AppProfileSnapshot? Profile { get; init; }

    /// <summary>页面级应用上下文；保留 Category 展开集合与分类服务。</summary>
    public AppPageContext? AppPage { get; init; }

    public required string VariantId { get; init; }

    public InputService? Service { get; init; }

    public WidgetLiveBus? Live { get; init; }

    public WidgetPageModel? Model { get; init; }

    /// <summary>字符模块在非字符页被混搭时使用的 Rime 快照；为空时按需加载一次并缓存。</summary>
    public RimeLogStats? Rime { get; init; }

    /// <summary>跨页混搭时按稳定字符串键复用模块级页面模型，避免变体切换重复聚合。</summary>
    public Dictionary<string, WidgetPageModel> Models { get; init; } = new(StringComparer.Ordinal);

    /// <summary>模块内交互（指标切换、小时选择等）需要整页重排时调用；只属于当前激活页。</summary>
    public Action? RequestReload { get; init; }

    public T ResolveModel<T>(string key, Func<T> factory)
        where T : WidgetPageModel
    {
        if (Model is T typed)
        {
            return typed;
        }

        string scopedKey = ModelKey(key);
        if (Models.TryGetValue(scopedKey, out WidgetPageModel? cached) && cached is T cachedTyped)
        {
            return cachedTyped;
        }

        T created = factory();
        Models[scopedKey] = created;
        return created;
    }

    /// <summary>缓存键必须带应用过滤维度，防止 All 与单应用的页面模型互相污染。</summary>
    public string ModelKey(string key) =>
        key + "|" + AppFilter.CacheKey + "|" + (Profile is null ? "pending" : "loaded");

    public WidgetContext WithVariant(string variantId) => new()
    {
        Data = Data,
        Scope = Scope,
        Span = Span,
        AppFilter = AppFilter,
        Capabilities = Capabilities,
        Profile = Profile,
        AppPage = AppPage,
        VariantId = variantId,
        Service = Service,
        Live = Live,
        Model = Model,
        Rime = Rime,
        Models = Models,
        RequestReload = RequestReload
    };

    /// <summary>栅格构建时注入当前模块的实际尺寸，供 3×1 / 6×2 等紧凑变体自适应。</summary>
    public WidgetContext WithSpan(WidgetSpan span) => new()
    {
        Data = Data,
        Scope = Scope,
        Span = span,
        AppFilter = AppFilter,
        Capabilities = Capabilities,
        Profile = Profile,
        AppPage = AppPage,
        VariantId = VariantId,
        Service = Service,
        Live = Live,
        Model = Model,
        Rime = Rime,
        Models = Models,
        RequestReload = RequestReload
    };

    /// <summary>编辑器重建页面时把模块内交互的刷新回调指向编辑器自身。</summary>
    public WidgetContext WithRequestReload(Action? requestReload) => new()
    {
        Data = Data,
        Scope = Scope,
        Span = Span,
        AppFilter = AppFilter,
        Capabilities = Capabilities,
        Profile = Profile,
        AppPage = AppPage,
        VariantId = VariantId,
        Service = Service,
        Live = Live,
        Model = Model,
        Rime = Rime,
        Models = Models,
        RequestReload = requestReload
    };
}

/// <summary>页面级聚合模型基类；具体页面用强类型子类承载快照。</summary>
public abstract class WidgetPageModel;

/// <summary>
/// 模块视图描述。BentoCardFrame 负责统一外框，模块只提供内容与可选标题栏挂件。
/// </summary>
public sealed class WidgetView
{
    public required Control Content { get; init; }

    public Control? HeaderTrailing { get; init; }

    public string? HeaderHint { get; init; }

    public bool ShowHeader { get; init; } = true;

    public VerticalAlignment BodyVerticalAlignment { get; init; } = VerticalAlignment.Stretch;

    public Thickness ContentPadding { get; init; } = new(16);

    public bool Elevated { get; init; }

    public double FillOpacity { get; init; } = 1;

    public double BorderOpacity { get; init; } = 0.9;
}

/// <summary>活跃模块的实时刷新协议。只有当前页面布局内的模块会注册。</summary>
public interface ILiveWidget
{
    void UpdateLive(in WidgetLiveSnapshot snapshot);
}

/// <summary>一秒一次的轻量实时快照，避免模块在 Tick 中重复聚合历史。</summary>
public readonly record struct WidgetLiveSnapshot(
    long Keys,
    long Clicks,
    long Wheel,
    double Meters,
    double ActiveSeconds,
    int Sessions,
    long KeysPerMinute,
    long Apm);

/// <summary>页面持有的实时广播器；不依赖事件，不产生未激活模块的订阅。</summary>
public sealed class WidgetLiveBus
{
    private readonly List<ILiveWidget> _widgets = [];

    public int Count => _widgets.Count;

    /// <summary>单应用过滤时冻结全局实时速率，避免把全局值冒充应用值。</summary>
    public bool IsFrozen { get; set; }

    public void Register(ILiveWidget widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        if (!_widgets.Contains(widget))
        {
            _widgets.Add(widget);
        }
    }

    public void Publish(in WidgetLiveSnapshot snapshot)
    {
        if (IsFrozen)
        {
            return;
        }

        for (int i = 0; i < _widgets.Count; i++)
        {
            _widgets[i].UpdateLive(snapshot);
        }
    }

    public void Clear() => _widgets.Clear();
}
