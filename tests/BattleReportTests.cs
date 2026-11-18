// tests/BattleReportTests.cs · v3.0 M3/S3 —— 战报长图:宽度、自适应高度、打码差异、降级不崩
using System;
using System.Drawing;
using KeyMouseStats;

internal static class BattleReportTests
{
    private static int _checks;
    private static void Check(bool v, string n) { if (!v) throw new Exception(n); _checks++; }
    private static void Equal(long a, long e, string n) { Check(a == e, n + "(期望 " + e + ",实际 " + a + ")"); }

    private static void Main() { Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("BattleReportTests", Run); }

    private static BattleReportModel Sample(bool flow)
    {
        BattleReportModel m = new BattleReportModel();
        m.Headline = "今日战报";
        m.Subtitle = "2026-11-16 周一";
        m.Honor = "江湖称号 · 键指如飞";
        m.HasFlow = flow;
        m.FlowScore = 82;
        m.DeepText = "3h20m"; m.NormalText = "1h05m"; m.FragmentedText = "0h40m"; m.LongestFlow = "48m";
        m.Metrics.Add(new BattleReportMetric { Label = "击键", Value = "7,680", Hint = "今日" });
        m.Metrics.Add(new BattleReportMetric { Label = "点击", Value = "1,210", Hint = "今日" });
        m.Metrics.Add(new BattleReportMetric { Label = "活跃", Value = "1时1分", Hint = "今日" });
        m.Metrics.Add(new BattleReportMetric { Label = "累计", Value = "365 天", Hint = "本地记录" });
        m.TopApps.Add("Visual Studio Code - 秘密项目主函数");
        m.TopApps.Add("微信 - 项目群聊");
        m.TopApps.Add("Chrome - 内部文档");
        m.TopAppsCategory = 0;
        m.TopChars = "今天我们交付了三千字";
        m.Footer = "键鼠统计 v3.0";
        return m;
    }

    private static string Digest(Bitmap bmp)
    {
        long sum = 0;
        for (int y = 0; y < bmp.Height; y += 7)
            for (int x = 0; x < bmp.Width; x += 7)
                sum = (sum * 31 + bmp.GetPixel(x, y).ToArgb()) & 0x7FFFFFFF;
        return bmp.Width + "x" + bmp.Height + ":" + sum;
    }

    private static void Run()
    {
        bool before = PrivacyMask.Enabled;
        try
        {
            SizeAndHeight();
            MaskDiffers();
            FlowOptional();
            Degrade();
            NoLeak();
        }
        finally { PrivacyMask.Enabled = before; }
        Console.WriteLine("BattleReportTests: " + _checks + " 项断言通过");
    }

    private static void SizeAndHeight()
    {
        BattleReportModel m = Sample(true);
        using (Bitmap bmp = BattleReport.Render(m, false))
        {
            Equal(bmp.Width, 1080, "战报宽度固定 1080");
            Check(bmp.Height >= 480, "高度至少 480");
            Check(bmp.Height == BattleReport.MeasureHeight(m), "渲染高度与 MeasureHeight 一致");
        }
        Equal(BattleReport.MeasureHeight(Sample(true)), BattleReport.MeasureHeight(Sample(true)), "度量确定性");
    }

    private static void MaskDiffers()
    {
        string masked, plain;
        using (Bitmap bmp = BattleReport.Render(Sample(true), true)) masked = Digest(bmp);
        using (Bitmap bmp = BattleReport.Render(Sample(true), false)) plain = Digest(bmp);
        Check(masked != plain, "打码开启与关闭必须产生像素差异");
        Equal(BattleReport.Render(Sample(true), true).Width, 1080, "打码版仍是 1080 宽");
        Equal(BattleReport.Render(Sample(true), true).Height, BattleReport.Render(Sample(true), false).Height, "打码不改变高度");
    }

    private static void FlowOptional()
    {
        int withFlow = BattleReport.MeasureHeight(Sample(true));
        int without = BattleReport.MeasureHeight(Sample(false));
        Check(withFlow > without, "有心流卡片时长图更高");
        Equal(withFlow - without, 150, "心流卡片高度为 150");
        using (Bitmap bmp = BattleReport.Render(Sample(false), false))
            Check(bmp.Height == without, "无卡片的实拍高度一致");
    }

    private static void Degrade()
    {
        BattleReportModel empty = new BattleReportModel();
        using (Bitmap bmp = BattleReport.Render(empty, true))
        {
            Equal(bmp.Width, 1080, "空模型仍是 1080 宽");
            Check(bmp.Height >= 480, "空模型有最小高度");
        }
        BattleReportModel noApps = Sample(false);
        noApps.TopApps.Clear();
        using (Bitmap bmp = BattleReport.Render(noApps, false))
            Check(bmp.Width == 1080, "无应用榜不崩溃");
    }

    private static void NoLeak()
    {
        PrivacyMask.Enabled = false;
        using (BattleReport.Render(Sample(true), true)) { }
        Check(!PrivacyMask.Enabled, "渲染后必须恢复调用方的打码开关");
        PrivacyMask.Enabled = true;
        using (BattleReport.Render(Sample(true), false)) { }
        Check(PrivacyMask.Enabled, "关闭打码渲染后同样要恢复");
    }
}