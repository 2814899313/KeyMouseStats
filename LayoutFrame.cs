// ============================================================================
//  KeyMouseStats v3.0 · Milestone 2 / Step 3  LayoutFrame.cs
//
//  无 Graphics、无窗体依赖的纯布局计算。所有方法只吃数值、返回矩形/尺寸,
//  因此可以直接被单测覆盖(见 tests/LayoutMetricsTests.cs)。
//
//  坐标约定:
//    · "画布单位"= 96 dpi 下的逻辑像素;窗口像素 = 画布单位 × _s。
//    · 页面内容区原点 (0,0) 是内容区左上角(绘制时已 TranslateTransform(ContentX,ContentY))。
//    · Cx/Cy 是内容区内的基准边距,Cw/Cb 是内容区可用宽高。
//
//  与旧常量的等价性(1072×724、_s=1 时必须逐像素一致):
//    Cw = 1072 - 176 - 24 - 40 = 832  ✓
//    Cb = 724 - 98 - 40        = 586  ✓
//    ContentRight = 24 + 832   = 856  ✓(窗口坐标 1032 = BW - 40)
//
//  C# 5:无 $"…" / ?. / => 成员 / nameof / out var / 元组。
// ============================================================================

using System;
using System.Drawing;

namespace KeyMouseStats
{
    internal static class LayoutFrame
    {
        /// <summary>设计基准画布(最小安全尺寸)。</summary>
        internal const int BaseW = 1072, BaseH = 724;
        /// <summary>侧栏宽度与页头高度。</summary>
        internal const int ContentX = 176, ContentY = 64;
        /// <summary>内容区基准边距。</summary>
        internal const int PageX = 24, PageY = 98;
        /// <summary>内容区右边距(窗口坐标)。</summary>
        internal const int RightMargin = 40;
        /// <summary>可用宽高的下限:再小就必然出现负宽高。</summary>
        internal const float MinWidth = 320f, MinHeight = 240f;
        /// <summary>页脚块高度:PaintFooter 的分割线画在 FrameH - FooterBlockH,按钮在 FrameH - 44。</summary>
        internal const int FooterBlockH = 58;

        /// <summary>客户区宽 → 画布单位宽。</summary>
        internal static float Width(int clientWidth, float s)
        {
            return (clientWidth <= 0 ? 0f : clientWidth) / Sanitize(s);
        }

        /// <summary>客户区高 → 画布单位高。</summary>
        internal static float Height(int clientHeight, float s)
        {
            return (clientHeight <= 0 ? 0f : clientHeight) / Sanitize(s);
        }

        /// <summary>内容区可用宽。</summary>
        internal static float Cw(int clientWidth, float s)
        {
            float value = Width(clientWidth, s) - ContentX - PageX - RightMargin;
            return value < MinWidth ? MinWidth : value;
        }

        /// <summary>
        /// 页面内容区的真实底边(内容坐标,已扣掉 TranslateTransform(ContentX,ContentY) 与页脚块),
        /// 再留 12px 净空不压页脚分割线。
        ///
        /// 注意:不能用 Cy + Cb —— Cb 是按「窗口高 - 顶边距 - 右边距」算的,没有减掉页眉的
        /// ContentY(64)与页脚块(58),直接拿它当内容坐标会落到窗口外面去(基准窗口越界 82px)。
        /// 这里给出的是唯一正确口径:1072×724 → 590;2560×1440 → 1306,差值恰好等于 ElasticH。
        /// </summary>
        internal static float ContentBottom(int clientHeight, float s)
        {
            float value = Height(clientHeight, s) - ContentY - FooterBlockH - 12f;
            return value < 160f ? 160f : value;
        }

        /// <summary>内容区可用高。</summary>
        internal static float Cb(int clientHeight, float s)
        {
            float value = Height(clientHeight, s) - PageY - RightMargin;
            return value < MinHeight ? MinHeight : value;
        }

        /// <summary>内容区右边界(画布单位,内容坐标)。</summary>
        internal static float ContentRight(int clientWidth, float s)
        {
            return PageX + Cw(clientWidth, s);
        }

        /// <summary>窗口坐标下的右边界。</summary>
        internal static float FrameRight(int clientWidth, float s)
        {
            float value = Width(clientWidth, s) - RightMargin;
            return value;
        }

        /// <summary>最小窗口客户区:严格等于基准画布 × 缩放。</summary>
        internal static Size MinClientSize(float s)
        {
            float scale = Sanitize(s);
            return new Size(
                Math.Max(1, (int)Math.Round(BaseW * scale)),
                Math.Max(1, (int)Math.Round(BaseH * scale)));
        }

        /// <summary>
        /// 最小窗口客户区,并按当前显示器工作区封顶。
        ///
        /// 150% / 175% 的 1080p 笔记本上,基准画布的物理最小尺寸(1608×1086 / 1876×1267)
        /// 会超过工作区(约 1920×1040):窗口被顶出屏幕底边后,ClientSize.Height 永远
        /// 小于「最小高」,布局反而把可用高度算成不够用。这里按工作区夹紧,
        /// 保证窗口始终完整落在屏幕内。
        /// </summary>
        internal static Size MinClientSize(float s, Size workArea)
        {
            Size min = MinClientSize(s);
            if (workArea.Width > 0 && min.Width > workArea.Width) min.Width = workArea.Width;
            if (workArea.Height > 0 && min.Height > workArea.Height) min.Height = workArea.Height;
            return min;
        }

        /// <summary>
        /// 期望的客户区尺寸:先按记忆值,再夹进可用工作区;小于最小安全尺寸时抬到最小。
        /// </summary>
        internal static Size FitClientSize(int wantW, int wantH, float s, Size workArea)
        {
            float scale = Sanitize(s);
            Size min = MinClientSize(scale);
            int width = wantW > 0 ? wantW : min.Width;
            int height = wantH > 0 ? wantH : min.Height;
            if (width < min.Width) width = min.Width;
            if (height < min.Height) height = min.Height;
            if (workArea.Width > min.Width && width > workArea.Width) width = workArea.Width;
            if (workArea.Height > min.Height && height > workArea.Height) height = workArea.Height;
            return new Size(width, height);
        }

        /// <summary>按比例切分一行。间距只在相邻列之间,首尾不留额外空白。</summary>
        internal static RectangleF[] SplitColumns(RectangleF area, float[] ratios, float gap)
        {
            return Split(area, ratios, gap, true);
        }

        /// <summary>按比例切分一列。</summary>
        internal static RectangleF[] SplitRows(RectangleF area, float[] ratios, float gap)
        {
            return Split(area, ratios, gap, false);
        }

        private static RectangleF[] Split(RectangleF area, float[] ratios, float gap, bool horizontal)
        {
            int count = ratios == null ? 0 : ratios.Length;
            if (count <= 0) return new RectangleF[0];

            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                float ratio = ratios[i];
                if (ratio < 0f) ratio = 0f;
                sum += ratio;
            }
            if (sum <= 0f)
            {
                for (int i = 0; i < count; i++) ratios[i] = 1f;
                sum = count;
            }

            float total = horizontal ? area.Width : area.Height;
            float spacing = gap > 0f ? gap * (count - 1) : 0f;
            float usable = total - spacing;
            if (usable < 0f) usable = 0f;

            RectangleF[] result = new RectangleF[count];
            float cursor = horizontal ? area.X : area.Y;
            float consumed = 0f;
            for (int i = 0; i < count; i++)
            {
                float length = usable * (Math.Max(0f, ratios[i]) / sum);
                if (i == count - 1) length = Math.Max(0f, usable - consumed);   // 末项吃掉舍入误差
                if (length < 0f) length = 0f;
                consumed += length;
                result[i] = horizontal
                    ? new RectangleF(cursor, area.Y, length, Math.Max(0f, area.Height))
                    : new RectangleF(area.X, cursor, Math.Max(0f, area.Width), length);
                cursor += length + (i < count - 1 ? Math.Max(0f, gap) : 0f);
            }
            return result;
        }

        /// <summary>把矩形夹回 ContentRight 之内,避免任何控件越界。</summary>
        internal static RectangleF ClampRight(RectangleF rect, int clientWidth, float s)
        {
            float limit = ContentRight(clientWidth, s);
            if (rect.Right > limit)
            {
                float width = limit - rect.X;
                rect = new RectangleF(rect.X, rect.Y, width > 0f ? width : 0f, rect.Height);
            }
            if (rect.Width < 0f) rect = new RectangleF(rect.X, rect.Y, 0f, rect.Height);
            return rect;
        }

        // ---------------------------------------------------------------- 内容密度
        //
        //  3.0 的「容器 ↔ 内容」联动:外框随 Cw / ElasticH 生长之后,卡片内部能放几条、
        //  行距多高,必须由可用高度反推,而不是继续写死 Math.Min(10, n) / i * 37f。
        //  这里的两个函数是全站唯一的口径,排行榜、热力图例、日报第三排、字符网格都调它们。

        /// <summary>
        /// 可用内容高度里能排下多少条。availHeight 是卡片总高,headerH/footerH 是卡内
        /// 标题行与页脚行占掉的固定高度,minRowH 是「再挤也不能低于」的行高。
        /// 结果夹在 1 .. totalItems 之间:有数据至少显示 1 条,数据不够就收敛到实际条数。
        /// </summary>
        internal static int CalcVisibleRows(float availHeight, float headerH, float footerH, float minRowH, float maxRowH, int totalItems)
        {
            if (totalItems <= 0) return 0;
            float body = availHeight - headerH - footerH;
            if (body < 0f) body = 0f;
            if (minRowH < 0.5f) minRowH = 0.5f;
            int rows = (int)(body / minRowH);
            if (rows < 1) rows = 1;
            if (rows > totalItems) rows = totalItems;
            // 注意:maxRowH 只约束「行高」,不参与「条数」。条数完全由可用高度 ÷ minRowH 决定,
            // 数据不足时的收敛交给 ConvergeCardHeight —— 否则 2K 上会把 30 行的空间压回 20 行、
            // 再反过来把卡片高度收敛掉,正好造成「卡片长不够、里面还空一截」。
            return rows;
        }

        /// <summary>
        /// 把可用高度平均分给 rows 条,并以上限 maxRowH 封顶。
        /// 行高随窗口均匀呼吸:2K 上既不会把 10 行摊成 100px 的稀疏表格,
        /// 最小窗口里也不会挤到叠字。
        /// </summary>
        internal static float CalcRowHeight(float availHeight, float headerH, float footerH, float minRowH, float maxRowH, int rows)
        {
            float body = availHeight - headerH - footerH;
            if (body < 0f) body = 0f;
            if (rows < 1) rows = 1;
            float height = body / rows;
            if (maxRowH > 0f && height > maxRowH) height = maxRowH;
            if (minRowH > 0f && height < minRowH && minRowH <= body) height = minRowH;
            return height;
        }

        /// <summary>
        /// 数据不足时的卡片高度收敛:弹性高度与「实际内容需要的高度」取小者。
        /// 例:只有 3 个按键时,排行榜卡不该还撑到 2K 屏幕底部,留一大片黑壳。
        /// </summary>
        internal static float ConvergeCardHeight(float elasticHeight, float contentHeight)
        {
            if (contentHeight <= 0f) return elasticHeight;
            return contentHeight < elasticHeight ? contentHeight : elasticHeight;
        }

        /// <summary>数值夹取。浮点比较一律走这里,避免各页各写一遍 if 链。</summary>
        internal static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>整数夹取。</summary>
        internal static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// 等比缩放并居中:给定外框与内容原始宽高,返回内容实际占用的矩形。
        /// 键盘键帽矩阵靠它保持宽高比 —— 横向拉伸会把 1u 键帽画成 3:1 的长条。
        /// </summary>
        internal static RectangleF FitUniform(RectangleF box, float contentWidth, float contentHeight)
        {
            if (contentWidth <= 0f || contentHeight <= 0f || box.Width <= 0f || box.Height <= 0f)
                return new RectangleF(box.X, box.Y, 0f, 0f);
            float scaleX = box.Width / contentWidth, scaleY = box.Height / contentHeight;
            float scale = scaleX < scaleY ? scaleX : scaleY;
            float width = contentWidth * scale, height = contentHeight * scale;
            return new RectangleF(box.X + (box.Width - width) / 2f, box.Y + (box.Height - height) / 2f, width, height);
        }

        /// <summary>缩放系数的兜底:0/负数/NaN 一律按 1 处理。</summary>
        internal static float Sanitize(float s)
        {
            if (s <= 0f) return 1f;
            if (float.IsNaN(s) || float.IsInfinity(s)) return 1f;
            return s;
        }

        /// <summary>矩形是否完全落在给定边界内(容差用于浮点舍入)。</summary>
        internal static bool Within(RectangleF rect, RectangleF bounds, float tolerance)
        {
            return rect.Left >= bounds.Left - tolerance
                && rect.Top >= bounds.Top - tolerance
                && rect.Right <= bounds.Right + tolerance
                && rect.Bottom <= bounds.Bottom + tolerance
                && rect.Width >= -tolerance
                && rect.Height >= -tolerance;
        }
    }
}
