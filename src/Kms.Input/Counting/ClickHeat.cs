using Kms.Core.Model;

namespace Kms.Input.Counting;

/// <summary>
/// 屏幕点击热区采样（v3 <c>ClickHeat</c> 的采集部分）。
/// 钩子坐标是屏幕物理像素；32px 一格，向下取整，支持负坐标多屏。
/// </summary>
public static class ClickHeat
{
    /// <summary>采样格边长（屏幕物理像素）。</summary>
    public const int Cell = 32;

    /// <summary>位置明细保留天数。</summary>
    public const int KeepDays = 30;

    /// <summary>单日格子上限。</summary>
    public const int MaxCellsPerDay = 32_768;

    /// <summary>同一次按下的两条采集通路去重窗口。</summary>
    public const uint DedupeWindowMs = 30;

    private static long _lastTick;
    private static int _lastButton = -1;
    private static int _lastCellKey = int.MinValue;

    public static int CellOf(int screenX, int screenY, out int cellX, out int cellY)
    {
        cellX = (int)Math.Floor(screenX / (double)Cell);
        cellY = (int)Math.Floor(screenY / (double)Cell);
        return CellKey(cellX, cellY);
    }

    public static int CellKey(int cellX, int cellY)
    {
        int x = Math.Max(-32768, Math.Min(32767, cellX)) + 32768;
        int y = Math.Max(-32768, Math.Min(32767, cellY)) + 32768;
        return (x << 16) | y;
    }

    public static void SplitKey(int key, out int cellX, out int cellY)
    {
        cellX = ((key >> 16) & 0xFFFF) - 32768;
        cellY = (key & 0xFFFF) - 32768;
    }

    public static int ChannelOf(int button)
    {
        if (button is >= 1 and <= 3)
        {
            return button;
        }

        return button >= 4 ? 4 : 0;
    }

    public static long ChannelKey(int channel, int cellKey) =>
        ((long)channel << 32) | (uint)cellKey;

    /// <summary>记录一次点击；返回 false 表示被去重窗口吃掉。</summary>
    public static bool Record(DayRecord? day, int screenX, int screenY, int button)
    {
        if (day is null || button <= 0)
        {
            return false;
        }

        int key = CellOf(screenX, screenY, out _, out _);
        bool existing = day.ClickCells.TryGetValue(key, out long count);
        if (!existing && day.ClickCells.Count >= MaxCellsPerDay)
        {
            return false;
        }

        long now = Environment.TickCount;
        if (button == _lastButton && key == _lastCellKey && unchecked((uint)(now - _lastTick)) < DedupeWindowMs)
        {
            return false;
        }

        _lastTick = now;
        _lastButton = button;
        _lastCellKey = key;
        day.ClickCells[key] = count + 1;

        int channel = ChannelOf(button);
        long channelKey = ChannelKey(channel, key);
        day.ClickCellsV1[channelKey] = day.ClickCellsV1.GetValueOrDefault(channelKey) + 1;
        return true;
    }

    /// <summary>测试用：清掉去重状态。</summary>
    public static void ResetDedupe()
    {
        _lastTick = 0;
        _lastButton = -1;
        _lastCellKey = int.MinValue;
    }
}
