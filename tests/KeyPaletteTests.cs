using System;
using System.Collections.Generic;
using System.Drawing;
using KeyMouseStats;

/// <summary>
/// 1.8.0:按键配色的回归。核心不变量只有一条——
/// **同一个按键在任何排名、任何区间、任何次数下都拿到同一个颜色**。
/// 1.8.0 之前颜色按下标取(cols[i]),所以「紫色」的含义是「当前第一名」,
/// 切区间后紫色会换一个键,跨区间对比就成了误读。
/// </summary>
internal static class KeyPaletteTests
{
    private static int checks;

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception(what);
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("KeyPaletteTests", Run);
    }

    private static void Run()
    {
            Store.ThemeId = 1;   // 固定一套主题,颜色期望值才可比

            // ---- 同一个键:颜色只由名字决定 ----
            Color space = KeyPalette.ForKey("空格");
            Check(KeyPalette.ForKey("空格") == space, "the same key keeps the same colour");
            Check(KeyPalette.ForKey("空格") == KeyPalette.ForKey("空格"), "the colour is not random");
            Check(KeyPalette.ForKey("I") != space, "different keys get different colours");
            Check(KeyPalette.ForKey("A") != space && KeyPalette.ForKey("A") != KeyPalette.ForKey("I"),
                "the top three keys are all distinguishable");

            // ---- 真实数据里的高频键:锚点表保证前 10 名两两可区分 ----
            // 只有 10 个身份色,而实测高频键有 12 个,所以第 11、12 名必然复用。
            // 这里断言的是「排进前十时不会撞色」——由 ForRanking 的去重保证。
            string[] top = { "空格", "I", "A", "N", "退格", "E", "W", "F", "H", "S", "D", "K" };
            Color[] topColors = KeyPalette.ForRanking(top);
            Dictionary<int, string> seen = new Dictionary<int, string>();
            for (int i = 0; i < 10; i++)
            {
                int argb = topColors[i].ToArgb();
                Check(!seen.ContainsKey(argb),
                    "the top ten must be distinguishable, but #" + (i + 1) + " reuses the colour of " + (seen.ContainsKey(argb) ? seen[argb] : "?"));
                seen[argb] = top[i];
            }

            // ---- 排名变了,颜色不变(这就是这次改动的全部意义) ----
            // 同一批键,两种顺序:模拟「空格今天第一、本周掉到第三」。
            string[] orderA = { "空格", "I", "A" };
            string[] orderB = { "I", "A", "空格" };
            Color[] colorsA = KeyPalette.ForRanking(orderA);
            Color[] colorsB = KeyPalette.ForRanking(orderB);
            Check(colorsA[0] == colorsB[2], "space keeps its colour when it drops from #1 to #3");
            Check(colorsB[0] == colorsA[1], "the new #1 keeps its own colour instead of taking over purple");
            Check(colorsA[0] != colorsB[0], "the top slot changes colour when a different key holds it");

            // ---- 槽位用完之前不允许撞色(实测高频键共 12 个,前 10 应两两不同) ----
            string[] ten = { "空格", "I", "A", "N", "退格", "E", "W", "F", "H", "S" };
            Color[] tenColors = KeyPalette.ForRanking(ten);
            for (int i = 0; i < tenColors.Length; i++)
                for (int j = i + 1; j < tenColors.Length; j++)
                    Check(tenColors[i] != tenColors[j], "the top ten rows must not repeat a colour");

            // ---- 撞色去重:人为构造两个想拿同色但不在锚点表里的键 ----
            // 锚点表里 "D" 与 "空格" 同为槽位 0;把它们放进前十,后面那个应顺延。
            string[] collide = { "空格", "D", "I", "A", "N", "退格", "E", "W", "F", "H" };
            Color[] fixedColors = KeyPalette.ForRanking(collide);
            Check(fixedColors[0] != fixedColors[1], "a colliding name inside the top ten is shifted to a free slot");

            // ---- 长尾键:仍然稳定,但允许复用颜色(只有六种身份色) ----
            Color[] tail = KeyPalette.ForRanking(new[] { "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20" });
            Check(tail.Length == 8, "long tail rows still get a colour each");
            Color[] again = KeyPalette.ForRanking(new[] { "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20" });
            for (int i = 0; i < tail.Length; i++) Check(tail[i] == again[i], "long tail colours are stable across calls");

            // ---- 空输入与 null 不炸 ----
            Check(KeyPalette.ForRanking(new string[0]).Length == 0, "an empty ranking yields no colours");
            Check(KeyPalette.ForRanking(null).Length == 0, "a null ranking yields no colours");
            Check(KeyPalette.ForKey(null) == KeyPalette.ForKey(null), "a null name still resolves deterministically");
            Check(KeyPalette.ForKey("") == KeyPalette.ForKey(""), "an empty name resolves deterministically");

            // ---- 跨主题:同一按键始终占同一个槽位(颜色数值随主题变,但身份不变) ----
            int[] themes = { 1, 5, 9 };
            for (int i = 0; i < themes.Length; i++)
            {
                Store.ThemeId = themes[i];
                Check(KeyPalette.ForKey("空格") == ArtTheme.Current.Purple,
                    "space always maps to the theme's purple slot (theme " + themes[i] + ")");
                Check(KeyPalette.ForKey("I") == ArtTheme.Current.Cyan,
                    "I always maps to the theme's cyan slot (theme " + themes[i] + ")");
                Check(KeyPalette.ForKey("A") == ArtTheme.Current.Orange,
                    "A always maps to the theme's orange slot (theme " + themes[i] + ")");
                // 槽位之间必须真的不同色,否则「可区分」这条前提就不成立。
                Check(ArtTheme.Current.Purple != ArtTheme.Current.Cyan && ArtTheme.Current.Cyan != ArtTheme.Current.Orange,
                    "theme " + themes[i] + " keeps its palette slots distinct");
            }
            Store.ThemeId = 1;

            // ---- 与热力图的分工:热力图按强度着色,不参与这套身份色 ----
            // 这里只固定一个事实:KeyPalette 只暴露按身份取色的入口,
            // 以免以后有人误把它用到热力图(那里的颜色必须继续表示数值)。
            const System.Reflection.BindingFlags Any =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            Check(typeof(KeyPalette).GetMethod("ForKey", Any) != null && typeof(KeyPalette).GetMethod("ForRanking", Any) != null,
                "the palette exposes identity-based entry points");
            Check(typeof(KeyPalette).GetMethod("ForValue", Any) == null && typeof(KeyPalette).GetMethod("ForIntensity", Any) == null,
                "the palette must not grow a value-based entry point (that is the heatmap's job)");

            Console.WriteLine("PASS: " + checks + " key palette checks — colours bind to the key, not to its rank");
    }
}
