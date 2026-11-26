using Kms.Core.Model;

namespace Kms.Input.Counting;

/// <summary>键盘物理键位统计（v3 <c>KeyboardHeat</c> 的采集部分）。</summary>
public static class KeyboardHeat
{
    /// <summary>
    /// 物理键位 id：扫描码低 8 位 + Extended 位；Pause 使用固定 id。
    /// 返回 0 表示没有可用扫描码（虚拟键输入等）。
    /// </summary>
    public static int ScanId(int virtualKey, uint scan, uint flags)
    {
        return scan == 0
            ? 0
            : virtualKey == 19
                ? 0x245
                : (int)(scan & 255) | ((flags & 1) != 0 ? 256 : 0);
    }

    public static void Record(DayRecord day, int virtualKey, uint scan, uint flags)
    {
        int id = ScanId(virtualKey, scan, flags);
        if (id == 0)
        {
            return;
        }

        day.PhysicalKeys[id] = day.PhysicalKeys.GetValueOrDefault(id) + 1;
        day.PhysicalVks[virtualKey] = day.PhysicalVks.GetValueOrDefault(virtualKey) + 1;
    }
}
