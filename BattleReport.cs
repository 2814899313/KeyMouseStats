// ============================================================================
//  KeyMouseStats v3.0 · Milestone 3 / Step 3  BattleReport.cs
//
//  社交战报竖版长图渲染器(GDI+ 离屏)。宽度固定 1080,高度按模块自适应。
//  纯绘制:输入一个模型,输出一个 Bitmap,不读存档、不碰 UI、不依赖主题单例。
//  隐私打码默认关闭;开启时窗口标题经 PrivacyMask 脱敏,Rime 文本按词长替换为 ＊。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace KeyMouseStats
{
    internal sealed class BattleReportMetric
    {
        public string Label = "";
        public string Value = "";
        public string Hint = "";
    }

#pragma warning disable 0649   // 模型字段由调用方填充(日报页 / 测试),本文件只读取
    internal sealed class BattleReportModel
    {
        public string Brand = "键鼠统计 · ACTIVITY MONITOR";
        public string Headline = "今日战报";
        public string Subtitle = "";
        public string Honor = "";
        public List<BattleReportMetric> Metrics = new List<BattleReportMetric>();
        public bool HasFlow;
        public double FlowScore;
        public string DeepText = "";
        public string NormalText = "";
        public string FragmentedText = "";
        public string LongestFlow = "";
        public List<string> TopApps = new List<string>();
        public int TopAppsCategory = 4;
        public string TopChars = "";
        public string Footer = "";

        public Color Background = Color.Black;
        public Color Card = Color.FromArgb(24, 24, 28);
        public Color TextMain = Color.White;
        public Color TextSub = Color.Gray;
        public Color Accent = Color.FromArgb(74, 122, 214);
    }

#pragma warning restore 0649
    internal static class BattleReport
    {
        internal const int Width = 1080;
        private const float Margin = 56f;

        /// <summary>渲染战报长图。mask=true 时窗口标题与 Rime 文本脱敏。</summary>
        internal static Bitmap Render(BattleReportModel model, bool mask)
        {
            if (model == null) model = new BattleReportModel();
            int height = MeasureHeight(model);
            Bitmap bitmap = new Bitmap(Width, height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(model.Background);

                bool previous = PrivacyMask.Enabled;
                PrivacyMask.Enabled = mask;
                try { Paint(g, model, mask); }
                finally { PrivacyMask.Enabled = previous; }
            }
            return bitmap;
        }

        /// <summary>
        /// 长图高度。必须与 <see cref="Paint"/> 的 y 推进逐段一致 ——
        /// 旧版是另一套估算(少了指标卡的 +8、应用榜的 +14 间距、页脚文字高度与下边距),
        /// 结果实际绘制比量出来的高 60 多像素,页脚「键鼠统计 v3.0 …」正好被底边切掉。
        /// </summary>
        internal static int MeasureHeight(BattleReportModel model)
        {
            if (model == null) model = new BattleReportModel();
            float y = Margin;
            y += 24f;                                   // 品牌行
            y += 54f + 22f + 30f;                       // 标题 / 副标题 / 江湖称号
            if (model.HasFlow) y += 150f;               // 心流卡片(卡片 124 + 间距 26,与 PaintFlow 一致)
            int rows = (model.Metrics.Count + 1) / 2;
            y += rows * 92f + 8f;                       // 指标卡 2 列
            if (model.TopApps != null && model.TopApps.Count > 0)
                y += 14f + 28f + model.TopApps.Count * 44f;      // 应用榜
            if (!string.IsNullOrEmpty(model.TopChars))
                y += 96f;                                        // 输入法亮点
            y += 12f + 20f;                             // 页脚偏移 + 文字高度
            y += Margin;                                // 下边距
            int result = (int)Math.Ceiling(y);
            return result < 480 ? 480 : result;
        }

        private static void Paint(Graphics g, BattleReportModel model, bool mask)
        {
            float x = Margin;
            float y = Margin;
            float inner = Width - Margin * 2f;

            using (SolidBrush sub = new SolidBrush(model.TextSub))
                g.DrawString(model.Brand, SystemFonts.DefaultFont, sub, x, y);
            y += 24f;

            using (SolidBrush main = new SolidBrush(model.TextMain))
            {
                g.DrawString(model.Headline, new Font("Microsoft YaHei", 34f, FontStyle.Bold), main, x - 4f, y);
                y += 54f;
                using (SolidBrush sub = new SolidBrush(model.TextSub))
                {
                    if (model.Subtitle.Length > 0) g.DrawString(model.Subtitle, new Font("Microsoft YaHei", 12f), sub, x, y);
                    y += 22f;
                    if (model.Honor.Length > 0) g.DrawString(model.Honor, new Font("Microsoft YaHei", 13f, FontStyle.Bold), main, x, y);
                }
                y += 30f;
            }

            if (model.HasFlow) y = PaintFlow(g, model, x, y, inner);
            y = PaintMetrics(g, model, x, y, inner);
            y = PaintApps(g, model, x, y, inner, mask);
            y = PaintChars(g, model, x, y, inner, mask);

            using (SolidBrush sub = new SolidBrush(model.TextSub))
                g.DrawString(model.Footer, new Font("Microsoft YaHei", 10f), sub, x, y + 12f);
        }

        private static float PaintFlow(Graphics g, BattleReportModel model, float x, float y, float w)
        {
            FillCard(g, model, x, y, w, 124f);
            using (SolidBrush main = new SolidBrush(model.TextMain))
                g.DrawString(((int)Math.Round(model.FlowScore)).ToString(), new Font("Microsoft YaHei", 30f, FontStyle.Bold), main, x + 22f, y + 18f);
            using (SolidBrush sub = new SolidBrush(model.TextSub))
            {
                g.DrawString("专注分", new Font("Microsoft YaHei", 11f), sub, x + 22f, y + 66f);
                g.DrawString("深度心流 " + model.DeepText, new Font("Microsoft YaHei", 12f), sub, x + 150f, y + 26f);
                g.DrawString("常规活跃 " + model.NormalText, new Font("Microsoft YaHei", 12f), sub, x + 150f, y + 52f);
                g.DrawString("碎片切换 " + model.FragmentedText, new Font("Microsoft YaHei", 12f), sub, x + 150f, y + 78f);
                g.DrawString("最长连击 " + model.LongestFlow, new Font("Microsoft YaHei", 12f, FontStyle.Bold), sub, x + w - 220f, y + 52f);
            }
            return y + 150f;   // 卡片 124 + 间距 26:与 MeasureHeight 的 150 保持一致(旧版这里只推 138)
        }

        private static float PaintMetrics(Graphics g, BattleReportModel model, float x, float y, float w)
        {
            float gap = 16f;
            float cardW = (w - gap) / 2f;
            for (int i = 0; i < model.Metrics.Count; i++)
            {
                BattleReportMetric metric = model.Metrics[i];
                float cx = x + (i % 2) * (cardW + gap);
                float cy = y + (i / 2) * 92f;
                FillCard(g, model, cx, cy, cardW, 76f);
                using (SolidBrush sub = new SolidBrush(model.TextSub))
                    g.DrawString(metric.Label, new Font("Microsoft YaHei", 11f), sub, cx + 18f, cy + 12f);
                using (SolidBrush main = new SolidBrush(model.TextMain))
                    g.DrawString(metric.Value, new Font("Microsoft YaHei", 22f, FontStyle.Bold), main, cx + 18f, cy + 30f);
                if (metric.Hint.Length > 0)
                    using (SolidBrush sub = new SolidBrush(model.TextSub))
                        g.DrawString(metric.Hint, new Font("Microsoft YaHei", 9f), sub, cx + 18f, cy + 58f);
            }
            int rows = (model.Metrics.Count + 1) / 2;
            return y + rows * 92f + 8f;
        }

        /// <summary>应用榜:没有数据时整段不画、也不占高度(旧版会留一个空的「常用应用 Top 1」标题)。</summary>
        private static float PaintApps(Graphics g, BattleReportModel model, float x, float y, float w, bool mask)
        {
            if (model.TopApps == null || model.TopApps.Count == 0) return y;
            y += 14f;   // 与指标卡的间距,只有真的有内容时才让出来
            using (SolidBrush main = new SolidBrush(model.TextMain))
                g.DrawString("常用应用 Top " + model.TopApps.Count, new Font("Microsoft YaHei", 14f, FontStyle.Bold), main, x, y);
            y += 28f;
            bool previous = PrivacyMask.Enabled;
            PrivacyMask.Enabled = mask;
            try
            {
                for (int i = 0; i < model.TopApps.Count; i++)
                {
                    string line = PrivacyMask.MaskTitle(model.TopApps[i], model.TopAppsCategory);
                    using (SolidBrush sub = new SolidBrush(model.TextSub))
                        g.DrawString((i + 1) + "  " + line, new Font("Microsoft YaHei", 12f), sub, x, y + i * 44f);
                }
            }
            finally { PrivacyMask.Enabled = previous; }
            return y + model.TopApps.Count * 44f;
        }

        /// <summary>输入法亮点:没有 Rime 数据时整段不画、也不占高度。</summary>
        private static float PaintChars(Graphics g, BattleReportModel model, float x, float y, float w, bool mask)
        {
            if (string.IsNullOrEmpty(model.TopChars)) return y;
            using (SolidBrush main = new SolidBrush(model.TextMain))
                g.DrawString("输入法亮点", new Font("Microsoft YaHei", 14f, FontStyle.Bold), main, x, y);
            bool previous = PrivacyMask.Enabled;
            PrivacyMask.Enabled = mask;
            try
            {
                string text = PrivacyMask.MaskText(model.TopChars);
                using (SolidBrush sub = new SolidBrush(model.TextSub))
                    g.DrawString(text, new Font("Microsoft YaHei", 13f), sub, x, y + 28f);
            }
            finally { PrivacyMask.Enabled = previous; }
            return y + 96f;
        }

        private static void FillCard(Graphics g, BattleReportModel model, float x, float y, float w, float h)
        {
            using (GraphicsPath path = Rounded(x, y, w, h, 14f))
            using (SolidBrush fill = new SolidBrush(model.Card))
                g.FillPath(fill, path);
        }

        private static GraphicsPath Rounded(float x, float y, float w, float h, float r)
        {
            GraphicsPath path = new GraphicsPath();
            float d = r * 2f;
            path.AddArc(x, y, d, d, 180f, 90f);
            path.AddArc(x + w - d, y, d, d, 270f, 90f);
            path.AddArc(x + w - d, y + h - d, d, d, 0f, 90f);
            path.AddArc(x, y + h - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }
    }
}
