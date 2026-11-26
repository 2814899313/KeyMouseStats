using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia.Media;

namespace Kms.Render.Text;

/// <summary>
/// 单行文本度量 LRU 缓存。键 = (文本, 字族, 字号, 字重, 最大宽度)，
/// 默认容量 512；容量 0 表示不缓存。
/// </summary>
public sealed class TextMeasureCache
{
    public const int DefaultCapacity = 512;

    private readonly object _gate = new();
    private readonly Dictionary<TextMeasureKey, LinkedListNode<CacheSlot>> _map;
    private readonly LinkedList<CacheSlot> _lru = new();
    private readonly Func<TextMeasureKey, TextMeasureEntry> _factory;
    private readonly int _capacity;

    /// <summary>生产路径：用 Avalonia <see cref="FormattedText"/> 度量。</summary>
    public TextMeasureCache(int capacity = DefaultCapacity)
        : this(capacity, static key => CreateAvaloniaEntry(key))
    {
    }

    /// <summary>
    /// 可注入度量委托，使用默认容量。测试可在没有 Avalonia 渲染后端时验证 LRU 与键语义，
    /// 此构造不会触碰 <see cref="FormattedText"/>。
    /// </summary>
    public TextMeasureCache(Func<TextMeasureKey, TextMetrics> measure)
        : this(DefaultCapacity, measure)
    {
    }

    /// <summary>
    /// 可注入度量委托。测试可在没有 Avalonia 渲染后端时验证 LRU 与键语义，
    /// 此构造不会触碰 <see cref="FormattedText"/>。
    /// </summary>
    public TextMeasureCache(int capacity, Func<TextMeasureKey, TextMetrics> measure)
        : this(capacity, CreateEntryFactory(measure))
    {
    }

    private TextMeasureCache(int capacity, Func<TextMeasureKey, TextMeasureEntry> factory)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        ArgumentNullException.ThrowIfNull(factory);

        _capacity = capacity;
        _factory = factory;
        _map = new Dictionary<TextMeasureKey, LinkedListNode<CacheSlot>>(capacity);
    }

    public int Capacity => _capacity;

    /// <summary>当前缓存条目数（不是容量）。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _lru.Clear();
        }
    }

    public TextMeasureEntry Get(
        string text,
        string fontFamily,
        double fontSize,
        FontWeight fontWeight = FontWeight.Normal,
        double maxWidth = double.PositiveInfinity) =>
        Get(new TextMeasureKey(text, fontFamily, fontSize, fontWeight, maxWidth));

    public TextMeasureEntry Get(in TextMeasureKey key)
    {
        TextMeasureKey normalized = Normalize(key);

        lock (_gate)
        {
            if (_map.TryGetValue(normalized, out LinkedListNode<CacheSlot>? node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Entry;
            }
        }

        TextMeasureEntry created = _factory(normalized);
        if (_capacity == 0)
        {
            return created;
        }

        lock (_gate)
        {
            // 工厂调用在锁外；并发下可能已有同键条目。
            if (_map.TryGetValue(normalized, out LinkedListNode<CacheSlot>? existing))
            {
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return existing.Value.Entry;
            }

            var slot = new CacheSlot(normalized, created);
            LinkedListNode<CacheSlot> newNode = _lru.AddFirst(slot);
            _map.Add(normalized, newNode);

            while (_map.Count > _capacity)
            {
                LinkedListNode<CacheSlot> last = _lru.Last!;
                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
            }

            return created;
        }
    }

    public TextMetrics Measure(
        string text,
        string fontFamily,
        double fontSize,
        FontWeight fontWeight = FontWeight.Normal,
        double maxWidth = double.PositiveInfinity) =>
        Get(text, fontFamily, fontSize, fontWeight, maxWidth).Metrics;

    public bool TryGet(in TextMeasureKey key, [NotNullWhen(true)] out TextMeasureEntry? entry)
    {
        TextMeasureKey normalized = Normalize(key);

        lock (_gate)
        {
            if (_map.TryGetValue(normalized, out LinkedListNode<CacheSlot>? node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                entry = node.Value.Entry;
                return true;
            }
        }

        entry = null;
        return false;
    }

    /// <summary>
    /// 取缓存的 <see cref="FormattedText"/> 并设置绘制前景色。实例是共享的，
    /// 只在 UI 线程绘制前调用；下一次取同键文本可能复用同一个对象。
    /// </summary>
    public FormattedText? GetFormattedText(
        string text,
        string fontFamily,
        double fontSize,
        FontWeight fontWeight = FontWeight.Normal,
        double maxWidth = double.PositiveInfinity,
        IBrush? foreground = null)
    {
        FormattedText? formatted = Get(text, fontFamily, fontSize, fontWeight, maxWidth).FormattedText;
        if (formatted is not null && foreground is not null)
        {
            formatted.SetForegroundBrush(foreground);
        }

        return formatted;
    }

    private static Func<TextMeasureKey, TextMeasureEntry> CreateEntryFactory(
        Func<TextMeasureKey, TextMetrics> measure)
    {
        ArgumentNullException.ThrowIfNull(measure);
        return key => new TextMeasureEntry(measure(key));
    }

    private static TextMeasureKey Normalize(in TextMeasureKey key)
    {
        ArgumentNullException.ThrowIfNull(key.Text);
        ArgumentNullException.ThrowIfNull(key.FontFamily);

        if (!double.IsFinite(key.FontSize) || key.FontSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(key), key.FontSize, "字号必须是有限正数。");
        }

        double maxWidth = double.IsNaN(key.MaxWidth)
            ? double.PositiveInfinity
            : key.MaxWidth < 0
                ? 0
                : key.MaxWidth;

        return key with { MaxWidth = maxWidth };
    }

    private static TextMeasureEntry CreateAvaloniaEntry(TextMeasureKey key)
    {
        var typeface = new Typeface(
            new FontFamily(key.FontFamily),
            FontStyle.Normal,
            key.FontWeight);

        var formatted = new FormattedText(
            key.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            key.FontSize,
            Brushes.White)
        {
            TextAlignment = TextAlignment.Left
        };

        if (double.IsFinite(key.MaxWidth))
        {
            // 单行 + 字符级省略号：等价于 GDI+ 的 EllipsisCharacter。
            formatted.MaxTextWidth = key.MaxWidth;
            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        return new TextMeasureEntry(
            new TextMetrics(formatted.Width, formatted.Height, formatted.Baseline),
            formatted);
    }

    private sealed class CacheSlot
    {
        public CacheSlot(TextMeasureKey key, TextMeasureEntry entry)
        {
            Key = key;
            Entry = entry;
        }

        public TextMeasureKey Key { get; }

        public TextMeasureEntry Entry { get; }
    }
}
