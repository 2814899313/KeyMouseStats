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

        /// <summary>切到洞察页/换日期/定时器时准备;SQLite 读取与标签回写都不在 Paint 内。</summary>
        internal void PrepareFlowData(bool force)
        {
            if (!FlowBoard.Available) { _flowData = null; return; }
            DateTime day = _insightDay.Date;
            bool sameDay = _flowData != null && _flowDataDay == day;
            if (!force && sameDay && (DateTime.Now - _flowDataAt).TotalSeconds < 30) return;
            FlowBoardData data = FlowBoard.Get(day);
            _flowData = data;
            _flowDataDay = day;
            _flowDataAt = DateTime.Now;
            if (data != null && data.HasData && data.Report != null && _flowTaggedDay != day)
            {
                _flowTaggedDay = day;
                FlowBoard.TagSessions(data);
            }
        }

        private static readonly Color FlowDeep = Color.FromArgb(74, 122, 214);      // 深蓝
        private static readonly Color FlowNormal = Color.FromArgb(64, 196, 190);    // 晴青
        private static readonly Color FlowFragmented = Color.FromArgb(226, 160, 72); // 琥珀

        internal static Color FlowColor(FlowState state)
        {
            if (state == FlowState.Deep) return FlowDeep;
            if (state == FlowState.Normal) return FlowNormal;
            return FlowFragmented;
        }

        internal static string FlowLabel(FlowState state)
        {
            if (state == FlowState.Deep) return "深度心流";
            if (state == FlowState.Normal) return "常规活跃";
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

            // 清爽版:显式能力探测降级(ShadowPipeline.Available 的第一个消费者)
            if (!FlowBoard.Available)
            {
                DrawFlowCapsule(g, x + 20, y + 42, w - 40);
                return;
            }

            FlowBoardData data = _flowData != null && _flowDataDay == _insightDay.Date ? _flowData : null;
            if (data == null || !data.HasData || data.Report == null)
            {
                DrawFlowCapsule(g, x + 20, y + 42, w - 40);
                return;
            }

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
                g.DrawString("最长连击 " + FlowDuration(data.Report.LongestDeepMs), _fSmall, sub, lx2, cy0 + big.Height - label.Height - 2f);
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

        private void DrawFlowCapsule(Graphics g, float x, float y, float w)
        {
            using (GraphicsPath path = RoundedRect(x, y, w, 34f, 12f))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(18, Csub)))
            using (Pen edge = new Pen(Color.FromArgb(40, Csub), 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(edge, path);
            }
            using (SolidBrush text = new SolidBrush(Csub))
                g.DrawString(FlowBoard.CleanHint, _fSmall, text, new RectangleF(x + 14f, y + 9f, w - 28f, 20f));
        }

        /// <summary>右上角干扰源 Top 3;与核心指标同一行,右对齐排布。</summary>
        private void PaintFlowCosts(Graphics g, float x, float y, float w, FlowBoardData data)
        {
            float costX = x + Math.Max(360f, w * 0.52f);
            if (data.Costs == null || data.Costs.Count == 0)
            {
                using (SolidBrush sub = new SolidBrush(Csub))
                    g.DrawString("暂无干扰源样本", _fSmall, sub, costX, y);
                return;
            }
            int shown = 0;
            for (int i = 0; i < data.Costs.Count && shown < 3; i++)
            {
                ContextSwitchStat cost = data.Costs[i];
                string line = ShortApp(cost.ProcessName) + " · 平均打断损耗 "
                    + (cost.AvgLagSeconds / 60.0).ToString("0.0") + " 分钟";
                using (SolidBrush brush = new SolidBrush(i == 0 ? FlowFragmented : Csub))
                    g.DrawString(line, _fSmall, brush, costX, y + shown * 15f);
                shown++;
            }
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

        /// <summary>分钟级轨迹轨道:视窗由 TimelineDrag 决定,底色按三态着色。</summary>
        private void PaintFlowTrack(Graphics g, float x, float y, float w, FlowBoardData data)
        {
            RectangleF track = new RectangleF(x, y, w, 22f);
            _timelineDrag.Track = track;

            using (GraphicsPath back = RoundedRect(track.X, track.Y, track.Width, track.Height, 6f))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(20, Csub)))
                g.FillPath(fill, back);

            long dayStart = FlowBoard.DayStartMs(_insightDay);
            List<FlowSegment> segments = data.Report.Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                FlowSegment segment = segments[i];
                int from = (int)((segment.StartMs - dayStart) / 1000L);
                int to = (int)((segment.EndMs - dayStart) / 1000L);
                float x0 = TimelineWindow.MapSecToX(from, track, _timelineDrag.StartSec, _timelineDrag.SpanSec);
                float x1 = TimelineWindow.MapSecToX(to, track, _timelineDrag.StartSec, _timelineDrag.SpanSec);
                float width = Math.Max(1f, x1 - x0);
                if (x1 < track.X || x0 > track.Right) continue;
                x0 = Math.Max(x0, track.X);
                width = Math.Min(width, track.Right - x0);
                using (SolidBrush brush = new SolidBrush(FlowColor(segment.State)))
                    g.FillRectangle(brush, x0, track.Y, width, track.Height);
            }

            // 小时刻度
            using (SolidBrush sub = new SolidBrush(Csub))
            using (Pen tick = new Pen(Color.FromArgb(48, Csub), 1f))
            {
                int step = _timelineDrag.SpanSec <= 3600 ? 600 : (_timelineDrag.SpanSec <= 21600 ? 3600 : 4 * 3600);
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
            FlowState state = data.StateAt(FlowBoard.DayStartMs(data.Day) + focus * 1000L);
            string time = (focus / 3600).ToString("00") + ":" + ((focus % 3600) / 60).ToString("00");
            string app = row == null ? "无前台记录" : (string.IsNullOrEmpty(row.ProcessName) ? "未知应用" : row.ProcessName);
            string title = row == null ? "" : (row.WindowTitle ?? "");
            double density = data.DensityAt(focus);

            using (SolidBrush sub = new SolidBrush(Csub))
            using (StringFormat clip = new StringFormat())
            {
                clip.Trimming = StringTrimming.EllipsisCharacter;
                clip.FormatFlags = StringFormatFlags.NoWrap;
                string line = time + " · " + app + " · " + FlowLabel(state)
                    + " · 局部 " + density.ToString("0") + " 键/分";
                if (title.Length > 0) line += " · " + title;
                g.DrawString(line, _fSmall, sub, new RectangleF(x, y, w, 20f), clip);
            }
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
        }
    }
}
