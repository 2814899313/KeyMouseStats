using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;
internal static class ReportVisualTests
{
    [STAThread] static void Main()
    {
        Store.History.Clear();Store.RollDay(DateTime.Today);Store.MouseDpi=1600;
        for(int i=0;i<40;i++)
        {
            DayRecord day=new DayRecord {Date=DateTime.Today.AddDays(-i),Keys=3500+(i*191)%4100,Clicks=1400+i*11,ActiveSeconds=14400,AppObservedSeconds=13000,Wheel=2300};
            day.ComboCounts["ctrl_c"]=150;day.MoveMeters=120;
            day.Apps["editor"]=new AppUsage{ProcessPath="C:\\Tools\\Code.exe",Keys=2000,ActiveSeconds=7000};
            day.Apps["browser"]=new AppUsage{ProcessPath="C:\\Tools\\Browser.exe",Keys=1000,ActiveSeconds=3200};
            day.Apps["game"]=new AppUsage{ProcessPath="C:\\Tools\\Minecraft.exe",Keys=500,ActiveSeconds=1200};
            for(int h=9;h<18;h+=2)day.Sessions.Add(new ActiveSession {Start=day.Date.AddHours(h),End=day.Date.AddHours(h).AddMinutes(42),Seconds=2520});
            day.KeyCounts[87]=1600;day.KeyCounts[49]=500;day.KeyCounts[32]=400;day.KeyCounts[162]=200;
            foreach(ActiveSession session in day.Sessions){CrossTelemetry.Interval(day,session,@"C:\Tools\Code.exe",session.Start,session.Start.AddMinutes(24));CrossTelemetry.Interval(day,session,@"C:\Tools\Browser.exe",session.Start.AddMinutes(24),session.End);}
            day.Cross.Direction[0]=3200;day.Cross.Direction[1]=1800;day.Cross.Direction[2]=900;day.Cross.Direction[3]=1100;day.Cross.Packets=100;day.Cross.CalibratedPackets=100;day.Cross.PathCounts=5900;day.Cross.DragCounts=1100;day.Cross.Flicks=8;day.Cross.Reversals=3;day.Cross.Clicks=100;day.Cross.MoveClicks=70;
            Store.History[day.Date]=day;
        }
        BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        int renders=0;
        foreach(int theme in new[]{0,5,6,7,8})foreach(int width in new[]{820,1040})
        using(StatisticsReport report=new StatisticsReport(DateTime.Today))
        {
            Store.ThemeId=theme;
            report.ClientSize=new Size(width,width==820?600:760);IntPtr handle=report.Handle;
            typeof(StatisticsReport).GetMethod("OnLoad",flags).Invoke(report,new object[]{EventArgs.Empty});
            ReportDeck tabs=(ReportDeck)typeof(StatisticsReport).GetField("_tabs",flags).GetValue(report);
            report.ClientSize=new Size(width,width==820?700:900);report.LayoutReport();
            for(int p=0;p<11;p++)
            {
                tabs.SelectedIndex=p;CreateHandles(report);report.PerformLayout();
                using(Bitmap bitmap=new Bitmap(report.Width,report.Height))
                {report.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));if(width==1040&&theme==7)bitmap.Save("tests/report-preview-"+p+".png");}
                tabs.SelectedTab.DetailsOpen=true;tabs.PerformLayout();
                ListView list=null;Control chart=null;
                foreach(Control child in tabs.SelectedTab.Controls){if(child is ListView)list=(ListView)child;if(child is ReportVisual||child is CrossReportVisual)chart=child;}
                if(list==null||chart==null||list.Height<65||list.Top<chart.Bottom)throw new Exception("Report layout overlap");
                renders++;
            }
        }
        Store.History.Clear();Store.RollDay(DateTime.Today);
        for(int p=0;p<11;p++)using(Control chart=p<5?(Control)new ReportVisual(DateTime.Today,p):new CrossReportVisual(DateTime.Today,p))
        using(Bitmap bitmap=new Bitmap(1560,520))
        {
            chart.Size=bitmap.Size;bitmap.SetResolution(144,144);
            using(Graphics graphics=Graphics.FromImage(bitmap))chart.GetType().GetMethod("OnPaint",flags).Invoke(chart,new object[]{new PaintEventArgs(graphics,new Rectangle(Point.Empty,bitmap.Size))});
        }
        using(StatisticsReport empty=new StatisticsReport(DateTime.Today.AddYears(-2)))
        {CreateHandles(empty);using(Bitmap bitmap=new Bitmap(empty.Width,empty.Height))empty.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));}
        Console.WriteLine("PASS: "+renders+" report renders across five themes/two widths; chart/table separation and missing-day fallback");
    }
    static void CreateHandles(Control control){IntPtr handle=control.Handle;foreach(Control child in control.Controls)CreateHandles(child);}
}


