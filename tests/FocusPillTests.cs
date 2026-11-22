// ============================================================================
//  tests/FocusPillTests.cs  ·  v3.3 心流胶囊几何、DPI 与多主题渲染
//  不显示真实窗口,只用 DrawToBitmap 做离屏验收。
// ============================================================================
using System;
using System.Drawing;
using KeyMouseStats;

internal static class FocusPillTests
{
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static void Equal(long actual, long expected, string name)
    {
        Check(actual == expected, name + " (expected " + expected + ", got " + actual + ")");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("FocusPillTests", Run);
    }

    private static FocusSnapshot Snapshot()
    {
        return new FocusSnapshot(FlowState.Deep, 14 * 60 + 12,
            @"C:\Apps\Code.exe", "Code.exe", 0.9, 2, 120, 0, false, 2, 0);
    }

    private static void Run()
    {
        Layout();
        RenderThemes();
        Console.WriteLine("FocusPillTests: " + _checks + " 项断言通过");
    }

    private static void Layout()
    {
        Size pill = FocusPillLayout.Size(false, 1f);
        Equal(pill.Width, 224, "胶囊宽度");
        Equal(pill.Height, 38, "胶囊高度");
        Size preview = FocusPillLayout.Size(true, 1f);
        Equal(preview.Width, 248, "预览宽度");
        Equal(preview.Height, 116, "预览高度");
        Size scaled = FocusPillLayout.Size(false, 1.5f);
        Equal(scaled.Width, 336, "1.5x 胶囊宽度");
        Equal(scaled.Height, 57, "1.5x 胶囊高度");

        RectangleF led = FocusPillLayout.LedRect(pill, 1f, false);
        Check(led.X >= 0 && led.Y >= 0 && led.Right <= pill.Width && led.Bottom <= pill.Height,
            "胶囊 LED 必须在窗口内");
        RectangleF state = FocusPillLayout.StateRect(pill, 1f, false);
        RectangleF app = FocusPillLayout.AppRect(pill, 1f, false);
        Check(state.Right <= app.Left + 1, "状态文字与主力应用不得重叠");
        RectangleF previewLed = FocusPillLayout.LedRect(preview, 1f, true);
        Check(previewLed.Bottom < preview.Height, "预览 LED 必须在窗口内");
    }

    private static void RenderThemes()
    {
        int savedTheme = Store.ThemeId;
        try
        {
            int[] themes = { 0, 1, 5, 12 };
            for (int i = 0; i < themes.Length; i++)
            {
                Store.ThemeId = themes[i];
                using (FocusPillForm form = new FocusPillForm(null, null))
                {
                    form.SetScaleForTest(1f);
                    form.UpdateSnapshot(Snapshot());
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        Check(bitmap.Width == 224 && bitmap.Height == 38, "胶囊离屏尺寸");
                    }

                    form.SetPreviewForTest(true);
                    Check(form.ClientSize.Width == 248 && form.ClientSize.Height == 116, "悬停预览几何");
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        Check(bitmap.Width == 248 && bitmap.Height == 116, "预览离屏尺寸");
                    }

                    form.SetScaleForTest(1.5f);
                    form.SetPreviewForTest(false);
                    Check(form.ClientSize.Width == 336 && form.ClientSize.Height == 57, "1.5x 胶囊几何");
                }
            }
        }
        finally
        {
            Store.ThemeId = savedTheme;
        }
    }
}
