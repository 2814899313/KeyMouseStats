using Kms.Core.Model;

namespace Kms.App;

/// <summary>
/// 首启导入时把 v3 文本（当前月完整明细）与 V2Migrator 写出的 SQLite（含历史月）
/// 合并成一份完整的 v4 主存档。
///
/// 核心计数以 SQLite 为准（含 v3 月档里 v4 文本没有的旧日期）；
/// 按住/组合键/物理键位/点击格/应用键组等明细以文本为准（SQLite 13 表不保存逐键明细）。
/// </summary>
internal static class StatsSeedMerge
{
    public static void MergeDetail(StatsData target, StatsData source)
    {
        foreach ((string key, DayRecord day) in source.History)
        {
            if (!target.History.TryGetValue(key, out DayRecord? existing))
            {
                target.History[key] = day;
                continue;
            }

            CopyDetail(existing, day);
        }

        foreach ((string key, MonthArchive archive) in source.Archives)
        {
            target.Archives[key] = archive;
        }

        if (source.AppRules.Count > 0)
        {
            target.AppRules.Clear();
            target.AppRules.AddRange(source.AppRules);
        }

        foreach ((string key, string value) in source.Settings)
        {
            target.Settings[key] = value;
        }

        if (target.GlobalLines.Count == 0)
        {
            target.GlobalLines.AddRange(source.GlobalLines);
        }

        foreach (string line in source.UnknownGlobalLines)
        {
            if (!target.UnknownGlobalLines.Contains(line, StringComparer.Ordinal))
            {
                target.UnknownGlobalLines.Add(line);
            }
        }

        if (source.Keyboards.Count > 0)
        {
            target.Keyboards.Clear();
            target.Keyboards.AddRange(source.Keyboards);
        }

        if (source.Mice.Count > 0)
        {
            target.Mice.Clear();
            target.Mice.AddRange(source.Mice);
        }

        RecomputeTotals(target);
    }

    private static void CopyDetail(DayRecord target, DayRecord source)
    {
        target.ActiveSeconds = source.ActiveSeconds;
        target.IdleSeconds = source.IdleSeconds;
        target.AppObservedSeconds = source.AppObservedSeconds;
        target.AppSwitches = source.AppSwitches;
        target.PeakApm = source.PeakApm;
        Array.Copy(source.ActiveHours, target.ActiveHours, 24);

        target.Sessions.Clear();
        target.Sessions.AddRange(source.Sessions);
        target.IdlePeriods.Clear();
        target.IdlePeriods.AddRange(source.IdlePeriods);

        target.Holds.Clear();
        foreach ((int vk, KeyHold hold) in source.Holds)
        {
            target.Holds[vk] = new KeyHold
            {
                Count = hold.Count,
                TotalMs = hold.TotalMs,
                MaxMs = hold.MaxMs
            };
        }

        target.HoldCount = source.HoldCount;
        target.HoldDiscarded = source.HoldDiscarded;
        target.HoldTotalMs = source.HoldTotalMs;
        target.HoldMaxMs = source.HoldMaxMs;
        target.HoldActiveSeconds = source.HoldActiveSeconds;
        Array.Copy(source.HoldBuckets, target.HoldBuckets, target.HoldBuckets.Length);

        target.ComboCounts.Clear();
        foreach ((string combo, long count) in source.ComboCounts)
        {
            target.ComboCounts[combo] = count;
        }

        target.PhysicalKeys.Clear();
        foreach ((int id, long count) in source.PhysicalKeys)
        {
            target.PhysicalKeys[id] = count;
        }

        target.PhysicalVks.Clear();
        foreach ((int vk, long count) in source.PhysicalVks)
        {
            target.PhysicalVks[vk] = count;
        }

        target.ClickCells.Clear();
        foreach ((int cell, long count) in source.ClickCells)
        {
            target.ClickCells[cell] = count;
        }

        target.ClickCellsV1.Clear();
        foreach ((long cell, long count) in source.ClickCellsV1)
        {
            target.ClickCellsV1[cell] = count;
        }

        foreach ((string id, AppUsage app) in source.Apps)
        {
            target.Apps[id] = app;
        }

        if (source.MoveMeters > 0)
        {
            target.MoveMeters = source.MoveMeters;
        }

        CopyCross(target.Cross, source.Cross);
    }

    private static void CopyCross(CrossDay target, CrossDay source)
    {
        Array.Copy(source.ObservedHours, target.ObservedHours, 24);
        Array.Copy(source.Direction, target.Direction, 4);

        target.AppHours.Clear();
        foreach ((string path, double[] hours) in source.AppHours)
        {
            target.AppHours[path] = (double[])hours.Clone();
        }

        target.PathCounts = source.PathCounts;
        target.DragCounts = source.DragCounts;
        target.Packets = source.Packets;
        target.CalibratedPackets = source.CalibratedPackets;
        target.Flicks = source.Flicks;
        target.Reversals = source.Reversals;
        target.Clicks = source.Clicks;
        target.MoveClicks = source.MoveClicks;
    }

    /// <summary>按 v3 口径重算全量：日明细 + 月归档。</summary>
    public static void RecomputeTotals(StatsData data)
    {
        StatsTotals totals = data.Totals;
        totals.Keys = 0;
        totals.Clicks = 0;
        totals.Left = 0;
        totals.Right = 0;
        totals.Middle = 0;
        totals.XButtons = 0;
        totals.Wheel = 0;
        totals.HWheel = 0;
        totals.MovePx = 0;
        totals.MoveMeters = 0;
        totals.HoldCount = 0;
        totals.HoldDiscarded = 0;
        totals.HoldTotalMs = 0;
        totals.HoldMaxMs = 0;
        totals.HoldActiveSeconds = 0;

        foreach (DayRecord day in data.History.Values)
        {
            totals.Keys += day.Keys;
            totals.Clicks += day.Clicks;
            totals.Left += day.Left;
            totals.Right += day.Right;
            totals.Middle += day.Middle;
            totals.XButtons += day.XButtons;
            totals.Wheel += day.Wheel;
            totals.HWheel += day.HWheel;
            totals.MovePx += day.MovePx;
            totals.MoveMeters += day.MoveMeters;
            totals.HoldCount += day.HoldCount;
            totals.HoldDiscarded += day.HoldDiscarded;
            totals.HoldTotalMs += day.HoldTotalMs;
            totals.HoldActiveSeconds += day.HoldActiveSeconds;
            if (day.HoldMaxMs > totals.HoldMaxMs)
            {
                totals.HoldMaxMs = day.HoldMaxMs;
            }
        }

        foreach (MonthArchive archive in data.Archives.Values)
        {
            totals.Keys += archive.Keys;
            totals.Clicks += archive.Clicks;
            totals.Wheel += archive.Wheel;
            totals.HWheel += archive.HWheel;
            totals.MoveMeters += archive.MoveMeters;
            totals.HoldCount += archive.HoldCount;
            totals.HoldDiscarded += archive.HoldDiscarded;
            totals.HoldTotalMs += archive.HoldTotalMs;
            totals.HoldActiveSeconds += archive.HoldActiveSeconds;
            if (archive.HoldMaxMs > totals.HoldMaxMs)
            {
                totals.HoldMaxMs = archive.HoldMaxMs;
            }
        }
    }
}
