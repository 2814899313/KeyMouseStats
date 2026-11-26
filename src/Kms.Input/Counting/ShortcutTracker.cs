using System.Globalization;
using System.Text;
using Kms.Core.Model;

namespace Kms.Input.Counting;

/// <summary>
/// 虚拟键归一化 + 组合键识别（v3 <c>ShortcutTracker</c> 的移植）。
/// 用物理键位区分自动重复与「丢失抬起后的重新按下」。
/// </summary>
public sealed class ShortcutTracker
{
    /// <summary>同一物理键重复按下的间隔上限；超过只可能是上一次抬起被吞掉。</summary>
    public const double RePressGapMs = 1200;

    private readonly HashSet<int> _held = new();
    private readonly Dictionary<int, int> _physicalHeld = new();
    private readonly Dictionary<int, long> _physicalDownTicks = new();

    public static int Normalize(int virtualKey, uint scan, uint flags)
    {
        return virtualKey switch
        {
            16 => scan == 0x36 ? 0xA1 : 0xA0,
            17 => (flags & 1) != 0 ? 0xA3 : 0xA2,
            18 => (flags & 1) != 0 ? 0xA5 : 0xA4,
            _ => virtualKey
        };
    }

    public static bool IsModifier(int virtualKey) =>
        virtualKey is (>= 0xA0 and <= 0xA5) or 0x5B or 0x5C;

    /// <summary>
    /// 处理一次键盘事件。返回组合键标识（形如 <c>ctrl_shift_a</c>），
    /// <paramref name="firstPress"/> 表示是否应计入一次击键。
    /// </summary>
    public string? Process(bool down, int virtualKey, uint scan, uint flags, long ticks, out bool firstPress)
    {
        firstPress = false;
        if (virtualKey < 8 || virtualKey >= 255)
        {
            return null;
        }

        int vk = Normalize(virtualKey, scan, flags);
        int physical = KeyboardHeat.ScanId(vk, scan, flags);
        if (physical == 0)
        {
            physical = 0x10000 | vk;
        }

        if (!down)
        {
            if (_physicalHeld.TryGetValue(physical, out int original))
            {
                _physicalHeld.Remove(physical);
                vk = original;
            }

            _physicalDownTicks.Remove(physical);
            if (!_physicalHeld.ContainsValue(vk))
            {
                _held.Remove(vk);
            }

            return null;
        }

        if (_physicalHeld.ContainsKey(physical))
        {
            long lastDown = _physicalDownTicks.TryGetValue(physical, out long value) ? value : ticks;
            if ((ticks - lastDown) * 1000.0 / System.Diagnostics.Stopwatch.Frequency <= RePressGapMs)
            {
                // 自动重复：刷新活跃时间，不产生新击键，也不重置按住起点。
                if (ticks > lastDown)
                {
                    _physicalDownTicks[physical] = ticks;
                }

                return null;
            }

            // 距离远超自动重复：上一次抬起丢了，按全新按下重新起算。
            _physicalHeld.Remove(physical);
            _physicalDownTicks.Remove(physical);
        }

        _physicalHeld[physical] = vk;
        _physicalDownTicks[physical] = ticks;
        _held.Add(vk);
        firstPress = true;

        if (IsModifier(vk))
        {
            return null;
        }

        StringBuilder combo = new();
        if (_held.Contains(0xA2) || _held.Contains(0xA3))
        {
            combo.Append("ctrl_");
        }

        if (_held.Contains(0xA4) || _held.Contains(0xA5))
        {
            combo.Append("alt_");
        }

        if (_held.Contains(0x5B) || _held.Contains(0x5C))
        {
            combo.Append("win_");
        }

        if (_held.Contains(0xA0) || _held.Contains(0xA1))
        {
            combo.Append("shift_");
        }

        if (combo.Length == 0)
        {
            return null;
        }

        if (vk is >= 0x41 and <= 0x5A)
        {
            combo.Append(char.ToLowerInvariant((char)vk));
        }
        else if (vk is >= 0x30 and <= 0x39)
        {
            combo.Append((char)vk);
        }
        else if (vk == 9)
        {
            combo.Append("tab");
        }
        else
        {
            combo.Append("vk").Append(vk.ToString("x2", CultureInfo.InvariantCulture));
        }

        return combo.ToString();
    }
}

/// <summary>组合键标识的校验与累积。</summary>
public static class ShortcutStats
{
    public static bool IsValid(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 40)
        {
            return false;
        }

        string[] parts = key.Split('_');
        if (parts.Length is < 2 or > 5)
        {
            return false;
        }

        string[] modifiers = ["ctrl", "alt", "win", "shift"];
        int previous = -1;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            int index = Array.IndexOf(modifiers, parts[i]);
            if (index <= previous)
            {
                return false;
            }

            previous = index;
        }

        string last = parts[^1];
        if (last == "tab")
        {
            return true;
        }

        if (last.Length == 1 && (last[0] is >= 'a' and <= 'z' or >= '0' and <= '9'))
        {
            return true;
        }

        return last.Length == 4
            && last.StartsWith("vk", StringComparison.Ordinal)
            && int.TryParse(last[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int vk)
            && vk is >= 8 and < 255 and not 16 and not 17 and not 18
            && !ShortcutTracker.IsModifier(vk);
    }

    public static void Add(DayRecord day, string? key)
    {
        if (key is null)
        {
            return;
        }

        day.ComboCounts[key] = day.ComboCounts.GetValueOrDefault(key) + 1;
    }
}
