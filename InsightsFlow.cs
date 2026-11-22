// ============================================================================
//  KeyMouseStats v3.0 · Milestone 3 / Step 2b  InsightsFlow.cs
//
//  「使用洞察」页的「深度心流与专注洞察」卡片 + 分钟级轨迹时光机轨道。
//  绘制全部走 Cw / ElasticH 弹性栅格;清爽版渲染克制提示胶囊。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace KeyMouseStats
{
    internal sealed partial class Dashboard
    {
        private TimelineDrag _timelineDrag = new TimelineDrag();
        private FlowBoardData _flowData;
        private DateTime _flowDataDay = DateTime.MinValue;
        private DateTime _flowDataAt = DateTime.MinValue;
        private DateTime _flowTaggedDay = DateTime.MinValue;
        private DateTime _flowViewDay = DateTime.MinValue;
        private bool _flowViewApplied;
        private static readonly FlowConfig FlowUiConfig = new FlowConfig();

        /// <summary>切到洞察页/换日期/定时器时准备;SQLite 读取与标签回写都不在 Paint 内。</summary>
        internal void PrepareFlowData(bool force)
        {
            if (!FlowBoard.Available) { _flowData = null; return; }
            DateTime day = _insightDay.Date;
            bool sameDay = _flowData != null && _flowDataDay == day;
            bool ready = sameDay && _flowData.HasData && _flowData.Report != null;
            // 缓存命中也要重试聚焦:首次数据未就绪时 _flowViewApplied 仍为 false。
            if (!force && ready && (DateTime.Now - _flowDataAt).TotalSeconds < 30)
            {
                ApplyFlowFocusOnce(day, _flowData);
                return;
            }
            FlowBoardData data = FlowBoard.Get(day);
            _flowData = data;
            _flowDataDay = day;
            _flowDataAt = DateTime.Now;
            if (data != null && data.HasData && data.Report != null && _flowTaggedDay != day)
            {
                _flowTaggedDay = day;
                FlowBoard.TagSessions(data);
            }
            ApplyFlowFocusOnce(day, data);
        }

        /// <summary>同一天只自动聚焦一次;双击复位后不会被定时刷新再次拉回。</summary>
        private void ApplyFlowFocusOnce(DateTime day, FlowBoardData data)
        {
            if (_flowViewDay == day && _flowViewApplied) return;
            _flowViewDay = day;
            _flowViewApplied = ApplyFlowFocus(data);
        }

        /// <summary>默认视口聚焦当日首末非 Idle 活跃段;用户拖动/双击后不再自动重置。</summary>
        private bool ApplyFlowFocus(FlowBoardData data)
        {
            int first, last;
            if (data == null || !data.ActiveRange(out first, out last)) return false;
            _timelineDrag.FocusRange(first, last);
            return true;
        }

        private static readonly Color FlowDeep = Color.FromArgb(74, 122, 214);      // 深蓝
        private static readonly Color FlowNormal = Color.FromArgb(64, 196, 190);    // 晴青
        private static readonly Color FlowFragmented = Color.FromArgb(226, 160, 72); // 琥珀
        private static readonly Color FlowIdle = Color.FromArgb(18, 128, 138, 152);  // 近留白:挂机不抢视觉
        private static readonly Color FlowGold = Color.FromArgb(190, 140, 42);       // 黄金时段
        private static readonly Color FlowGlow = Color.FromArgb(70, 64, 220, 180);   // 翡翠高光

        internal static Color FlowColor(FlowState state)
        {
            if (state == FlowState.Deep) return FlowDeep;
            if (state == FlowState.Normal) return FlowNormal;
            if (state == FlowState.Idle) return FlowIdle;
            return FlowFragmented;
        }

        internal static string FlowLabel(FlowState state)
        {
            if (state == FlowState.Deep) return "深度心流";
            if (state == FlowState.Normal) return "常规活跃";
            if (state == FlowState.Idle) return "空闲/挂机";
            return "碎片切换";
        }

        private static string FlowDuration(long ms)
        {
            if (ms <= 0) return "0m";
            long minutes = ms / 60000;
            if (minutes < 60) return minutes + "m";
            return (minutes / 60) + "h" + (minutes % 60 == 0 ? "" : (minutes % 60) + "m");
        }

        /// <summary>卡片几何:锚在内容区底部,随 Cb 上下浮动。空间不足时返回 false(整卡隐藏)。</summary>
        private bool FlowBoardRect(out float x, out float y, out float w, out float h)
        {
            x = Cx;
            w = Cw;
            // 上沿固定钉在 y=598(既有内容收在 586),高度随 ElasticH 向下长,绝不向上压内容。
            y = Cy + 500f;
            h = 96f + ElasticH;
            float limit = PageBottom - y - 8f;   // 底边严格收口在页脚分割线之上
            if (limit < 76f) { h = 0f; return false; }   // 1072 基准高度没有余量:整卡隐藏,绝不压页脚
            if (h > limit) h = limit;
            if (h < 76f) h = 76f;
            return true;
        }

        private void PaintFlowBoard(Graphics g)
        {
            float x, y, w, h;
            if (!FlowBoardRect(out x, out y, out w, out h)) return;
            PaintCard(g, x, y, w, h, "深度心流与专注洞察");

            // 真正未启用事件库:清爽版提示;标准版初始化失败则给明确诊断引导。
            if (!FlowBoard.Available)
            {
                DrawFlowCapsule(g, x + 20, y + 42, w - 40, FlowBoard.UnavailableHint);
                return;
            }

            FlowBoardData data = _flowData != null && _flowDataDay == _insightDay.Date ? _flowData : null;
            if (data == null || !data.HasData || data.Report == null)
            {
                DrawFlowCapsule(g, x + 20, y + 42, w - 40, FlowBoard.NoDataHint);
                return;
            }
            // 首帧兜底:即便准备流程被早退/缓存路径跳过,首次绘制有效数据也立即聚焦。
            if (_flowViewDay != _insightDay.Date || !_flowViewApplied)
                ApplyFlowFocusOnce(_insightDay.Date, data);

            float pad = 20f;
            float left = x + pad;
            float inner = w - pad * 2f;

            // 动态网格:大数字的真实宽高决定右侧标签与胶囊的位置,并给下方比例条留 ≥12px 安全间距。
            string scoreText = ((int)Math.Round(data.Report.FocusScore)).ToString();
            SizeF big = g.MeasureString(scoreText, _fNum2);
            SizeF label = g.MeasureString("专注分", _fSmall);
            float cy0 = y + 26f;
            using (SolidBrush ink = new SolidBrush(Ctext))
                g.DrawString(scoreText, _fNum2, ink, left, cy0);
            using (SolidBrush sub = new SolidBrush(Csub))
            {
                float lx = left + big.Width + 8f;
                g.DrawString("专注分", _fSmall, sub, lx, cy0 + big.Height - label.Height - 2f);
                float lx2 = lx + label.Width + 18f;
                string deepText = "深度连击 " + FlowDuration(data.Report.LongestDeepMs);
                g.DrawString(deepText, _fSmall, sub, lx2, cy0 + big.Height - label.Height - 2f);
                float lx3 = lx2 + g.MeasureString(deepText, _fSmall).Width + 14f;
                g.DrawString("最长专注 " + FlowDuration(data.Report.LongestFocusMs), _fSmall, sub, lx3, cy0 + big.Height - label.Height - 2f);
            }
            PaintFlowCosts(g, left, cy0, inner, data);

            float barY = cy0 + big.Height + 12f;
            float legendEnd;
            PaintFlowBar(g, left, barY, inner, data, out legendEnd);
            // 详情条从图例实际右边界 +18px 起画,并夹在卡片内,长文本用省略号截断。
            float detailX = legendEnd + 18f;
            float detailW = (left + inner) - detailX;
            if (detailW < 120f) { detailX = left + inner - 120f; detailW = 120f; }
            PaintFlowDetail(g, detailX, barY + 11f, detailW, data);
            PaintFlowTrack(g, left, barY + 30f, inner, data);
        }

        private void DrawFlowCapsule(Graphics g, float x, float y, float w, string message)
        {
            using (GraphicsPath path = RoundedRect(x, y, w, 34f, 12f))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(18, Csub)))
            using (Pen edge = new Pen(Color.FromArgb(40, Csub), 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(edge, path);
            }
            using (SolidBrush text = new SolidBrush(Csub))
                g.DrawString(message, _fSmall, text, new RectangleF(x + 14f, y + 9f, w - 28f, 20f));
        }

        /// <summary>右上角摘要:今日黄金专注时段(单行) + 干扰源 Top;与核心指标同一行,右对齐排布。</summary>
        private void PaintFlowCosts(Graphics g, float x, float y, float w, FlowBoardData data)
        {
            float costX = x + Math.Max(360f, w * 0.52f);
            float costW = x + w - costX;
            if (costW < 80f) return;

            string goldenText = FlowGoldenLine(data);
            using (SolidBrush gold = new SolidBrush(FlowGold))
            using (StringFormat clip = new StringFormat())
            {
                clip.Trimming = StringTrimming.EllipsisCharacter;
                clip.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(goldenText, _fSmall, gold, new RectangleF(costX, y, costW, 15f), clip);
            }

            if (data.Costs == null || data.Costs.Count == 0)
            {
                using (SolidBrush sub = new SolidBrush(Csub))
                    g.DrawString("暂无干扰源样本", _fSmall, sub, costX, y + 15f);
                return;
            }
            int shown = 0;
            for (int i = 0; i < data.Costs.Count && shown < 2; i++)
            {
                ContextSwitchStat cost = data.Costs[i];
                string line = ShortApp(cost.ProcessName) + " · 平均打断损耗 "
                    + (cost.AvgLagSeconds / 60.0).ToString("0.0") + " 分钟";
                using (SolidBrush brush = new SolidBrush(i == 0 ? FlowFragmented : Csub))
                    g.DrawString(line, _fSmall, brush, costX, y + (shown + 1) * 15f);
                shown++;
            }
        }

        /// <summary>按「深度权重 + 常规权重」挑选 1–2 个黄金专注段。</summary>
        /// <summary>黄金时段摘要:固定单行,最多两个时段用「 / 」横向拼接。</summary>
        internal static string FlowGoldenLine(FlowBoardData data)
        {
            List<FlowSegment> golden = FlowGoldenSegments(data);
            string text = "今日黄金时段 · ";
            if (golden.Count == 0) return text + "暂无专注高峰";
            for (int i = 0; i < golden.Count && i < 2; i++)
            {
                if (i > 0) text += " / ";
                FlowSegment segment = golden[i];
                text += FlowClock(segment.StartMs, data) + "–"
                    + FlowClock(segment.EndMs, data) + " " + FlowSegmentApp(data, segment);
            }
            return text;
        }

        private static List<FlowSegment> FlowGoldenSegments(FlowBoardData data)
        {
            List<FlowSegment> picked = new List<FlowSegment>();
            if (data == null || data.Report == null) return picked;
            FlowSegment best1 = null;
            FlowSegment best2 = null;
            long score1 = -1;
            long score2 = -1;
            List<FlowSegment> segments = data.Report.Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                FlowSegment segment = segments[i];
                if (segment.State != FlowState.Deep && segment.State != FlowState.Normal) continue;
                if (segment.LengthMs < 60000) continue;
                long score = segment.LengthMs * (segment.State == FlowState.Deep ? 10L : 6L);
                if (score > score1)
                {
                    score2 = score1;
                    best2 = best1;
                    score1 = score;
                    best1 = segment;
                }
                else if (score > score2)
                {
                    score2 = score;
                    best2 = segment;
                }
            }
            if (best1 != null) picked.Add(best1);
            if (best2 != null) picked.Add(best2);
            if (picked.Count == 2 && picked[0].StartMs > picked[1].StartMs)
            {
                FlowSegment swap = picked[0];
                picked[0] = picked[1];
                picked[1] = swap;
            }
            return picked;
        }

        private static string FlowClock(long ms, FlowBoardData data)
        {
            int sec = (int)((ms - FlowBoard.DayStartMs(data.Day)) / 1000L);
            if (sec < 0) sec = 0;
            if (sec > TimelineWindow.DaySeconds) sec = TimelineWindow.DaySeconds;
            return (sec / 3600).ToString("00") + ":" + ((sec % 3600) / 60).ToString("00");
        }

        private static string FlowSegmentApp(FlowBoardData data, FlowSegment segment)
        {
            string path = segment.PrimaryProcess;
            if (!string.IsNullOrEmpty(path))
            {
                int slash = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
                string name = slash >= 0 ? path.Substring(slash + 1) : path;
                return ShortApp(name);
            }
            long midMs = (segment.StartMs + segment.EndMs) / 2L;
            int mid = (int)((midMs - FlowBoard.DayStartMs(data.Day)) / 1000L);
            FlowSessionRow row = data.SessionAt(mid);
            return row == null ? "未知应用" : ShortApp(row.ProcessName);
        }

        private void PaintFlowBar(Graphics g, float x, float y, float w, FlowBoardData data, out float legendEndX)
        {
            legendEndX = x;
            float barW = Math.Max(120f, w * 0.5f);
            float barH = 8f;
            using (GraphicsPath back = RoundedRect(x, y, barW, barH, 4f))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(22, Csub)))
                g.FillPath(fill, back);

            long total = data.TotalMs;
            if (total <= 0) return;
            float px = x;
            px = DrawFlowSlice(g, px, y, barW, barH, data.DeepMs, total, FlowDeep);
            px = DrawFlowSlice(g, px, y, barW, barH, data.NormalMs, total, FlowNormal);
            DrawFlowSlice(g, px, y, barW, barH, data.FragmentedMs, total, FlowFragmented);

            using (SolidBrush sub = new SolidBrush(Csub))
            {
                // 图例按实际文字宽度递推,固定 18px 间距,杜绝「深度心流• 常规活跃」粘连。
                string[] legend = { "● 深度心流", "● 常规活跃", "● 碎片切换" };
                float legendX = x;
                for (int gi = 0; gi < legend.Length; gi++)
                {
                    g.DrawString(legend[gi], _fSmall, sub, legendX, y + 13f);
                    legendX += g.MeasureString(legend[gi], _fSmall).Width + 18f;
                }
                legendEndX = legendX - 18f;   // 返回最后一个图例的右边界(不含尾部间距)
            }
        }

        private static float DrawFlowSlice(Graphics g, float x, float y, float barW, float barH,
            long part, long total, Color color)
        {
            if (part <= 0 || total <= 0) return x;
            float width = barW * part / total;
            if (width < 1f) width = 1f;
            using (SolidBrush brush = new SolidBrush(color))
                g.FillRectangle(brush, x, y, width, barH);
            return x + width;
        }

        /// <summary>
        /// 分钟级轨迹:底带按四态着色,脉冲柱按每分钟击键起伏,
        /// Deep 区间顶部叠加翡翠高光;Idle 仅保留近留白底带。
        /// </summary>
        private void PaintFlowTrack(Graphics g, float x, float y, float w, FlowBoardData data)
        {
            RectangleF track = new RectangleF(x, y, w, 22f);
            _timelineDrag.Track = track;

            using (GraphicsPath back = RoundedRect(track.X, track.Y, track.Width, track.Height, 6f))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(20, Csub)))
                g.FillPath(fill, back);

            long dayStart = FlowBoard.DayStartMs(_insightDay);
            List<FlowSegment> segments = data.Report.Segments;
            long maxKeys = 1;
            for (int i = 0; i < segments.Count; i++)
            {
                FlowSegment segment = segments[i];
                if (segment.MinuteKeys != null)
                {
                    for (int m = 0; m < segment.MinuteKeys.Count; m++)
                        if (segment.MinuteKeys[m] > maxKeys) maxKeys = segment.MinuteKeys[m];
                }
                else if (segment.LengthMs > 0)
                {
                    long minutes = Math.Max(1L, segment.LengthMs / 60000L);
                    long average = segment.Keys / minutes;
                    if (average > maxKeys) maxKeys = average;
                }
            }

            using (SolidBrush deepPulse = new SolidBrush(FlowDeep))
            using (SolidBrush normalPulse = new SolidBrush(FlowNormal))
            using (SolidBrush fragPulse = new SolidBrush(FlowFragmented))
            using (SolidBrush idleBase = new SolidBrush(Color.FromArgb(10, Csub)))
            using (SolidBrush glow = new SolidBrush(FlowGlow))
            using (Pen glowEdge = new Pen(Color.FromArgb(150, 64, 220, 180), 1f))
            {
                // 1. 底部状态带:Idle 近留白,其余用低透明状态色锚定区间。
                for (int i = 0; i < segments.Count; i++)
                {
                    FlowSegment segment = segments[i];
                    float bx0, bx1, bw;
                    if (!FlowTrackSpan(segment, track, dayStart, out bx0, out bx1, out bw)) continue;
                    if (segment.State == FlowState.Idle)
                    {
                        g.FillRectangle(idleBase, bx0, track.Bottom - 2f, bw, 2f);
                        continue;
                    }
                    using (SolidBrush baseBrush = new SolidBrush(Color.FromArgb(42, FlowColor(segment.State))))
                        g.FillRectangle(baseBrush, bx0, track.Bottom - 3f, bw, 3f);
                }

                // 2. 分钟脉冲:输入越密集柱越高;无击键的活动分钟保留 3px 底柱。
                for (int i = 0; i < segments.Count; i++)
                {
                    FlowSegment segment = segments[i];
                    if (segment.State == FlowState.Idle) continue;
                    SolidBrush pulse = segment.State == FlowState.Deep
                        ? deepPulse
                        : (segment.State == FlowState.Normal ? normalPulse : fragPulse);
                    if (segment.MinuteKeys == null)
                    {
                        long minutes = Math.Max(1L, segment.LengthMs / 60000L);
                        long average = segment.Keys / minutes;
                        if (average <= 0) continue;
                        float bx0, bx1, bw;
                        if (!FlowTrackSpan(segment, track, dayStart, out bx0, out bx1, out bw)) continue;
                        float bh = FlowPulseHeight(average, maxKeys, track.Height);
                        g.FillRectangle(pulse, bx0, track.Bottom - bh, bw, bh);
                        continue;
                    }
                    int startSec = (int)((segment.StartMs - dayStart) / 1000L);
                    for (int m = 0; m < segment.MinuteKeys.Count; m++)
                    {
                        long keys = segment.MinuteKeys[m];
                        if (keys <= 0) continue;
                        int sec = startSec + m * 60;
                        if (sec + 60 <= _timelineDrag.StartSec || sec >= _timelineDrag.EndSec) continue;
                        float x0 = TimelineWindow.MapSecToX(sec, track, _timelineDrag.StartSec, _timelineDrag.SpanSec);
                        float x1 = TimelineWindow.MapSecToX(sec + 60, track, _timelineDrag.StartSec, _timelineDrag.SpanSec);
                        float width = x1 - x0;
                        if (width < 1f) width = 1f;
                        if (x1 < track.X || x0 > track.Right) continue;
                        if (x0 < track.X) { width -= track.X - x0; x0 = track.X; }
                        if (x0 + width > track.Right) width = track.Right - x0;
                        if (width <= 0f) continue;
                        float height = FlowPulseHeight(keys, maxKeys, track.Height);
                        g.FillRectangle(pulse, x0, track.Bottom - height, width, height);
                    }
                }

                // 3. Deep 顶部翡翠高光:柔和的半透明光带 + 一条更亮的边。
                for (int i = 0; i < segments.Count; i++)
                {
                    FlowSegment segment = segments[i];
                    if (segment.State != FlowState.Deep) continue;
                    float bx0, bx1, bw;
                    if (!FlowTrackSpan(segment, track, dayStart, out bx0, out bx1, out bw)) continue;
                    g.FillRectangle(glow, bx0, track.Y + 1f, bw, 3f);
                    g.DrawLine(glowEdge, bx0, track.Y + 1.5f, bx0 + bw, track.Y + 1.5f);
                }
            }

            // 时间刻度:2 小时视窗用 15 分钟刻度,6 小时内用 30 分钟。
            using (SolidBrush sub = new SolidBrush(Csub))
            using (Pen tick = new Pen(Color.FromArgb(48, Csub), 1f))
            {
                int step = _timelineDrag.SpanSec <= 3600 ? 300
                    : (_timelineDrag.SpanSec <= 7200 ? 900
                    : (_timelineDrag.SpanSec <= 21600 ? 1800
                    : (_timelineDrag.SpanSec <= 43200 ? 3600 : 4 * 3600)));
                for (int sec = 0; sec <= TimelineWindow.DaySeconds; sec += step)
                {
                    float tx = TimelineWindow.MapSecToX(sec, track, _timelineDrag.StartSec, _timelineDrag.SpanSec);
                    if (tx < track.X - 1f || tx > track.Right + 1f) continue;
                    g.DrawLine(tick, tx, track.Bottom, tx, track.Bottom + 3f);
                    if (sec % (step * 2) == 0)
                        g.DrawString((sec / 3600).ToString("00") + ":" + ((sec % 3600) / 60).ToString("00"),
                            _fSmall, sub, tx - 14f, track.Bottom + 4f);
                }
            }

            // 游标
            int focus = _timelineDrag.FocusSec;
            if (focus >= 0)
            {
                float cx = TimelineWindow.MapSecToX(focus, track, _timelineDrag.StartSec, _timelineDrag.SpanSec);
                if (cx >= track.X && cx <= track.Right)
                {
                    using (Pen pen = new Pen(Color.FromArgb(220, Ctext), 1f))
                        g.DrawLine(pen, cx, track.Y - 3f, cx, track.Bottom + 3f);
                }
            }
        }

        /// <summary>把段映射到轨道并裁剪;返回 false 表示完全不可见。</summary>
        private bool FlowTrackSpan(FlowSegment segment, RectangleF track, long dayStart,
            out float x0, out float x1, out float width)
        {
            int from = (int)((segment.StartMs - dayStart) / 1000L);
            int to = (int)((segment.EndMs - dayStart) / 1000L);
            x0 = TimelineWindow.MapSecToX(from, track, _timelineDrag.StartSec, _timelineDrag.SpanSec);
            x1 = TimelineWindow.MapSecToX(to, track, _timelineDrag.StartSec, _timelineDrag.SpanSec);
            width = x1 - x0;
            if (width < 1f) width = 1f;
            if (x1 < track.X || x0 > track.Right) return false;
            if (x0 < track.X) { width -= track.X - x0; x0 = track.X; }
            if (x0 + width > track.Right) width = track.Right - x0;
            return width > 0f;
        }

        /// <summary>脉冲高度:开方压缩,低输入也可见,高输入不溢出轨道。</summary>
        internal static float FlowPulseHeight(long keys, long maxKeys, float trackHeight)
        {
            float baseHeight = 3f;
            if (trackHeight <= 0f) return 0f;
            if (trackHeight <= baseHeight + 1f) return trackHeight;
            if (keys <= 0 || maxKeys <= 0) return baseHeight;
            double ratio = keys * 1.0 / maxKeys;
            if (ratio > 1.0) ratio = 1.0;
            return baseHeight + (trackHeight - baseHeight - 1f) * (float)Math.Sqrt(ratio);
        }

        /// <summary>联动信息条:前台应用 / 完整标题 / 心流状态 / 局部输入密度。</summary>
        private void PaintFlowDetail(Graphics g, float x, float y, float w, FlowBoardData data)
        {
            int focus = _timelineDrag.FocusSec;
            if (focus < 0)
            {
                using (SolidBrush sub = new SolidBrush(Csub))
                    g.DrawString("在轨道上拖动可平移视窗 · 滚轮缩放 · 单击选中分钟 · 双击复位全天",
                        _fSmall, sub, new RectangleF(x, y, w, 18f));
                return;
            }

            FlowSessionRow row = data.SessionAt(focus);
            long atMs = FlowBoard.DayStartMs(data.Day) + focus * 1000L;
            FlowState state = data.StateAt(atMs);
            string time = (focus / 3600).ToString("00") + ":" + ((focus % 3600) / 60).ToString("00");
            string app = row == null ? "无前台记录" : (string.IsNullOrEmpty(row.ProcessName) ? "未知应用" : row.ProcessName);
            string title = row == null ? "" : (row.WindowTitle ?? "");
            double density = data.DensityAt(focus);
            FlowDiagnostic diag = data.Report.Normalized == null
                ? null
                : FlowAnalyzer.DiagnoseNormalized(data.Report.Normalized, data.Bursts, atMs, FlowUiConfig);
            string reason = FlowReason(state, diag);

            using (SolidBrush sub = new SolidBrush(Csub))
            using (StringFormat clip = new StringFormat())
            {
                clip.Trimming = StringTrimming.EllipsisCharacter;
                clip.FormatFlags = StringFormatFlags.NoWrap;
                string line = time + " · " + app + " · " + FlowLabel(state);
                if (reason.Length > 0) line += " · " + reason;
                line += " · 局部 " + density.ToString("0") + " 键/分";
                if (title.Length > 0) line += " · " + title;
                g.DrawString(line, _fSmall, sub, new RectangleF(x, y, w, 20f), clip);
            }
        }

        /// <summary>悬停诊断文案:专注段给粘性/输入,碎片段给最可能的打断原因。</summary>
        private static string FlowReason(FlowState state, FlowDiagnostic diag)
        {
            if (state == FlowState.Idle) return "无键鼠输入";
            if (diag == null) return "";
            if (state == FlowState.Deep || state == FlowState.Normal)
            {
                int rate = (int)Math.Round(diag.EffectiveInput * 60.0 / FlowUiConfig.WindowSeconds);
                return "粘性 " + (diag.Stickiness * 100.0).ToString("0") + "% · " + rate + " 键鼠/分";
            }
            if (diag.EffectiveInput < FlowUiConfig.MinInputPerWindow) return "输入不足 / 停顿过长";
            if (diag.IkiCv > FlowUiConfig.IkiCvMax || diag.IkiVar > FlowUiConfig.IkiVarMax) return "停顿过长";
            if (diag.LongCheckouts > 0) return "长切出 / 停顿过长";
            if (diag.Stickiness < FlowUiConfig.NormalStickinessThreshold
                || diag.DistinctCategories > FlowUiConfig.MaxCategoriesPerWindow) return "切窗频繁";
            if (diag.MaxUnrelatedRun >= FlowUiConfig.UnrelatedSwitchLimit) return "连续切出";
            if (diag.WorkToNonWork > FlowUiConfig.MaxWorkToNonWork) return "工作流被打断";
            return "节律不稳";
        }

        private static string ShortApp(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return "未知应用";
            string name = processName;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name;
        }

        // ---------------------------------------------------------------- 交互入口

        /// <summary>轨道是否捕获该点(内容坐标)。</summary>
        private bool FlowTrackHit(float cx, float cy)
        {
            return _timelineDrag.HitTrack(cx, cy);
        }

        private void ResetFlowTagCache()
        {
            _flowData = null;
            _flowDataDay = DateTime.MinValue;
            _flowDataAt = DateTime.MinValue;
            _flowTaggedDay = DateTime.MinValue;
            _flowViewDay = DateTime.MinValue;
            _flowViewApplied = false;
        }
    }
}
