// ============================================================================
//  键鼠统计 - 按键时长与应用×键位报告页  PressReport.cs
//
//  1.7.3 新增两页:
//    · 按键时长:8 档直方图 + 每键平均时长排行 + 覆盖率与丢弃样本。
//    · 应用 × 键位:应用行 × 六类键位分组的构成热图 + 明细。
//
//  口径:按住时长不是按压力度;丢 UP 与超长按会被丢弃,因此必须显示覆盖率。
//  应用归因本身有误差,未归因的击键单列,不按比例分摊。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace KeyMouseStats
{
    /// <summary>按键时长报告。</summary>
    internal sealed class HoldReportData
    {
        public const string Note = "按键时长 = 首次按下到抬起的间隔,由单调时钟测量;系统的自动重复不会重置起点。\n超过 60 秒的按住视为挂机或丢失抬起,直接丢弃并计入丢弃数;锁屏、休眠与退出时未抬起的按键同样丢弃。按住时长不是按压力度——普通键盘没有压力感应。\n覆盖率 = 有效样本 ÷ 该区间击键数。丢 UP 是常态(Alt+Tab、Win 键、游戏吞键),覆盖率偏低说明样本偏少,结论要谨慎。";

        public string Caption, Footer;
        public string[] Cards = new string[4], CardValues = new string[4];
        public string[] Headings = { "按键", "平均时长", "次数", "最长", "占样本" };
        public readonly List<string[]> Rows = new List<string[]>();
        public readonly long[] Buckets = new long[HoldTracker.BucketCount];
        public readonly List<KeyValuePair<int, KeyHold>> TopKeys = new List<KeyValuePair<int, KeyHold>>();
        public long Samples, Discarded, Keys;
        public double TotalMs, MaxMs;
        public int Days, Observed;

        private static string Milliseconds(double value)
        {
            if (double.IsNaN(value)) return "--";
            return value < 1000
                ? value.ToString("0", CultureInfo.InvariantCulture) + " ms"
                : (value / 1000).ToString("0.##", CultureInfo.InvariantCulture) + " s";
        }

        public static HoldReportData Build(DateTime start, DateTime end)
        {
            HoldReportData data = new HoldReportData();
            RangeMetrics range = RangeStats.Of(start, end, true);   // 累积类页面:把进行中的今天算进来
            data.Days = range.Days;
            data.Observed = range.Observed;
            data.Keys = range.Keys;

            Dictionary<int, KeyHold> byKey = new Dictionary<int, KeyHold>();
            DateTime from, to;
            if (RangeRules.TryNormalize(start, end, true, out from, out to))
            {
                for (DateTime date = from; date <= to; date = date.AddDays(1))
                {
                    DayRecord day = Analysis.GetDay(date);
                    if (day == null || day.IsEmpty) continue;
                    data.Samples += day.HoldCount;
                    data.Discarded += day.HoldDiscarded;
                    data.TotalMs += day.HoldTotalMs;
                    if (day.HoldMaxMs > data.MaxMs) data.MaxMs = day.HoldMaxMs;
                    for (int i = 0; i < data.Buckets.Length && i < day.HoldBuckets.Length; i++) data.Buckets[i] += day.HoldBuckets[i];
                    foreach (KeyValuePair<int, KeyHold> pair in day.Holds)
                    {
                        KeyHold hold;
                        if (!byKey.TryGetValue(pair.Key, out hold)) { hold = new KeyHold(); byKey[pair.Key] = hold; }
                        hold.Count += pair.Value.Count;
                        hold.TotalMs += pair.Value.TotalMs;
                        if (pair.Value.MaxMs > hold.MaxMs) hold.MaxMs = pair.Value.MaxMs;
                    }
                }
            }

            List<KeyValuePair<int, KeyHold>> ranked = new List<KeyValuePair<int, KeyHold>>(byKey);
            ranked.Sort(delegate(KeyValuePair<int, KeyHold> a, KeyValuePair<int, KeyHold> b)
            {
                int order = b.Value.Mean.CompareTo(a.Value.Mean);
                return order != 0 ? order : string.Compare(Analysis.KeyName(a.Key), Analysis.KeyName(b.Key), StringComparison.Ordinal);
            });
            for (int i = 0; i < ranked.Count && i < 8; i++)
                if (ranked[i].Value.Count >= 10) data.TopKeys.Add(ranked[i]);

            for (int i = 0; i < ranked.Count; i++)
            {
                if (ranked[i].Value.Count < 5) continue;
                data.Rows.Add(new string[] { Analysis.KeyName(ranked[i].Key), Milliseconds(ranked[i].Value.Mean),
                    ranked[i].Value.Count.ToString(CultureInfo.InvariantCulture), Milliseconds(ranked[i].Value.MaxMs),
                    data.Samples > 0 ? (ranked[i].Value.Count * 100.0 / data.Samples).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "--" });
            }
            if (data.Rows.Count == 0) data.Rows.Add(new string[] { "暂无有效样本", "--", "0", "--", "--" });

            double coverage = data.Keys > 0 ? (double)data.Samples / data.Keys : double.NaN;
            double mean = data.Samples > 0 ? data.TotalMs / data.Samples : double.NaN;
            data.Caption = range.Days == 0 ? "所选区间没有完整日"
                : range.Start.ToString("MM.dd") + "—" + range.End.ToString("MM.dd") + " · " + range.Days + " 天" + (range.End == DateTime.Today ? "（含今日，进行中）" : "") + " · 按住时长分布与逐键排行";
            data.Cards = new string[] { "有效样本", "平均时长", "最长一次", "覆盖率" };
            data.CardValues = new string[] { data.Samples.ToString("N0", CultureInfo.InvariantCulture),
                Milliseconds(mean), Milliseconds(data.MaxMs),
                RangeMath.IsNumber(coverage) && coverage > 0 ? (coverage * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "--" };
            data.Footer = data.Samples == 0
                ? "区间内没有按键时长样本。该维度从 1.7.3 起采集,更早的日期不会有数据;如果今天已经在打字,样本会在几秒内出现。"
                : "有效样本 " + data.Samples.ToString("N0", CultureInfo.InvariantCulture) + " · 丢弃 " + data.Discarded.ToString("N0", CultureInfo.InvariantCulture)
                    + "（丢失抬起 / 超过 60 秒）· 覆盖率按该区间击键数计算。";
            return data;
        }
    }

    /// <summary>按键时长图表:左为八档直方图,右为每键平均时长排行。</summary>
    internal sealed class HoldReportVisual : Control
    {
        internal float UiScale;
        private readonly HoldReportData data;
        private readonly List<KeyValuePair<RectangleF, string>> targets = new List<KeyValuePair<RectangleF, string>>();
        private readonly ToolTip tip = new ToolTip { InitialDelay = 250, ReshowDelay = 100, AutoPopDelay = 20000 };
        private int hover = -1;

        public HoldReportVisual(DateTime start, DateTime end)
        {
            data = HoldReportData.Build(start, end);
            DoubleBuffered = true; ResizeRedraw = true;
            MouseMove += delegate(object sender, MouseEventArgs e)
            {
                float scale = UiScale > 0 ? UiScale : 1f;
                PointF point = new PointF(e.X / scale, e.Y / scale);
                int next = -1;
                for (int i = 0; i < targets.Count; i++) if (targets[i].Key.Contains(point)) { next = i; break; }
                if (next != hover) { hover = next; tip.SetToolTip(this, next >= 0 ? targets[next].Value : ""); Invalidate(); }
            };
            MouseLeave += delegate { hover = -1; Invalidate(); };
        }

        public void RenderTo(Graphics g, float scale)
        {
            float previous = UiScale; UiScale = scale;
            try
            {
                using (PaintEventArgs args = new PaintEventArgs(g, new Rectangle(0, 0,
                    Math.Max(1, (int)(ClientSize.Width * scale)), Math.Max(1, (int)(ClientSize.Height * scale))))) OnPaint(args);
            }
            finally { UiScale = previous; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }

        private static void TextAt(Graphics g, string text, Font font, Color color, RectangleF rect)
        {
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text ?? "", font, brush, rect, format);
        }
        private static void Fill(Graphics g, Color color, RectangleF rect)
        {
            if (rect.Width > 0 && rect.Height > 0) using (SolidBrush brush = new SolidBrush(color)) g.FillRectangle(brush, rect);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float scale = UiScale > 0 ? UiScale : g.DpiX / 96f;
            g.ScaleTransform(scale, scale);
            float w = ClientSize.Width / scale;
            ArtTheme t = ArtTheme.Current;
            g.Clear(t.Background); g.SmoothingMode = SmoothingMode.AntiAlias; targets.Clear();

            using (Font small = new Font("Microsoft YaHei UI", 11, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font body = new Font("Microsoft YaHei UI", 12, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font large = new Font("Segoe UI", 23, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                float card = (w - 44) / 4;
                for (int i = 0; i < 4; i++)
                {
                    float x = 10 + i * (card + 8);
                    ReportDesign.Surface(g, new RectangleF(x, 10, card, 67), t.Card, t.Line);
                    TextAt(g, data.Cards[i], small, t.Muted, new RectangleF(x + 12, 17, card - 24, 20));
                    TextAt(g, data.CardValues[i], data.CardValues[i].Length > 10 ? body : large, t.Text, new RectangleF(x + 12, 37, card - 24, 32));
                }
                TextAt(g, data.Caption, body, t.Text, new RectangleF(14, 86, w - 28, 23));

                // ---- 左:八档直方图 ----
                float half = (w - 44) / 2f;
                TextAt(g, "按住时长分布", small, t.Text, new RectangleF(14, 114, half, 18));
                long maximum = 1;
                foreach (long value in data.Buckets) maximum = Math.Max(maximum, value);
                float histogramTop = 140, histogramBottom = 250, histogramLeft = 24;
                float step = (half - 20) / data.Buckets.Length;
                if (data.Samples == 0) TextAt(g, "暂无样本", small, t.Muted, new RectangleF(24, 170, half, 20));
                for (int i = 0; i < data.Buckets.Length; i++)
                {
                    float height = (float)data.Buckets[i] / maximum * (histogramBottom - histogramTop);
                    RectangleF bar = new RectangleF(histogramLeft + i * step + 1, histogramBottom - height, Math.Max(2, step - 4), height);
                    Fill(g, ArtTheme.Mix(t.Card, t.Accent, .55), bar);
                    TextAt(g, HoldTracker.BucketLabel(i).Replace(" ms", "").Replace(" s", "s"), small, t.Muted,
                        new RectangleF(histogramLeft + i * step - 10, histogramBottom + 2, step + 20, 18));
                    targets.Add(new KeyValuePair<RectangleF, string>(new RectangleF(histogramLeft + i * step, histogramTop, step, histogramBottom - histogramTop),
                        HoldTracker.BucketLabel(i) + " · " + data.Buckets[i] + " 次"));
                }
                using (Pen axis = new Pen(t.Line)) g.DrawLine(axis, histogramLeft, histogramBottom, histogramLeft + half - 20, histogramBottom);
                TextAt(g, "最大 " + maximum + " 次", small, t.Muted, new RectangleF(24, 268, half, 18));

                // ---- 右:每键平均时长排行 ----
                float right = half + 30;
                TextAt(g, "每键平均时长 · Top " + data.TopKeys.Count, small, t.Text, new RectangleF(right, 114, half, 18));
                double peak = 1;
                foreach (KeyValuePair<int, KeyHold> pair in data.TopKeys) peak = Math.Max(peak, pair.Value.Mean);
                if (data.TopKeys.Count == 0) TextAt(g, "样本不足（每个键至少 10 次）", small, t.Muted, new RectangleF(right, 170, half, 20));
                for (int i = 0; i < data.TopKeys.Count; i++)
                {
                    KeyValuePair<int, KeyHold> pair = data.TopKeys[i];
                    float y = 140 + i * 26;
                    TextAt(g, Analysis.KeyName(pair.Key), small, t.Text, new RectangleF(right, y, 74, 20));
                    RectangleF track = new RectangleF(right + 80, y + 4, half - 200, 12);
                    Fill(g, t.Raised, track);
                    Fill(g, ArtTheme.Mix(t.Card, t.Cyan, .6), new RectangleF(track.X, track.Y, (float)(pair.Value.Mean / peak * track.Width), track.Height));
                    TextAt(g, pair.Value.Mean.ToString("0", CultureInfo.InvariantCulture) + " ms · " + pair.Value.Count + " 次", small, t.Muted,
                        new RectangleF(track.Right + 6, y, 130, 20));
                    targets.Add(new KeyValuePair<RectangleF, string>(new RectangleF(right, y, half - 20, 22),
                        Analysis.KeyName(pair.Key) + " · 平均 " + pair.Value.Mean.ToString("0", CultureInfo.InvariantCulture) + " ms · 最长 "
                        + pair.Value.MaxMs.ToString("0", CultureInfo.InvariantCulture) + " ms · " + pair.Value.Count + " 次"));
                }

                if (hover >= 0 && hover < targets.Count)
                    using (Pen outline = new Pen(t.Accent, 1.4f))
                    {
                        RectangleF r = targets[hover].Key;
                        g.DrawRectangle(outline, r.X, r.Y, Math.Max(2, r.Width), r.Height);
                    }
                TextAt(g, hover >= 0 && hover < targets.Count ? targets[hover].Value : data.Footer, small, t.Muted,
                    new RectangleF(14, 292, w - 28, 20));
            }
        }
    }

    /// <summary>应用 × 键位报告。</summary>
    internal sealed class AppKeyReportData
    {
        public const string Note = "按键在按下时归因到当时的前台进程;快速切换、游戏全屏与提权进程都可能漏记,未归因的击键单列,不按比例分摊。\n六类分组与「键位分布」页同一套默认映射:移动 / 技能栏 / 交互 / 功能 / 文字及其他 / 未归位。这里只描述按键构成,不推断应用用途。\n每天只保存击键最多的 12 个应用与「其他应用汇总」,更少的应用会并入汇总行。";

        public string Caption, Footer;
        public string[] Cards = new string[4], CardValues = new string[4];
        public string[] Headings = { "应用 / 进程", "击键", "移动", "技能栏", "交互", "功能", "文字及其他" };
        public readonly List<string[]> Rows = new List<string[]>();
        public readonly List<string> Names = new List<string>();
        public readonly List<long[]> Groups = new List<long[]>();
        public long TotalKeys, AttributedKeys;
        public int Days, Observed;

        public static AppKeyReportData Build(DateTime start, DateTime end)
        {
            AppKeyReportData data = new AppKeyReportData();
            RangeMetrics range = RangeStats.Of(start, end, true);   // 累积类页面:把进行中的今天算进来
            data.Days = range.Days;
            data.Observed = range.Observed;
            data.TotalKeys = range.Keys;

            Dictionary<string, long[]> byPath = new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase);
            DateTime from, to;
            if (RangeRules.TryNormalize(start, end, true, out from, out to))
            {
                for (DateTime date = from; date <= to; date = date.AddDays(1))
                {
                    DayRecord day = Analysis.GetDay(date);
                    if (day == null || day.IsEmpty) continue;
                    foreach (AppUsage app in day.Apps.Values)
                    {
                        if (!app.HasKeyGroups) continue;
                        string path = app.ProcessPath ?? "";
                        long[] groups;
                        if (!byPath.TryGetValue(path, out groups)) { groups = new long[6]; byPath[path] = groups; }
                        for (int i = 0; i < 6 && i < app.KeyGroups.Length; i++) groups[i] += app.KeyGroups[i];
                    }
                }
            }

            List<KeyValuePair<string, long[]>> list = new List<KeyValuePair<string, long[]>>(byPath);
            list.Sort(delegate(KeyValuePair<string, long[]> a, KeyValuePair<string, long[]> b)
            {
                long left = Sum(a.Value), right = Sum(b.Value);
                int order = right.CompareTo(left);
                return order != 0 ? order : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            });
            for (int i = 0; i < list.Count; i++)
            {
                long total = Sum(list[i].Value);
                data.AttributedKeys += total;
                data.Names.Add(list[i].Key == CrossTelemetry.Other ? "其他应用汇总" : CrossReportData.AppName(list[i].Key));
                data.Groups.Add(list[i].Value);
                data.Rows.Add(new string[]{
                    CrossReportData.AppName(list[i].Key) + " · " + list[i].Key,
                    total.ToString("N0", CultureInfo.InvariantCulture),
                    Share(list[i].Value[0], total), Share(list[i].Value[1], total), Share(list[i].Value[2], total),
                    Share(list[i].Value[3], total), Share(list[i].Value[4], total) });
            }
            if (data.Rows.Count == 0)
            {
                data.Rows.Add(new string[] { "暂无该维度数据", "0", "--", "--", "--", "--", "--" });
                data.Names.Add("暂无数据");
                data.Groups.Add(new long[6]);
            }
            else if (data.TotalKeys > data.AttributedKeys)
            {
                long missing = data.TotalKeys - data.AttributedKeys;
                data.Rows.Add(new string[] { "未归因击键（旧记录 / 切换边界 / 识别失败）", missing.ToString("N0", CultureInfo.InvariantCulture), "--", "--", "--", "--", "--" });
            }

            long[] grand = new long[6];
            foreach (KeyValuePair<string, long[]> pair in list)
                for (int i = 0; i < 6; i++) grand[i] += pair.Value[i];
            data.Caption = range.Days == 0 ? "所选区间没有完整日"
                : range.Start.ToString("MM.dd") + "—" + range.End.ToString("MM.dd") + " · " + range.Days + " 天" + (range.End == DateTime.Today ? "（含今日，进行中）" : "") + " · 各应用的按键构成（Top 8）";
            data.Cards = new string[] { "已归因击键", "记录应用", "移动类占比", "覆盖率" };
            data.CardValues = new string[] { data.AttributedKeys.ToString("N0", CultureInfo.InvariantCulture), list.Count.ToString(CultureInfo.InvariantCulture),
                Share(grand[0], data.AttributedKeys), Share(data.AttributedKeys, data.TotalKeys) };
            data.Footer = data.AttributedKeys == 0
                ? "区间内没有应用 × 键位样本。该维度从 1.7.3 起采集,更早的日期不会有数据;如果今天已经在使用,样本会在几秒内出现。"
                : "每行以该应用自身的击键数为分母;未归因部分单列,不按比例分摊。";
            return data;
        }

        private static long Sum(long[] values)
        {
            long total = 0;
            foreach (long value in values) total += value;
            return total;
        }

        private static string Share(long part, long whole)
        {
            return whole > 0 ? (part * 100.0 / whole).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "--";
        }
    }

    /// <summary>应用 × 键位图表:应用行 × 六类分组的构成热图。</summary>
    internal sealed class AppKeyReportVisual : Control
    {
        internal float UiScale;
        private readonly AppKeyReportData data;
        private readonly List<KeyValuePair<RectangleF, string>> targets = new List<KeyValuePair<RectangleF, string>>();
        private readonly ToolTip tip = new ToolTip { InitialDelay = 250, ReshowDelay = 100, AutoPopDelay = 20000 };
        private int hover = -1;

        public AppKeyReportVisual(DateTime start, DateTime end)
        {
            data = AppKeyReportData.Build(start, end);
            DoubleBuffered = true; ResizeRedraw = true;
            MouseMove += delegate(object sender, MouseEventArgs e)
            {
                float scale = UiScale > 0 ? UiScale : 1f;
                PointF point = new PointF(e.X / scale, e.Y / scale);
                int next = -1;
                for (int i = 0; i < targets.Count; i++) if (targets[i].Key.Contains(point)) { next = i; break; }
                if (next != hover) { hover = next; tip.SetToolTip(this, next >= 0 ? targets[next].Value : ""); Invalidate(); }
            };
            MouseLeave += delegate { hover = -1; Invalidate(); };
        }

        public void RenderTo(Graphics g, float scale)
        {
            float previous = UiScale; UiScale = scale;
            try
            {
                using (PaintEventArgs args = new PaintEventArgs(g, new Rectangle(0, 0,
                    Math.Max(1, (int)(ClientSize.Width * scale)), Math.Max(1, (int)(ClientSize.Height * scale))))) OnPaint(args);
            }
            finally { UiScale = previous; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }

        private static void TextAt(Graphics g, string text, Font font, Color color, RectangleF rect)
        {
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text ?? "", font, brush, rect, format);
        }
        private static void Fill(Graphics g, Color color, RectangleF rect)
        {
            if (rect.Width > 0 && rect.Height > 0) using (SolidBrush brush = new SolidBrush(color)) g.FillRectangle(brush, rect);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float scale = UiScale > 0 ? UiScale : g.DpiX / 96f;
            g.ScaleTransform(scale, scale);
            float w = ClientSize.Width / scale;
            ArtTheme t = ArtTheme.Current;
            g.Clear(t.Background); g.SmoothingMode = SmoothingMode.AntiAlias; targets.Clear();

            using (Font small = new Font("Microsoft YaHei UI", 11, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font body = new Font("Microsoft YaHei UI", 12, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font large = new Font("Segoe UI", 23, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                float card = (w - 44) / 4;
                for (int i = 0; i < 4; i++)
                {
                    float x = 10 + i * (card + 8);
                    ReportDesign.Surface(g, new RectangleF(x, 10, card, 67), t.Card, t.Line);
                    TextAt(g, data.Cards[i], small, t.Muted, new RectangleF(x + 12, 17, card - 24, 20));
                    TextAt(g, data.CardValues[i], data.CardValues[i].Length > 10 ? body : large, t.Text, new RectangleF(x + 12, 37, card - 24, 32));
                }
                TextAt(g, data.Caption, body, t.Text, new RectangleF(14, 86, w - 28, 23));

                float left = 210, right = w - 24;
                float column = (right - left) / 6;
                for (int i = 0; i < 6; i++)
                    TextAt(g, KeySemantics.Names[i], small, t.Muted, new RectangleF(left + i * column, 112, column - 4, 18));

                int shown = Math.Min(8, data.Groups.Count);
                float rowHeight = 26;
                for (int row = 0; row < shown; row++)
                {
                    long[] groups = data.Groups[row];
                    long total = 0;
                    foreach (long value in groups) total += value;
                    float y = 134 + row * rowHeight;
                    TextAt(g, data.Names[row], small, t.Text, new RectangleF(14, y + 2, left - 22, 20));
                    for (int g2 = 0; g2 < 6; g2++)
                    {
                        double share = total > 0 ? (double)groups[g2] / total : 0;
                        RectangleF cell = new RectangleF(left + g2 * column, y, column - 4, rowHeight - 6);
                        Fill(g, share <= 0 ? t.Raised : HeatScale.At(share), cell);
                        if (share >= 0.12) TextAt(g, (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%", small, t.Text,
                            new RectangleF(cell.X + 6, cell.Y + 2, cell.Width - 10, 18));
                        targets.Add(new KeyValuePair<RectangleF, string>(cell, data.Names[row] + " · " + KeySemantics.Names[g2] + " · "
                            + groups[g2].ToString("N0", CultureInfo.InvariantCulture) + " 次（" + (share * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%）"));
                    }
                }
                if (shown == 0) TextAt(g, "区间内没有应用 × 键位样本", small, t.Muted, new RectangleF(14, 150, w - 28, 20));

                HeatScale.Bar(g, new RectangleF(14, 356, w - 28, 9));
                TextAt(g, "按该应用的击键数归一化：0%", small, t.Muted, new RectangleF(14, 368, 240, 18));
                TextAt(g, "100%", small, t.Muted, new RectangleF(w - 60, 368, 50, 18));

                if (hover >= 0 && hover < targets.Count)
                    using (Pen outline = new Pen(t.Accent, 1.4f))
                    {
                        RectangleF r = targets[hover].Key;
                        g.DrawRectangle(outline, r.X, r.Y, Math.Max(2, r.Width), r.Height);
                    }
                TextAt(g, hover >= 0 && hover < targets.Count ? targets[hover].Value : data.Footer, small, t.Muted,
                    new RectangleF(14, 392, w - 28, 20));
            }
        }
    }
}
