using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;

internal static class HourlyCompareRender
{
    [STAThread] private static void Main()
    {
        Store.History.Clear(); Store.ThemeId = 9;
        foreach (int ago in new[] { 0, 1, 7 })
        {
            DayRecord day = new DayRecord { Date = DateTime.Today.AddDays(-ago), ActiveSeconds = 7200 };
            for (int h = 0; h < 24; h++)
            {
                day.HourKeys[h] = h < 7 || h > 21 ? 0 : (long)(120 + 520 * Math.Abs(Math.Sin((h + ago) * .47)));
                day.HourClicks[h] = day.HourKeys[h] / 4;
                day.ActiveHours[h] = h < 7 || h > 21 ? 0 : 300 + day.HourKeys[h] * 3;
            }
            Store.History[day.Date] = day;
        }
        Store.RollDay(DateTime.Today);
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using (Dashboard form = new Dashboard())
        using (Bitmap bitmap = new Bitmap(1072, 724))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            typeof(Dashboard).GetMethod("OnLoad", flags).Invoke(form, new object[] { EventArgs.Empty });
            typeof(Dashboard).GetField("_s", flags).SetValue(form, 1f);
            FieldInfo tab = typeof(Dashboard).GetField("_tab", flags); tab.SetValue(form, Enum.ToObject(tab.FieldType, 1));
            typeof(Dashboard).GetField("_trendHourly", flags).SetValue(form, true);
            typeof(Dashboard).GetField("_hourlyCompareMode", flags).SetValue(form, 4);
            typeof(Dashboard).GetField("_mouse", flags).SetValue(form, new Point(590, 350));
            typeof(Dashboard).GetMethod("OnPaint", flags).Invoke(form, new object[] { new PaintEventArgs(graphics, new Rectangle(Point.Empty, bitmap.Size)) });
            IList rects = (IList)typeof(Dashboard).GetField("_chips", flags).GetValue(form);
            IList ids = (IList)typeof(Dashboard).GetField("_chipIds", flags).GetValue(form);
            int[] controls = { 300, 301, 310, 311, 312, 313, 314, 322, 326 };
            for (int a = 0; a < controls.Length; a++) for (int b = a + 1; b < controls.Length; b++)
            {
                int ia = -1, ib = -1;
                for (int i = 0; i < ids.Count; i++) { if ((int)ids[i] == controls[a]) ia = i; if ((int)ids[i] == controls[b]) ib = i; }
                if (ia < 0 || ib < 0) throw new Exception("missing hourly control");
                if (((RectangleF)rects[ia]).IntersectsWith((RectangleF)rects[ib])) throw new Exception("overlapping hourly controls: " + controls[a] + ", " + controls[b]);
            }
            bitmap.Save("previews/trend-hourly-multiline-1.3.2.png", ImageFormat.Png);
            typeof(Dashboard).GetMethod("OnFormClosed", flags).Invoke(form, new object[] { new FormClosedEventArgs(CloseReason.None) });
        }
        Console.WriteLine("PASS: hourly comparison rendered");
    }
}
