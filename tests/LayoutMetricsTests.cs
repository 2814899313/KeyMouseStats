// ============================================================================
//  tests/LayoutMetricsTests.cs  ·  v3.0 Milestone 2 / Step 3(3.1)
//
//  纯函数断言:弹性布局引擎在四档分辨率下都必须
//    · 与 1072×724 基准逐像素等价(Cw=832 / Cb=586 / ContentRight=856);
//    · 放大时内容区严格扩张,不出现负宽高;
//    · 任何比例切分都完整铺满、互不重叠、末端不越过 ContentRight;
//    · 最小窗口尺寸严格等于 基准 × 缩放。
//
//  不创建窗体、不依赖 Graphics、不读写任何文件。
// ============================================================================

using System;
using System.Drawing;
using KeyMouseStats;

internal static class LayoutMetricsTests
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

    private static void Near(float actual, float expected, float tolerance, string name)
    {
        Check(Math.Abs(actual - expected) <= tolerance,
            name + "(期望 " + expected.ToString("0.##") + ",实际 " + actual.ToString("0.##") + ")");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("LayoutMetricsTests", Run);
    }

    private static void Run()
    {
        BaselineIdentity();
        MinimumSizeClamp();
        FourResolutions();
        SplitGuarantees();
        SanitizeEdgeCases();
        VerticalElasticity();
        Console.WriteLine("LayoutMetricsTests: " + _checks + " 项断言通过");
    }

    /// <summary>1. 基准尺寸必须与旧常量逐像素一致。</summary>
    private static void BaselineIdentity()
    {
        Equal(LayoutFrame.BaseW, 1072, "基准宽");
        Equal(LayoutFrame.BaseH, 724, "基准高");
        Near(LayoutFrame.Cw(1072, 1f), 832f, 0.01f, "基准 Cw 必须仍为 832");
        Near(LayoutFrame.Cb(724, 1f), 586f, 0.01f, "基准 Cb 必须仍为 586");
        Near(LayoutFrame.ContentRight(1072, 1f), 856f, 0.01f, "基准 ContentRight 必须仍为 856");
        Near(LayoutFrame.FrameRight(1072, 1f), 1032f, 0.01f, "基准 FrameRight 必须仍为 1032");
        Near(LayoutFrame.Width(1072, 1f), 1072f, 0.01f, "画布宽");
        Near(LayoutFrame.Height(724, 1f), 724f, 0.01f, "画布高");
    }

    /// <summary>2. 最小尺寸严格等于基准 × 缩放,且 FitClientSize 永不小于它。</summary>
    private static void MinimumSizeClamp()
    {
        Size one = LayoutFrame.MinClientSize(1f);
        Equal(one.Width, 1072, "100% 最小宽");
        Equal(one.Height, 724, "100% 最小高");
        Size half = LayoutFrame.MinClientSize(1.5f);
        Equal(half.Width, 1608, "150% 最小宽");
        Equal(half.Height, 1086, "150% 最小高");

        Size work = new Size(1920, 1040);
        Size tiny = LayoutFrame.FitClientSize(200, 100, 1f, work);
        Equal(tiny.Width, 1072, "过小请求应被抬到最小宽");
        Equal(tiny.Height, 724, "过小请求应被抬到最小高");

        Size huge = LayoutFrame.FitClientSize(4000, 3000, 1f, work);
        Check(huge.Width <= work.Width && huge.Height <= work.Height, "过大请求应夹进工作区");

        Size kept = LayoutFrame.FitClientSize(1280, 860, 1f, work);
        Equal(kept.Width, 1280, "合法请求应原样保留");
        Equal(kept.Height, 860, "合法请求应原样保留");

        // 3.0:150% / 175% 的 1080p 笔记本上,基准画布的物理最小高(1086 / 1267)超过工作区
        // 高度,不夹紧就会把窗口顶出屏幕底边,ClientSize 反而永远小于「最小高」。
        Size work1080 = new Size(1920, 1040);
        Size min150 = LayoutFrame.MinClientSize(1.5f, work1080);
        Equal(min150.Width, 1608, "150% 最小宽仍为基准×缩放");
        Equal(min150.Height, 1040, "150% 最小高应夹到工作区高");
        Check(min150.Width <= work1080.Width && min150.Height <= work1080.Height,
            "150% 最小尺寸不得超出工作区");
        Size min175 = LayoutFrame.MinClientSize(1.75f, work1080);
        Check(min175.Width <= work1080.Width, "175% 最小宽不得超出工作区");
        Equal(min175.Height, 1040, "175% 最小高应夹到工作区高");
        Check(min175.Height <= work1080.Height, "175% 最小尺寸不得超出工作区");

        // 工作区足够大时不夹紧,口径与旧版一致。
        Size roomy = LayoutFrame.MinClientSize(1.5f, new Size(2560, 1440));
        Equal(roomy.Width, 1608, "工作区充裕时 150% 最小宽不变");
        Equal(roomy.Height, 1086, "工作区充裕时 150% 最小高不变");

        // 未知工作区(0×0)不得把最小尺寸压成 0。
        Size unknown = LayoutFrame.MinClientSize(1.5f, Size.Empty);
        Equal(unknown.Width, 1608, "未知工作区时保留最小宽");
        Equal(unknown.Height, 1086, "未知工作区时保留最小高");
    }

    /// <summary>3. 四档分辨率:扩张、非负、且右边界不溢出。</summary>
    private static void FourResolutions()
    {
        int[][] sizes = new int[][]
        {
            new int[] { 1072, 724 },
            new int[] { 1280, 860 },
            new int[] { 1600, 1000 },
            new int[] { 1920, 1080 }
        };
        float lastCw = 0f;
        for (int i = 0; i < sizes.Length; i++)
        {
            int w = sizes[i][0], h = sizes[i][1];
            float cw = LayoutFrame.Cw(w, 1f);
            float cb = LayoutFrame.Cb(h, 1f);
            float right = LayoutFrame.ContentRight(w, 1f);
            Check(cw > 0f, w + "×" + h + " Cw 必须为正");
            Check(cb > 0f, w + "×" + h + " Cb 必须为正");
            Check(cw >= lastCw, w + "×" + h + " 放大后 Cw 不应缩小");
            Near(right, LayoutFrame.PageX + cw, 0.01f, w + "×" + h + " ContentRight = Cx + Cw");
            Check(right <= LayoutFrame.Width(w, 1f), w + "×" + h + " 内容右边界不得越过窗口");
            Check(LayoutFrame.PageX + cw + LayoutFrame.RightMargin <= LayoutFrame.Width(w, 1f) + 0.01f,
                w + "×" + h + " 内容区 + 右边距不得超出画布宽");
            lastCw = cw;
        }
        // 1920 宽时内容区应明显比基准宽
        Check(LayoutFrame.Cw(1920, 1f) > LayoutFrame.Cw(1072, 1f) + 500f, "1920 宽下 Cw 应大幅扩张");
        // 150% 缩放下同一物理分辨率得到相同的画布单位
        Near(LayoutFrame.Cw(1608, 1.5f), 832f, 0.01f, "1608px @150% 等价于 1072 画布单位");
    }

    /// <summary>4. 比例切分:铺满、不重叠、末端不越界、无负宽高。</summary>
    private static void SplitGuarantees()
    {
        float[][] ratioSets = new float[][]
        {
            new float[] { 0.485f, 0.515f },
            new float[] { 0.721f, 0.279f },
            new float[] { 0.673f, 0.327f },
            new float[] { 0.493f, 0.507f },
            new float[] { 1f, 1f, 1f, 1f }
        };
        int[][] sizes = new int[][]
        {
            new int[] { 1072, 724 }, new int[] { 1280, 860 },
            new int[] { 1600, 1000 }, new int[] { 1920, 1080 }
        };
        for (int s = 0; s < sizes.Length; s++)
        {
            int w = sizes[s][0], h = sizes[s][1];
            float cw = LayoutFrame.Cw(w, 1f);
            float cb = LayoutFrame.Cb(h, 1f);
            RectangleF area = new RectangleF(LayoutFrame.PageX, LayoutFrame.PageY, cw, cb);
            float limit = LayoutFrame.ContentRight(w, 1f);
            for (int r = 0; r < ratioSets.Length; r++)
            {
                float[] ratios = (float[])ratioSets[r].Clone();
                RectangleF[] cols = LayoutFrame.SplitColumns(area, ratios, 12f);
                Check(cols.Length == ratioSets[r].Length, "列数应与比例数一致");
                float previousRight = float.MinValue;
                for (int i = 0; i < cols.Length; i++)
                {
                    Check(cols[i].Width >= 0f, "列宽不得为负");
                    Check(cols[i].Height > 0f, "列高不得为零");
                    Check(cols[i].Left >= previousRight - 0.01f, "列之间不得重叠");
                    Check(cols[i].Right <= limit + 0.01f, "列右边界不得越过 ContentRight");
                    previousRight = cols[i].Right;
                }
                Near(cols[cols.Length - 1].Right, area.Right, 0.05f, "最后一列应紧贴区域右边界");

                float[] rowRatios = (float[])ratioSets[r].Clone();
                RectangleF[] rows = LayoutFrame.SplitRows(area, rowRatios, 12f);
                float previousBottom = float.MinValue;
                for (int i = 0; i < rows.Length; i++)
                {
                    Check(rows[i].Height >= 0f, "行高不得为负");
                    Check(rows[i].Top >= previousBottom - 0.01f, "行之间不得重叠");
                    Check(rows[i].Bottom <= area.Bottom + 0.05f, "行底边不得越过内容区");
                    previousBottom = rows[i].Bottom;
                }
                Near(rows[rows.Length - 1].Bottom, area.Bottom, 0.05f, "最后一行应紧贴区域底边");
            }
        }

        // 全零比例不应除以零
        RectangleF[] zero = LayoutFrame.SplitColumns(new RectangleF(0, 0, 100, 10), new float[] { 0f, 0f }, 2f);
        Check(zero.Length == 2 && zero[0].Width > 0f && zero[1].Width > 0f, "全零比例应退化为均分");
    }

    /// <summary>5. 非法输入兜底。</summary>
    private static void SanitizeEdgeCases()
    {
        Near(LayoutFrame.Sanitize(0f), 1f, 0.0001f, "s=0 按 1 处理");
        Near(LayoutFrame.Sanitize(-2f), 1f, 0.0001f, "负缩放按 1 处理");
        Near(LayoutFrame.Sanitize(float.NaN), 1f, 0.0001f, "NaN 按 1 处理");
        Check(LayoutFrame.Cw(0, 1f) > 0f, "零客户区宽也应返回正的最小宽");
        Check(LayoutFrame.Cb(0, 1f) > 0f, "零客户区高也应返回正的最小高");

        RectangleF area = new RectangleF(0, 0, 200, 100);
        Check(LayoutFrame.Within(new RectangleF(0, 0, 200, 100), area, 0.01f), "完全重合应判定在内");
        Check(!LayoutFrame.Within(new RectangleF(0, 0, 260, 100), area, 0.01f), "溢出应判定在外");
        Check(!LayoutFrame.Within(new RectangleF(0, 0, -5, 100), area, 0f), "负宽应判定在外");

        RectangleF clamped = LayoutFrame.ClampRight(new RectangleF(800, 0, 200, 10), 1072, 1f);
        Check(clamped.Right <= LayoutFrame.ContentRight(1072, 1f) + 0.01f, "ClampRight 应把越界矩形收回");
        Check(clamped.Width >= 0f, "ClampRight 不得产生负宽");
    }

    /// <summary>6. 纵向弹性:主卡随 Cb 拉伸,底边与页脚保持安全距离。</summary>
    private static void VerticalElasticity()
    {
        int[][] sizes = new int[][]
        {
            new int[] { 1072, 724 },
            new int[] { 1280, 860 },
            new int[] { 1440, 900 },
            new int[] { 1920, 1080 }
        };
        float previous = -1f;
        for (int i = 0; i < sizes.Length; i++)
        {
            int w = sizes[i][0], h = sizes[i][1];
            float cb = LayoutFrame.Cb(h, 1f);
            float elastic = Math.Max(0f, cb - 586f);
            Check(elastic >= 0f, w + "×" + h + " 纵向弹性不得为负");
            if (i > 0) Check(elastic >= previous, w + "×" + h + " 放大后弹性不得减小");
            previous = elastic;

            // 总览:两行数字卡 + 主视觉卡 + 洞察胶囊,全部落在内容区内
            float cardY = LayoutFrame.PageY + 208f;          // Cy + 2*88 + 32
            float cardH = 234f + elastic;
            float pillBottom = cardY + cardH + 14f + 24f;
            Check(pillBottom <= LayoutFrame.PageY + cb + 0.5f,
                w + "×" + h + " 洞察胶囊不得越过内容区底边");
            Check(cardH >= 234f, w + "×" + h + " 主视觉卡不得小于基准高度");

            // 页脚分割线必须在内容区之下
            float footerLine = LayoutFrame.Height(h, 1f) - 58f;
            Check(footerLine >= LayoutFrame.PageY + 208f, w + "×" + h + " 页脚线不得压在数字卡上");

            // 点击热区:左右卡同样拉伸,画布不越过卡片底边
            float trayBottom = 266f + 296f + elastic;
            Check(trayBottom <= 138f + 438f + elastic + 0.5f,
                w + "×" + h + " 热区画布不得越出卡片");
        }

        // 基准尺寸下弹性必须恰好为 0(旧布局逐像素不变)
        Near(Math.Max(0f, LayoutFrame.Cb(724, 1f) - 586f), 0f, 0.01f, "基准高度弹性为 0");
        Near(234f + Math.Max(0f, LayoutFrame.Cb(724, 1f) - 586f), 234f, 0.01f, "基准主视觉卡高度不变");
    }
}
