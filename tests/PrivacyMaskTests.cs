// ============================================================================
//  tests/PrivacyMaskTests.cs  ·  v3.0 Milestone 3 / Step 1
//  脱敏纯函数:幂等、结构保留、默认关闭。
// ============================================================================
using System;
using System.Collections.Generic;
using KeyMouseStats;

internal static class PrivacyMaskTests
{
    private static int _checks;
    private static void Check(bool v, string n) { if (!v) throw new Exception(n); _checks++; }
    private static void Equal(long a, long e, string n) { Check(a == e, n + "(期望 " + e + ",实际 " + a + ")"); }
    private static void Same(string a, string e, string n) { Check(string.Equals(a, e, StringComparison.Ordinal), n + "(期望「" + e + "」,实际「" + a + "」)"); }

    private static void Main() { Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("PrivacyMaskTests", Run); }

    private static void Run()
    {
        try
        {
            DefaultsOff();
            TitleMasking();
            TextMasking();
            PathMasking();
            CategoryTable();
            Console.WriteLine("PrivacyMaskTests: " + _checks + " 项断言通过");
        }
        finally { PrivacyMask.Enabled = PrivacyMask.DefaultEnabled; }
    }

    private static void DefaultsOff()
    {
        Check(!PrivacyMask.DefaultEnabled, "战报默认关闭打码(拍板决策)");
        PrivacyMask.Enabled = false;
        Same(PrivacyMask.MaskTitle("秘密项目 - Visual Studio Code", 0), "秘密项目 - Visual Studio Code", "关闭时标题原样");
        Same(PrivacyMask.MaskText("今天写了三千字"), "今天写了三千字", "关闭时文本原样");
        Same(PrivacyMask.MaskPath(@"C:\Users\me\docs\plan.md"), @"C:\Users\me\docs\plan.md", "关闭时路径原样");
    }

    private static void TitleMasking()
    {
        PrivacyMask.Enabled = true;
        Same(PrivacyMask.MaskTitle("秘密项目 - Visual Studio Code", 0), "编程 · 已隐藏", "编程类标题脱敏");
        Same(PrivacyMask.MaskTitle("群聊", 8), "沟通 · 已隐藏", "沟通类标题脱敏");
        Same(PrivacyMask.MaskTitle("任意", 9), "娱乐 · 已隐藏", "娱乐类标题脱敏");
        Same(PrivacyMask.MaskTitle("任意", 99), "其他 · 已隐藏", "越界类别回落其他");
        Same(PrivacyMask.MaskTitle("任意", -3), "其他 · 已隐藏", "负类别回落其他");
        Same(PrivacyMask.MaskTitle(null, 0), "编程 · 已隐藏", "null 标题也脱敏");
        string once = PrivacyMask.MaskTitle("x", 0);
        Same(PrivacyMask.MaskTitle(once, 0), once, "标题脱敏幂等");
        Same(PrivacyMask.MaskTitle(PrivacyMask.MaskTitle("x", 5), 5), "办公 · 已隐藏", "二次脱敏结果稳定");

        List<string> masked = PrivacyMask.MaskTitles(new string[] { "a", "b" }, 2);
        Equal(masked.Count, 2, "批量脱敏数量一致");
        Same(masked[0], "视频 · 已隐藏", "批量脱敏内容");
        Equal(PrivacyMask.MaskTitles(null, 0).Count, 0, "null 集合返回空");
    }

    private static void TextMasking()
    {
        PrivacyMask.Enabled = true;
        string source = "今天 写了 三千字";
        string masked = PrivacyMask.MaskText(source);
        Equal(masked.Length, source.Length, "脱敏后长度不变(保留词长分布)");
        Check(masked.IndexOf('\uFF0A') >= 0, "脱敏后含全角星号");
        Check(masked.IndexOf(' ') >= 0, "空白结构保留");
        Same(PrivacyMask.MaskText(masked), masked, "文本脱敏幂等");
        Same(PrivacyMask.MaskText(""), "", "空串保持空");
        Same(PrivacyMask.MaskText(null), "", "null 文本返回空串");

        string english = "hello world";
        string maskedEn = PrivacyMask.MaskText(english);
        Equal(maskedEn.Length, english.Length, "英文同样保长");
        Check(maskedEn.IndexOf('w') < 0, "英文原文被替换");
    }

    private static void PathMasking()
    {
        PrivacyMask.Enabled = true;
        Same(PrivacyMask.MaskPath(@"C:\Users\me\docs\plan.md"), "plan.md", "路径只保留文件名");
        Same(PrivacyMask.MaskPath(@"C:\Users\me\docs\plan.md"), "plan.md", "路径脱敏幂等");
        Same(PrivacyMask.MaskPath(""), "", "空路径返回空");
        Same(PrivacyMask.MaskPath(null), "", "null 路径返回空");
        string fileName = PrivacyMask.MaskPath("/home/u/a/b/c.txt");
        Check(fileName.IndexOf("home") < 0, "目录部分被去掉");
    }

    private static void CategoryTable()
    {
        string[] expected = { "编程", "游戏", "视频", "其他", "未分类", "办公", "科研", "写作", "沟通", "娱乐" };
        PrivacyMask.Enabled = true;
        for (int i = 0; i < expected.Length; i++)
        {
            Same(PrivacyMask.CategoryName(i), expected[i], "第 " + i + " 类名称");
            Same(PrivacyMask.MaskTitle("任意标题", i), expected[i] + " · 已隐藏", "第 " + i + " 类脱敏");
        }
        Same(PrivacyMask.CategoryName(-1), "其他", "负类别回落");
        Same(PrivacyMask.CategoryName(99), "其他", "越界类别回落");
    }
}
