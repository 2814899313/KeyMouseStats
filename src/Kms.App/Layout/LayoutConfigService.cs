using System.Text;
using System.Text.Json;

namespace Kms.App.Layout;

/// <summary>
/// 声明式布局持久化。用户覆盖写独立的 layouts.json，绝不污染 stats.txt；
/// 文件缺失/损坏/校验失败时按页回退到 <see cref="DefaultPresets"/>。
/// 所有 JSON 读写都走 <see cref="LayoutJsonContext"/> 源生成器。
/// </summary>
public sealed class LayoutConfigService
{
    private static readonly Lazy<LayoutConfigService> DefaultLazy =
        new(static () => new LayoutConfigService(DataPaths.V4Root));

    private static readonly Dictionary<string, PageLayoutConfig> Presets = new(StringComparer.Ordinal)
    {
        [LayoutPresets.OverviewPageId] = LayoutPresets.CreateOverview(),
        [LayoutPresets.AppsPageId] = LayoutPresets.CreateApps(),
        [LayoutPresets.TrendsPageId] = LayoutPresets.CreateTrends(),
        [LayoutPresets.HourlyPageId] = LayoutPresets.CreateHourly(),
        [LayoutPresets.KeysPageId] = LayoutPresets.CreateKeys(),
        [LayoutPresets.InsightsPageId] = LayoutPresets.CreateInsights(),
        [LayoutPresets.CharsPageId] = LayoutPresets.CreateChars(),
        [LayoutPresets.DailyPageId] = LayoutPresets.CreateDaily(),
        [LayoutPresets.HeatmapPageId] = LayoutPresets.CreateHeatmap()
    };

    private readonly object _gate = new();
    private readonly string _root;
    private LayoutDocument? _document;

    public LayoutConfigService(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _root = Path.GetFullPath(rootDirectory);
        LayoutPath = Path.Combine(_root, "layouts.json");
    }

    public static LayoutConfigService Default => DefaultLazy.Value;

    public static IReadOnlyDictionary<string, PageLayoutConfig> DefaultPresets => Presets;

    public string LayoutPath { get; }

    /// <summary>读取页面布局；用户覆盖合法时优先使用，否则回退官方预设。</summary>
    public PageLayoutConfig GetPageLayout(string pageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        LayoutDocument document = EnsureLoaded();
        PageLayoutConfig? custom = document.Pages.FirstOrDefault(
            page => string.Equals(page.PageId, pageId, StringComparison.Ordinal));

        if (custom is not null
            && LayoutValidation.TryNormalize(custom, out PageLayoutConfig normalized, out _))
        {
            return normalized;
        }

        return GetDefault(pageId);
    }

    public static PageLayoutConfig GetDefault(string pageId) =>
        Presets.TryGetValue(pageId, out PageLayoutConfig? preset)
            ? preset.Clone()
            : new PageLayoutConfig { PageId = pageId };

    /// <summary>清除某页的用户覆盖并立即持久化；下次读取回到官方预设。</summary>
    public void ResetPageToDefault(string pageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        LayoutDocument snapshot;
        lock (_gate)
        {
            LayoutDocument current = EnsureLoaded();
            snapshot = current.Clone();
            snapshot.Pages.RemoveAll(page => string.Equals(page.PageId, pageId, StringComparison.Ordinal));
            _document = snapshot;
        }

        TryPersist(snapshot);
    }

    /// <summary>
    /// 保存页面布局。写入前做完整校验（模块存在、尺寸/变体受支持、12×6 内不重叠），
    /// 通过临时文件原子替换，避免半截 JSON。
    /// </summary>
    public async Task SavePageLayoutAsync(
        string pageId,
        IReadOnlyList<WidgetPlacement> placements,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentNullException.ThrowIfNull(placements);

        PageLayoutConfig candidate = new()
        {
            PageId = pageId,
            Placements = placements.Select(static placement => placement.Clone()).ToList()
        };
        if (!LayoutValidation.TryNormalize(candidate, out PageLayoutConfig normalized, out string? error))
        {
            throw new ArgumentException(error ?? "布局不合法。", nameof(placements));
        }

        LayoutDocument snapshot;
        lock (_gate)
        {
            LayoutDocument current = EnsureLoaded();
            snapshot = current.Clone();
            snapshot.Pages.RemoveAll(page => string.Equals(page.PageId, pageId, StringComparison.Ordinal));
            snapshot.Pages.Add(normalized);
        }

        await PersistAsync(snapshot, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _document = snapshot;
        }
    }

    private LayoutDocument EnsureLoaded()
    {
        if (_document is not null)
        {
            return _document;
        }

        _document = LoadFromDisk();
        return _document;
    }

    private LayoutDocument LoadFromDisk()
    {
        if (!File.Exists(LayoutPath))
        {
            return new LayoutDocument();
        }

        try
        {
            string json = File.ReadAllText(LayoutPath);
            LayoutDocument? document = JsonSerializer.Deserialize(json, LayoutJsonContext.Default.LayoutDocument);
            return document ?? new LayoutDocument();
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or JsonException
                                              or NotSupportedException)
        {
            // 损坏的 layouts.json 不应影响启动；直接使用内置预设。
            return new LayoutDocument();
        }
    }

    private async Task PersistAsync(LayoutDocument document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        string json = JsonSerializer.Serialize(document, LayoutJsonContext.Default.LayoutDocument);
        string temp = LayoutPath + ".tmp";
        await File.WriteAllTextAsync(
                temp,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken)
            .ConfigureAwait(false);
        File.Move(temp, LayoutPath, overwrite: true);
    }

    private void TryPersist(LayoutDocument document)
    {
        try
        {
            Directory.CreateDirectory(_root);
            string json = JsonSerializer.Serialize(document, LayoutJsonContext.Default.LayoutDocument);
            string temp = LayoutPath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, LayoutPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or JsonException
                                              or NotSupportedException)
        {
            // Reset 是编辑器入口：写盘失败时保留内存回退，不影响当前页面。
        }
    }
}
