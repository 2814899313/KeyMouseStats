using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;
internal static class AnomalyRenderTests
{
    [STAThread] static void Main()
    {
        Store.History.Clear();Store.RollDay(DateTime.Today);Store.ThemeId=6;DailySignal.Clear();
        for(int i=0;i<80;i++)Store.History[DateTime.Today.AddDays(-i)]=new DayRecord{Date=DateTime.Today.AddDays(-i),Keys=i==1?6000:1000+i%5*30,ActiveSeconds=3600};
        if(!DailySignal.Get(DateTime.Today.AddDays(-1)).High||DailySignal.Get(DateTime.Today).High)throw new Exception("Anomaly fixture");
        BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using(Dashboard form=new Dashboard())
        using(Bitmap bitmap=new Bitmap(1072,724))
        using(Graphics graphics=Graphics.FromImage(bitmap))
        {
            typeof(Dashboard).GetMethod("OnLoad",flags).Invoke(form,new object[]{EventArgs.Empty});typeof(Dashboard).GetField("_s",flags).SetValue(form,1f);
            FieldInfo tab=typeof(Dashboard).GetField("_tab",flags);tab.SetValue(form,Enum.ToObject(tab.FieldType,1));
            typeof(Dashboard).GetField("_hover",flags).SetValue(form,12);
            typeof(Dashboard).GetMethod("OnPaint",flags).Invoke(form,new object[]{new PaintEventArgs(graphics,new Rectangle(Point.Empty,bitmap.Size))});bitmap.Save("tests/anomaly-trend.png");
            graphics.ResetTransform();tab.SetValue(form,Enum.ToObject(tab.FieldType,5));typeof(Dashboard).GetField("_calendarMetric",flags).SetValue(form,1);
            typeof(Dashboard).GetMethod("OnPaint",flags).Invoke(form,new object[]{new PaintEventArgs(graphics,new Rectangle(Point.Empty,bitmap.Size))});bitmap.Save("tests/anomaly-calendar.png");
            typeof(Dashboard).GetMethod("OnFormClosed",flags).Invoke(form,new object[]{new FormClosedEventArgs(CloseReason.None)});
        }
        Console.WriteLine("PASS: high-day trend/calendar annotations and partial-today exclusion rendered");
    }
}
