using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using KeyMouseStats;

/// <summary>
/// 屏幕点击热区:采样格 key 编解码、双通路去重、落盘、30 天裁剪、九宫格聚合。
/// 这些算错一个,用户看到的就是「热区位置整体偏了一格」或者「点了没记上」。
/// </summary>
internal static class ClickHeatTests
{
    private static int checks;

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception(what);
    }

    private static long SumHours(long[] values)
    {
        long total = 0;
        foreach (long value in values) total += value;
        return total;
    }

    private static long SumKeys(Dictionary<int, long> values)
    {
        long total = 0;
        foreach (long value in values.Values) total += value;
        return total;
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("ClickHeatTests", Run);
    }

    private static void Run()
    {
        // ---- 1. 采样格:向下取整,负坐标(主屏左边的显示器)也要正确 ----
        int cellX, cellY;
        ClickHeat.CellOf(0, 0, out cellX, out cellY);
        Check(cellX == 0 && cellY == 0, "origin cell is zero");
        ClickHeat.CellOf(31, 31, out cellX, out cellY);
        Check(cellX == 0 && cellY == 0, "cell floors within the first cell");
        ClickHeat.CellOf(32, -1, out cellX, out cellY);
        Check(cellX == 1 && cellY == -1, "cell boundary and negative coordinate floor");
        int[] samples = { -32768, -5000, -33, -1, 0, 1, 31, 32, 777, 32767 };
        foreach (int x in samples)
        {
            foreach (int y in samples)
            {
                int key = ClickHeat.CellKey(x, y);
                int rx, ry;
                ClickHeat.SplitKey(key, out rx, out ry);
                Check(rx == x && ry == y, "cell key round trip " + x + "," + y);
            }
        }
        Check(ClickHeat.CellKey(-40000, 0) == ClickHeat.CellKey(-32768, 0), "out-of-range cells are clamped");
        Check(ClickHeat.CellLeft(-2) == -ClickHeat.Cell * 2, "cell left edge is derived from the cell index");

        // ---- 2b. 鼠标动作多日聚合:方向、拖拽、移动后点击与样本天数 ----
        DayRecord motionDay = new DayRecord();
        motionDay.Cross.Direction[0] = 315;
        motionDay.Cross.Direction[1] = 304;
        motionDay.Cross.Direction[2] = 189;
        motionDay.Cross.Direction[3] = 192;
        motionDay.Cross.PathCounts = 1000;
        motionDay.Cross.DragCounts = 39;
        motionDay.Cross.Clicks = 1000;
        motionDay.Cross.MoveClicks = 871;
        motionDay.Cross.Packets = 483489;
        motionDay.Cross.CalibratedPackets = 480000;
        motionDay.Cross.Flicks = 6823;
        motionDay.Cross.Reversals = 439;
        MouseActionStats motion = MouseActionStats.Build(new[] { motionDay });
        Check(motion.Days == 1 && motion.Packets == 483489, "mouse action aggregates sample count");
        Check(Math.Abs(motion.DirectionShare(0) - 315.0 / 1000.0) < 1e-9, "direction shares use all four axes");
        Check(Math.Abs(motion.DragShare - 0.039) < 1e-9, "drag share uses path samples");
        Check(Math.Abs(motion.MoveClickShare - 0.871) < 1e-9, "move-click share uses clicks");
        Check(motion.HorizontalInsight().Contains("1.62"), "horizontal/vertical insight is derived from direction shares");

        // ---- 2c. 派生桶与日总数不一致时可自检修复（以 keys/clicks 为准） ----
        DayRecord brokenDay = new DayRecord();
        brokenDay.Keys = 100;
        brokenDay.Clicks = 20;
        brokenDay.HourKeys[9] = 70;
        brokenDay.HourKeys[10] = 30;
        brokenDay.KeyCounts[65] = 80;
        brokenDay.HourClicks[9] = 15;
        brokenDay.Left = 10;
        Check(brokenDay.RepairDerivedCounts(true), "an inconsistent day is detected");
        Check(SumHours(brokenDay.HourKeys) == 100 && SumKeys(brokenDay.KeyCounts) == 100,
            "key totals are normalized to the day total");
        Check(SumHours(brokenDay.HourClicks) == 20, "click hours are normalized to the day total");
        Check(brokenDay.Left + brokenDay.Right + brokenDay.Middle + brokenDay.XButtons == 20,
            "missing button detail is filled without changing total clicks");

        // ---- 2d. 同日重复节不应把 hk/kk 累加到后一节的 keys 上 ----
        Store.Parsed duplicated = Store.ParseText(
            "format=13\n[day=2026-11-14]\nkeys=10\nhk=1:10,2:0\n[day=2026-11-14]\nkeys=5\nhk=1:5,2:0\n");
        DayRecord duplicateDay;
        Check(duplicated.History.TryGetValue(new DateTime(2026, 11, 14), out duplicateDay), "duplicate day still parses");
        Check(duplicateDay.Keys == 10 && SumHours(duplicateDay.HourKeys) == 10,
            "duplicate day uses the first consistent section instead of mixing buckets");


        // ---- 2. 记录与双通路去重:同一次物理按下只算一次,不同键/不同格不合并 ----
        ClickHeat.ResetDedupe();
        DayRecord day = new DayRecord();
        Check(ClickHeat.Record(day, 100, 100, 1), "first click is recorded");
        Check(!ClickHeat.Record(day, 100, 100, 1), "the second path for the same press is ignored");
        Check(ClickHeat.Record(day, 100, 100, 2), "another button of the same press is a separate action");
        Check(ClickHeat.Record(day, 500, 100, 1), "another cell is a separate action");
        Check(day.ClickCells.Count == 2, "two cells are stored");
        ClickHeat.ResetDedupe();
        Check(ClickHeat.Record(day, 100, 100, 1), "a later click in the same cell counts again");
        long total = 0;
        foreach (KeyValuePair<int, long> pair in day.ClickCells) total += pair.Value;
        Check(total == 4, "counts accumulate per cell");
        ClickHeat.ResetDedupe();
        Check(!ClickHeat.Record(null, 10, 10, 1), "a null day is ignored");
        Check(!ClickHeat.Record(day, 10, 10, 0), "an unknown button is ignored");

        // ---- 3. 落盘编解码 ----
        DayRecord round = new DayRecord();
        round.ClickCells[ClickHeat.CellKey(-2, 3)] = 7;
        round.ClickCells[ClickHeat.CellKey(5, 0)] = 1;
        string encoded = ClickHeat.Encode(round.ClickCells);
        Check(encoded.Length > 0 && encoded.IndexOf(':') > 0, "cells encode as cx:cy:count");
        Check(encoded == ClickHeat.Encode(round.ClickCells), "encoding is deterministic");
        DayRecord decoded = new DayRecord();
        ClickHeat.Decode(encoded, decoded.ClickCells);
        Check(decoded.ClickCells.Count == 2, "both cells decode");
        Check(decoded.ClickCells[ClickHeat.CellKey(-2, 3)] == 7 && decoded.ClickCells[ClickHeat.CellKey(5, 0)] == 1,
            "decoded counts match");
        DayRecord bad = new DayRecord();
        ClickHeat.Decode("bad,1:2,3:4:0,-40000:1:5,9:9:3", bad.ClickCells);
        Check(bad.ClickCells.Count == 1 && bad.ClickCells[ClickHeat.CellKey(9, 9)] == 3, "malformed cells are skipped");
        ClickHeat.Decode("", bad.ClickCells);
        ClickHeat.Decode(null, null);
        Check(bad.ClickCells.Count == 1, "empty input does not touch existing cells");

        // ---- 3b. 4K/多屏容量:写入超过旧 4000 上限,保存重载后不能截断 ----
        DayRecord wide = new DayRecord();
        ClickHeat.ResetDedupe();
        for (int i = 0; i < 5000; i++)
        {
            int x = (i % 100) * ClickHeat.Cell;
            int y = (i / 100) * ClickHeat.Cell;
            Check(ClickHeat.Record(wide, x, y, 1), "大桌面点击应被记录: " + i);
        }
        Check(wide.ClickCells.Count == 5000, "写入端应保留 5000 个不同格子");
        DayRecord wideReloaded = new DayRecord();
        ClickHeat.Decode(ClickHeat.Encode(wide.ClickCells), wideReloaded.ClickCells);
        Check(wideReloaded.ClickCells.Count == 5000, "5000 格保存重载后必须完整保留");
        Check(ClickHeat.MaxCellsPerDay >= 5000, "容量上限必须覆盖 4K 常见桌面");

        // ---- 4. 九宫格聚合:范围外只计入总数,不参与区域 ----
        DayRecord sample = new DayRecord();
        sample.ClickCells[ClickHeat.CellKey(0, 0)] = 5;        // 屏幕 (10,10) → 左上
        sample.ClickCells[ClickHeat.CellKey(4, 3)] = 3;        // 屏幕 (145,110) → 正中
        sample.ClickCells[ClickHeat.CellKey(8, 5)] = 1;        // 屏幕 (272,176) → 右下
        sample.ClickCells[ClickHeat.CellKey(-4, 1)] = 2;       // 屏幕 (-120,50) → 画面外
        Rectangle bounds = new Rectangle(0, 0, 300, 200);
        ClickHeat.Aggregate aggregate = ClickHeat.Collect(new DayRecord[] { sample }, bounds);
        aggregate.ComputeRegions(bounds, bounds, new Rectangle[] { bounds });
        Check(aggregate.Total == 11 && aggregate.RegionTotal == 9 && aggregate.Outside == 2,
            "outside samples only count towards the total");
        Check(aggregate.Max == 5 && aggregate.MaxKey == ClickHeat.CellKey(0, 0), "the hottest cell is tracked");
        Check(aggregate.Days == 1 && aggregate.Cells.Count == 4, "aggregate keeps day and cell counts");
        Check(ClickHeat.HottestRegion(aggregate) == 0 && ClickHeat.RegionName(0) == "左上",
            "the hottest region is the top-left third");
        Check(aggregate.Regions[4] == 3 && aggregate.Regions[8] == 1, "middle and bottom-right thirds");
        Check(ClickHeat.HottestRegion(new ClickHeat.Aggregate()) == -1, "no data has no hottest region");
        Check(ClickHeat.RegionAt(0, 0, bounds) == 0, "region clamp at the top-left");
        Check(ClickHeat.RegionAt(299, 199, bounds) == 8, "region clamp at the bottom-right");
        Check(ClickHeat.RegionAt(50, 100, bounds) == 3 && ClickHeat.RegionAt(150, 100, bounds) == 4,
            "region columns are thirds of the width");
        Check(ClickHeat.RegionName(99) == "--", "unknown region index is safe");
        Check(ClickHeat.Collect(new DayRecord[0], bounds).Total == 0, "an empty range aggregates to zero");

        // 多屏:九宫格只算所选屏幕,其他屏幕只计入总点击数。
        DayRecord multi = new DayRecord();
        multi.ClickCells[ClickHeat.CellKey(4, 3)] = 4;      // 主屏 (145,110)
        multi.ClickCells[ClickHeat.CellKey(14, 3)] = 6;     // 副屏 (465,110)
        ClickHeat.Aggregate secondary = ClickHeat.Collect(new DayRecord[] { multi }, new Rectangle(0, 0, 600, 200));
        secondary.ComputeRegions(new Rectangle(0, 0, 600, 200), bounds, new Rectangle[] { bounds, new Rectangle(300, 0, 300, 200) });
        Check(secondary.Total == 10 && secondary.RegionTotal == 4 && secondary.OffRegion == 6,
            "secondary-screen clicks stay out of the nine-grid but inside the total");
        Check(secondary.Regions[4] == 4 && ClickHeat.HottestRegion(secondary) == 4,
            "the nine-grid uses the selected screen's thirds only");
        Check(secondary.RegionCells == 1, "selected-screen coverage counts cells, not clicks");

        // ---- 4b. 混合分辨率/混合 DPI 的多屏几何:2560×1600 主屏 + 3840×2160 副屏(中间留空隙) ----
        Rectangle desktop = new Rectangle(0, 0, 7680, 2160);
        Rectangle primary = new Rectangle(0, 0, 2560, 1600);
        Rectangle ultra = new Rectangle(3840, 0, 3840, 2160);
        Rectangle[] both = { primary, ultra };
        ClickHeat.Projection shape = ClickHeat.Fit(desktop, new RectangleF(10, 20, 800, 300));
        Check(Math.Abs(shape.Scale - 800f / 7680f) < 1e-6, "fit uses the tighter axis for a 32:9 desktop");
        RectangleF primaryRect = shape.ToContent(primary);
        RectangleF ultraRect = shape.ToContent(ultra);
        Check(Math.Abs(primaryRect.Width - 2560 * 800f / 7680f) < 0.01 && Math.Abs(primaryRect.Height - 1600 * 800f / 7680f) < 0.01,
            "different resolutions keep their true relative size");
        Check(ultraRect.Left > primaryRect.Right, "the physical gap between screens is preserved");
        Check(Math.Abs(ultraRect.Top - primaryRect.Top) < 0.01, "screens sharing the top edge stay aligned");
        Check(shape.ToContent(2560, 0).X < shape.ToContent(3840, 0).X, "gap maps between the two screens");
        // 每块屏的角与中心:投影再反投影都应回到原像素(±1)。
        foreach (Rectangle screen in both)
        {
            int[] xs = { screen.Left, screen.Left + 1, screen.Left + screen.Width / 2, screen.Right - 1 };
            int[] ys = { screen.Top, screen.Top + 1, screen.Top + screen.Height / 2, screen.Bottom - 1 };
            foreach (int x in xs)
                foreach (int y in ys)
                {
                    PointF content = shape.ToContent(x, y);
                    int rx, ry;
                    Check(shape.TryUnproject(content, out rx, out ry), "projected point stays inside the canvas");
                    Check(Math.Abs(rx - x) <= 1 && Math.Abs(ry - y) <= 1, "screen " + x + "," + y + " round trips through the projection");
                }
        }
        int gapX = 3000, gapY = 100;
        Check(ClickHeat.OnAnyScreen(100, 100, both) && !ClickHeat.OnAnyScreen(gapX, gapY, both),
            "the mixed-DPI gap is not on any screen");
        // 画布里的空隙点:投影出来是有效画布坐标,但不属于任何屏幕,也不是九宫格样本。
        PointF gapContent = shape.ToContent(gapX, gapY);
        int gx, gy;
        Check(shape.TryUnproject(gapContent, out gx, out gy) && Math.Abs(gx - gapX) <= 1 && Math.Abs(gy - gapY) <= 1,
            "gap unprojects within a pixel");
        DayRecord gapDay = new DayRecord();
        gapDay.ClickCells[ClickHeat.CellKey(3, 3)] = 2;     // 主屏
        gapDay.ClickCells[ClickHeat.CellKey(gapX / ClickHeat.Cell, gapY / ClickHeat.Cell)] = 5; // 空隙里(布局变过才会出现)
        ClickHeat.Aggregate gapAggregate = ClickHeat.Collect(new DayRecord[] { gapDay }, desktop);
        gapAggregate.ComputeRegions(desktop, primary, both);
        Check(gapAggregate.Total == 7 && gapAggregate.RegionTotal == 2 && gapAggregate.Vacant == 5,
            "clicks in the gap are reported separately and kept out of the nine-grid");
        // 副屏作为选中屏幕时,九宫格换到副屏的三分格上。
        ClickHeat.Aggregate onSecondary = ClickHeat.Collect(new DayRecord[] { gapDay }, desktop);
        onSecondary.ComputeRegions(desktop, ultra, both);
        Check(onSecondary.RegionTotal == 0 && onSecondary.OffRegion == 2 && onSecondary.RegionArea == ultra,
            "selecting the second screen moves the nine-grid to it");
        // 横屏 / 竖屏混排(负坐标、旋转屏):投影不关心方向,只做等比缩放。
        Rectangle portrait = new Rectangle(-1080, 0, 1080, 1920);
        Rectangle landscapeScreen = new Rectangle(0, 320, 2560, 1440);
        Rectangle rotated = new Rectangle(-1080, 0, 3000, 1920);
        ClickHeat.Projection rotatedProjection = ClickHeat.Fit(rotated, new RectangleF(0, 0, 600, 300));
        Check(rotatedProjection.ToContent(portrait).Left < rotatedProjection.ToContent(landscapeScreen).Left,
            "a portrait screen left of the primary keeps its negative origin");
        Check(rotatedProjection.ToContent(portrait).Height > rotatedProjection.ToContent(landscapeScreen).Height,
            "a rotated screen is taller than the landscape one");

        // ---- 5. 30 天裁剪:更早的清位置,今天与边界日保留 ----
        Dictionary<DateTime, DayRecord> history = new Dictionary<DateTime, DayRecord>();
        DayRecord tooOld = new DayRecord();
        tooOld.Clicks = 9;
        tooOld.ClickCells[ClickHeat.CellKey(1, 1)] = 1;
        DayRecord edge = new DayRecord();
        edge.ClickCells[ClickHeat.CellKey(2, 2)] = 1;
        DayRecord today = new DayRecord();
        today.ClickCells[ClickHeat.CellKey(3, 3)] = 1;
        history[DateTime.Today.AddDays(-ClickHeat.KeepDays)] = tooOld;
        history[DateTime.Today.AddDays(-(ClickHeat.KeepDays - 1))] = edge;
        history[DateTime.Today] = today;
        int cleared = ClickHeat.Prune(history);
        Check(cleared == 1, "only expired samples are pruned");
        Check(tooOld.ClickCells.Count == 0 && tooOld.Clicks == 9, "expired samples are cleared, totals stay");
        Check(edge.ClickCells.Count == 1 && today.ClickCells.Count == 1, "boundary and today survive");
        Check(ClickHeat.Prune(null) == 0, "null history is safe");

        // ---- 6. 导入合并:同一个格子相加,其余保留 ----
        DayRecord left = new DayRecord();
        left.ClickCells[ClickHeat.CellKey(1, 1)] = 2;
        left.ClickCells[ClickHeat.CellKey(2, 2)] = 1;
        DayRecord right = new DayRecord();
        right.ClickCells[ClickHeat.CellKey(1, 1)] = 3;
        right.ClickCells[ClickHeat.CellKey(3, 3)] = 4;
        left.AddFrom(right);
        Check(left.ClickCells[ClickHeat.CellKey(1, 1)] == 5, "merged cells add up");
        Check(left.ClickCells[ClickHeat.CellKey(2, 2)] == 1 && left.ClickCells[ClickHeat.CellKey(3, 3)] == 4,
            "unshared cells are preserved");

        // ---- 7. stats.txt 往返:format=12、ch= 行、按天恢复 ----
        string dir = Path.Combine(Path.GetTempPath(), "ClickHeatTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Store.DataDirectory = dir;
        try
        {
            Store.History.Clear();
            Store.Total = new Counters();
            Store.RollDay(DateTime.Today);
            Store.Today.Clicks = 4;
            Store.Today.ClickCells[ClickHeat.CellKey(10, 5)] = 3;
            Store.Save();
            string text = File.ReadAllText(Path.Combine(dir, "stats.txt"));
            Check(text.IndexOf("format=13", StringComparison.Ordinal) >= 0, "the data file is written as v13");
            Check(text.IndexOf("ch=", StringComparison.Ordinal) >= 0, "click heat is written as a ch= line");
            Store.History.Clear();
            Store.Today = new DayRecord();
            Store.Load();
            Check(Store.Today.ClickCells.Count == 1 && Store.Today.ClickCells[ClickHeat.CellKey(10, 5)] == 3,
                "click heat round trips through stats.txt");
            Check(Store.DetectedFormat == 13, "the format version is detected");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        // ---- 8. 旧文件没有 ch= 行也能读,不当成损坏 ----
        Store.Parsed legacy = Store.ParseText("format=11\n[day=2026-01-02]\nclicks=5\n");
        DayRecord legacyDay;
        Check(legacy.History.TryGetValue(new DateTime(2026, 1, 2), out legacyDay), "legacy day parses");
        Check(legacyDay.Clicks == 5 && legacyDay.ClickCells.Count == 0, "legacy day has no click samples");
        Check(legacy.FormatVersion == 11, "legacy format version is preserved");

        Console.WriteLine("Click heatmap: " + checks + " checks passed");
    }
}
