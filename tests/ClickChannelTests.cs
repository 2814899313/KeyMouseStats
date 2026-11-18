// ============================================================================
//  tests/ClickChannelTests.cs  ·  v3.0 Milestone 1 / Step 5
//
//  验证三通道点击热区:
//   1. Record 同时维护 ClickCells(全通道汇总)与 ClickCellsV1(分通道),签名不变;
//   2. chv1 编解码往返;sum(chv1) ≡ sum(ch) 恒等;
//   3. 旧档只有 ch=(无 chv1)时整格映射到 channel = 0,绝不伪造归入左键;
//   4. 四档通道聚合筛选;channel=0 的逐格数值与全通道汇总 100% 一致;
//   5. chv1 与 ch 不自洽时退回 ch,避免把损坏数据当真;
//   6. 走真实 Store.ParseText 的端到端往返。
//
//  纯托管用例,不写任何文件,不触碰任何用户目录。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using KeyMouseStats;

internal static class ClickChannelTests
{
    private static int _checks;

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }

    private static void Equal(long actual, long expected, string name)
    {
        Check(actual == expected, name + "(期望 " + expected + ",实际 " + actual + ")");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("ClickChannelTests", Run);
    }

    private static void Run()
    {
        RecordMaintainsBothViews();
        CodecRoundTrip();
        LegacyArchiveMapsToChannelZero();
        ChannelFiltering();
        FullViewMatchesFlatExactly();
        CorruptChannelDataFallsBack();
        ParseTextEndToEnd();
        Console.WriteLine("ClickChannelTests: " + _checks + " 项断言通过");
    }

    /// <summary>同一格点四次不同按键:汇总 4 次,分通道各 1 次。</summary>
    private static void RecordMaintainsBothViews()
    {
        DayRecord day = new DayRecord();
        int[] buttons = new int[] { 1, 2, 3, 4 };
        for (int i = 0; i < buttons.Length; i++)
        {
            ClickHeat.ResetDedupe();
            Check(ClickHeat.Record(day, 100, 200, buttons[i]), "第 " + i + " 次点击应被记录");
        }

        int cellX, cellY;
        int cellKey = ClickHeat.CellOf(100, 200, out cellX, out cellY);
        Equal(day.ClickCells[cellKey], 4, "全通道汇总应累计 4 次");
        Equal(day.ClickCellsV1.Count, 4, "四个按键应各占一个通道桶");

        for (int channel = 1; channel <= 4; channel++)
        {
            long key = ClickHeat.ChannelKey(channel, cellKey);
            Check(day.ClickCellsV1.ContainsKey(key), "通道 " + channel + " 应有记录");
            Equal(day.ClickCellsV1[key], 1, "通道 " + channel + " 应记 1 次");
        }

        Equal(SumChanneled(day), SumFlat(day), "sum(chv1) 必须等于 sum(ch)");
        Check(ClickHeat.InvariantHolds(day), "新写入的数据必须自洽");
    }

    /// <summary>编解码往返。</summary>
    private static void CodecRoundTrip()
    {
        Dictionary<long, long> source = new Dictionary<long, long>();
        source[ClickHeat.ChannelKey(1, ClickHeat.CellKey(3, 6))] = 7;
        source[ClickHeat.ChannelKey(2, ClickHeat.CellKey(3, 6))] = 2;
        source[ClickHeat.ChannelKey(3, ClickHeat.CellKey(-4, -5))] = 9;
        source[ClickHeat.ChannelKey(0, ClickHeat.CellKey(10, 11))] = 1;

        string text = ClickHeat.EncodeV1(source);
        Check(text.Length > 0, "编码不应为空");
        Check(text.IndexOf("1:3:6:7", StringComparison.Ordinal) >= 0, "编码应含 channel:cx:cy:count,实际=" + text);
        Check(text.IndexOf("3:-4:-5:9", StringComparison.Ordinal) >= 0, "负坐标应可编码,实际=" + text);

        Dictionary<long, long> restored = new Dictionary<long, long>();
        ClickHeat.DecodeV1(text, restored);

        Equal(restored.Count, source.Count, "往返后条目数应一致");
        foreach (KeyValuePair<long, long> pair in source)
        {
            Check(restored.ContainsKey(pair.Key), "往返应保留键 " + pair.Key);
            Equal(restored[pair.Key], pair.Value, "往返应保留计数");
        }

        // 非法输入:字段数不对 / 通道越界 / 计数非正,全部忽略。
        Dictionary<long, long> junk = new Dictionary<long, long>();
        ClickHeat.DecodeV1("9:1:2:3,1:2:3,1:1:1:0,x:y:z:w", junk);
        Equal(junk.Count, 0, "非法 chv1 片段应被全部忽略");
    }

    /// <summary>旧档只有 ch=:整格进 channel 0,分通道查询为空并带标记。</summary>
    private static void LegacyArchiveMapsToChannelZero()
    {
        DayRecord legacy = new DayRecord();
        legacy.ClickCells[ClickHeat.CellKey(3, 6)] = 5;
        legacy.ClickCells[ClickHeat.CellKey(4, 7)] = 2;
        Check(!ClickHeat.InvariantHolds(legacy), "没有 chv1 的历史日不应判为自洽");

        ClickHeat.Aggregate all = ClickHeat.Collect(new DayRecord[] { legacy }, Rectangle.Empty, 0);
        Equal(all.Total, 7, "全部点击视图应看到全部历史点击");
        Equal(all.Cells.Count, 2, "全部点击视图应有两格");

        ClickHeat.Aggregate left = ClickHeat.Collect(new DayRecord[] { legacy }, Rectangle.Empty, 1);
        Equal(left.Total, 0, "左键视图不得吞掉历史数据");
        Equal(left.Cells.Count, 0, "左键视图应为空集");
        Check(left.HasLegacyUnclassified, "应标记存在未区分按键的历史数据");
        Equal(left.LegacyTotal, 7, "标记应带出历史点击数");
    }

    /// <summary>四档通道筛选。</summary>
    private static void ChannelFiltering()
    {
        DayRecord day = BuildMixedDay();

        Equal(ClickHeat.Collect(new DayRecord[] { day }, Rectangle.Empty, 1).Total, 3, "左键合计");
        Equal(ClickHeat.Collect(new DayRecord[] { day }, Rectangle.Empty, 2).Total, 2, "右键合计");
        Equal(ClickHeat.Collect(new DayRecord[] { day }, Rectangle.Empty, 3).Total, 1, "中键合计");
        Equal(ClickHeat.Collect(new DayRecord[] { day }, Rectangle.Empty, 4).Total, 4, "侧键合计");
        Equal(ClickHeat.Collect(new DayRecord[] { day }, Rectangle.Empty, 0).Total, 10, "全部合计");

        ClickHeat.Aggregate left = ClickHeat.Collect(new DayRecord[] { day }, Rectangle.Empty, 1);
        Check(!left.HasLegacyUnclassified, "纯新格式数据不应带 legacy 标记");
        Equal(left.LegacyTotal, 0, "纯新格式不应有历史通道计数");
    }

    /// <summary>channel=0 的逐格数值必须与全通道汇总完全一致。</summary>
    private static void FullViewMatchesFlatExactly()
    {
        DayRecord day = BuildMixedDay();

        ClickHeat.Aggregate all = ClickHeat.Collect(new DayRecord[] { day }, Rectangle.Empty, 0);
        Equal(all.Cells.Count, day.ClickCells.Count, "格数应与全通道汇总一致");
        foreach (KeyValuePair<int, long> pair in day.ClickCells)
        {
            long actual;
            Check(all.Cells.TryGetValue(pair.Key, out actual), "全部视图应包含格子 " + pair.Key);
            Equal(actual, pair.Value, "格子 " + pair.Key + " 的数值必须与 v2.4.0 逐格一致");
        }
        Equal(all.Total, SumFlat(day), "全部视图总数应等于全通道汇总");
    }

    /// <summary>chv1 与 ch 不自洽时退回 ch(权威总数),不采信损坏的分通道数据。</summary>
    private static void CorruptChannelDataFallsBack()
    {
        DayRecord day = BuildMixedDay();
        // 人为把汇总表改大,制造不一致。
        day.ClickCells[ClickHeat.CellKey(9, 9)] = 99;
        Check(!ClickHeat.InvariantHolds(day), "人为破坏后应判为不自洽");

        ClickHeat.Aggregate all = ClickHeat.Collect(new DayRecord[] { day }, Rectangle.Empty, 0);
        Equal(all.Total, SumFlat(day), "不自洽时应以 ch= 为准");
        long cellValue;
        Check(all.Cells.TryGetValue(ClickHeat.CellKey(9, 9), out cellValue), "退回 ch 后应包含被改大的格子");
        Equal(cellValue, 99, "退回 ch 后应使用 ch 的数值");

        ClickHeat.Aggregate left = ClickHeat.Collect(new DayRecord[] { day }, Rectangle.Empty, 1);
        Check(left.HasLegacyUnclassified, "退回 ch 后分通道查询应带 legacy 标记");
    }

    /// <summary>端到端:真实解析器读 ch= 与 chv1=。</summary>
    private static void ParseTextEndToEnd()
    {
        string text = string.Join("\n", new string[]
        {
            "format=13",
            "date=2023-11-13",
            "[day=2023-11-13]",
            "keys=4",
            "ch=3:6:4",
            "chv1=1:3:6:1,2:3:6:1,3:3:6:1,4:3:6:1"
        });

        Store.Parsed parsed = Store.ParseText(text);
        DateTime day = new DateTime(2023, 11, 13);
        Check(parsed.History.ContainsKey(day), "应解析出 2023-11-13");

        DayRecord record = parsed.History[day];
        Equal(record.ClickCells[ClickHeat.CellKey(3, 6)], 4, "ch= 应被解析");
        Equal(record.ClickCellsV1.Count, 4, "chv1= 应被解析成四个通道桶");
        Check(ClickHeat.InvariantHolds(record), "解析出的数据应自洽");

        ClickHeat.Aggregate left = ClickHeat.Collect(new DayRecord[] { record }, Rectangle.Empty, 1);
        Equal(left.Total, 1, "解析后左键视图应为 1");

        // 旧档(只有 ch=)解析后不应产生 chv1 桶。
        Store.Parsed legacyParsed = Store.ParseText(string.Join("\n", new string[]
        {
            "format=13",
            "[day=2023-11-12]",
            "ch=3:6:4"
        }));
        DayRecord legacy = legacyParsed.History[new DateTime(2023, 11, 12)];
        Equal(legacy.ClickCellsV1.Count, 0, "旧档不应凭空产生 chv1");
        Check(!ClickHeat.InvariantHolds(legacy), "旧档不应判为自洽");
    }

    // ---------------------------------------------------------------- 工具

    /// <summary>3 左 + 2 右 + 1 中 + 4 侧,分布在不同格子。</summary>
    private static DayRecord BuildMixedDay()
    {
        DayRecord day = new DayRecord();
        for (int i = 0; i < 3; i++) Add(day, 10 + i * 40, 10, 1);
        for (int i = 0; i < 2; i++) Add(day, 200 + i * 40, 300, 2);
        Add(day, 400, 400, 3);
        for (int i = 0; i < 4; i++) Add(day, 600 + i * 40, 500, 4);
        return day;
    }

    private static void Add(DayRecord day, int x, int y, int button)
    {
        ClickHeat.ResetDedupe();
        ClickHeat.Record(day, x, y, button);
    }

    private static long SumFlat(DayRecord day)
    {
        long total = 0;
        foreach (KeyValuePair<int, long> pair in day.ClickCells) total += pair.Value;
        return total;
    }

    private static long SumChanneled(DayRecord day)
    {
        long total = 0;
        foreach (KeyValuePair<long, long> pair in day.ClickCellsV1) total += pair.Value;
        return total;
    }
}
