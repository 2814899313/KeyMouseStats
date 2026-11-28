using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Kms.App.Platform;

/// <summary>
/// 应用图标异步缓存。UI 只做内存命中查询；未命中时在后台线程提取 exe 原始像素，
/// 回到 UI 线程创建 Avalonia 位图，完成后写入 128 项 LRU。Render 热路径绝不读磁盘。
/// </summary>
internal sealed class AppIconCacheService
{
    public const int Capacity = 128;

    private sealed record Entry(string Key, string Path, Bitmap Bitmap);

    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _order = new();
    private readonly Dictionary<string, Task<Bitmap?>> _inflight = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _loadGate = new(2, 2);
    private readonly Func<string, AppIconPixels?> _extract;
    private readonly Func<AppIconPixels, Bitmap?> _createBitmap;
    private readonly bool _marshalToUiThread;

    internal AppIconCacheService(
        Func<string, AppIconPixels?>? extractor = null,
        Func<AppIconPixels, Bitmap?>? bitmapFactory = null,
        bool marshalToUiThread = true)
    {
        _extract = extractor ?? AppIconLoader.ExtractPixelsUncached;
        _createBitmap = bitmapFactory ?? AppIconLoader.CreateBitmap;
        _marshalToUiThread = marshalToUiThread;
    }

    public bool Enabled { get; set; } = true;

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

    /// <summary>只查内存；绝不触发磁盘读取。</summary>
    public Bitmap? TryGet(string? path)
    {
        string key = Key(path);
        if (key.Length == 0)
        {
            return null;
        }

        lock (_gate)
        {
            if (!_map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                return null;
            }

            _order.Remove(node);
            _order.AddFirst(node);
            return node.Value.Bitmap;
        }
    }

    public Task<Bitmap?> GetAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return Task.FromResult<Bitmap?>(null);
        }

        string trimmed = (path ?? "").Trim().Trim('"');
        string key = Key(trimmed);
        if (key.Length == 0)
        {
            return Task.FromResult<Bitmap?>(null);
        }

        lock (_gate)
        {
            if (_map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                return Task.FromResult<Bitmap?>(node.Value.Bitmap);
            }

            if (_inflight.TryGetValue(key, out Task<Bitmap?>? running))
            {
                return running;
            }

            Task<Bitmap?> task = LoadAsync(key, trimmed, cancellationToken);
            _inflight[key] = task;
            return task;
        }
    }

    public void Prefetch(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            _ = GetAsync(path);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _order.Clear();
            _inflight.Clear();
        }
    }

    private async Task<Bitmap?> LoadAsync(
        string key,
        string path,
        CancellationToken cancellationToken)
    {
        Bitmap? bitmap = null;
        AppIconPixels? pixels = null;
        try
        {
            await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                pixels = await Task.Run(
                    () => _extract(path),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _loadGate.Release();
            }

            if (pixels is { } extracted)
            {
                bitmap = _marshalToUiThread
                    ? await Dispatcher.UIThread.InvokeAsync(() => _createBitmap(extracted))
                    : _createBitmap(extracted);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Debug.WriteLine($"AppIconCacheService: 加载图标失败 {path}: {error}");
            bitmap = null;
        }

        lock (_gate)
        {
            _inflight.Remove(key);
            if (bitmap is null)
            {
                return null;
            }

            if (_map.TryGetValue(key, out LinkedListNode<Entry>? existing))
            {
                _order.Remove(existing);
                _map.Remove(key);
            }

            LinkedListNode<Entry> node = _order.AddFirst(new Entry(key, path, bitmap));
            _map[key] = node;
            while (_map.Count > Capacity && _order.Last is { } last)
            {
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }

        return bitmap;
    }

    private static string Key(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? ""
            : path.Trim().Trim('"').ToLowerInvariant();
}
