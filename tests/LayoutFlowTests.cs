// tests/LayoutFlowTests.cs · v3.0 重写 —— 布局原语:游标推进、权重分配、省略截断
using System;
using System.Drawing;
using KeyMouseStats;

internal static class LayoutFlowTests
{
    private static int _c;
    private static void C(bool v, string n) { if (!v) throw new Exception(n); _c++; }
    private static void E(long a, long e, string n) { C(a == e, n + "(期望 " + e + " 实际 " + a + ")"); }
    private static void N(double a, double e, double tol, string n) { C(Math.Abs(a - e) <= tol, n + "(期望 " + e + " 实际 " + a + ")"); }

    private static void Main() { Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("LayoutFlowTests", Run); }

    private static void Run()
    {
        FlowCursor();
        Distribute();
        Fit();
        Console.WriteLine("LayoutFlowTests: " + _c + " 项断言通过");
    }

    private static void FlowCursor()
    {
        Flow f = new Flow(24f, 100f, 800f);
        E((long)f.Next(40f), 100, "首行从起点开始");
        E((long)f.Next(30f), 140, "第二行接着排");
        N(f.Used, 70, 0.01, "已用高度 = 累加行高");
        f.Gap(12f);
        E((long)f.Y, 182, "间距推进游标");
        N(f.Remaining(200f), 18, 0.01, "剩余空间 = 底边 - 当前");
        N(f.Remaining(100f), 0, 0.01, "越过底边时剩余为 0");
        f.MoveTo(60f);
        N(f.Used, -40, 0.01, "贴底对齐可以反向移动");
        C(f.X == 24f && f.Width == 800f, "水平位置与宽度不变");
    }

    private static void Distribute()
    {
        float[] w = RowLayout.Distribute(1000f, 10f, new float[] { 200f, 0f, 0f }, new float[] { 0f, 1f, 1f }, new float[] { 100f, 100f, 100f });
        E(3, w.Length, "三列");
        N(w[0], 200, 0.01, "固定列不变");
        N(w[1], 390, 0.01, "剩余按权重均分");
        N(w[2], 390, 0.01, "剩余按权重均分");
        N(w[0] + w[1] + w[2] + 20, 1000, 0.01, "总宽扣间隙后恰好用满");

        float[] m = RowLayout.Distribute(300f, 0f, new float[] { 0f, 0f }, new float[] { 1f, 1f }, new float[] { 200f, 200f });
        N(m[0], 200, 0.01, "不足时抬到最小宽");
        float[] z = RowLayout.Distribute(100f, 8f, new float[] { 0f, 0f, 0f }, new float[] { 3f, 1f, 0f }, new float[] { 10f, 10f, 10f });
        N(z[0], 3 * (100 - 16) / 4.0, 0.01, "权重 3:1:非弹性按比例");
        N(z[2], 10, 0.01, "权重 0 的列只拿到最小宽");
        N(RowLayout.Even(1000f, 3, 10f), 326.666, 0.01, "均分扣间隙");
        N(RowLayout.Even(100f, 0, 0f), 0, 0.01, "零列返回 0");
        E(0, RowLayout.Distribute(100f, 0f, null, null, null).Length, "null 输入返回空");
    }

    private static void Fit()
    {
        using (Bitmap bmp = new Bitmap(8, 8))
        using (Graphics g = Graphics.FromImage(bmp))
        using (Font font = new Font("Microsoft YaHei", 12f))
        {
            C(TextFit.Fits(g, "abc", font, 400f), "短文本放得下");
            C(!TextFit.Fits(g, new string('国', 80), font, 100f), "长文本放不下");
            C(TextFit.LineHeight(g, font) > 0, "行高由字体实测");
            string shortText = TextFit.Ellipsis(g, "abc", font, 400f);
            C(shortText == "abc", "放得下时原样返回");
            string cut = TextFit.Ellipsis(g, new string('国', 60), font, 100f);
            C(cut.EndsWith("…"), "超宽时以省略号结尾");
            C(g.MeasureString(cut, font).Width <= 101f, "省略后不超宽");
            C(TextFit.VisibleChars(g, "abcdef", font, 400f) == 6, "全放得下时字符数完整");
            C(TextFit.VisibleChars(g, new string('国', 60), font, 100f) < 60, "超宽时可显示字符数减少");
            C(TextFit.Ellipsis(g, "", font, 100f) == "", "空串返回空");
            C(TextFit.VisibleChars(g, "", font, 100f) == 0, "空串零字符");
        }
    }
}