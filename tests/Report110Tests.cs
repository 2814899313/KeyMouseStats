using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;
internal static class Report110Tests
{
    static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Handles(Control c){IntPtr h=c.Handle;foreach(Control child in c.Controls)Handles(child);}
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    [STAThread]static void Main(){try{Run();}catch(Exception e){Console.WriteLine(e.Message);Console.WriteLine(e.StackTrace);Environment.ExitCode=1;}}
    static void Run()
    {
        Store.MouseDpi=1600;
        foreach(int days in new[]{0,1,7})
        {
            Store.History.Clear();Store.RollDay(DateTime.Today);
            Store.Today.Keys=930;Store.Today.Clicks=845;Store.Today.ActiveSeconds=4395;Store.Today.MoveMeters=105.555;Store.Today.Wheel=1000;Store.Today.ComboCounts["ctrl_c"]=26;
            for(int i=1;i<=days;i++)Store.History[DateTime.Today.AddDays(-i)]=new DayRecord{Date=DateTime.Today.AddDays(-i),Keys=800+i*100,Clicks=700,ActiveSeconds=3600,MoveMeters=100};
            foreach(int theme in new[]{0,1,2,3,4,5,6,7,8})foreach(float scale in new[]{1f,1.5f})
            using(StatisticsReport report=new StatisticsReport(DateTime.Today))
            {
                Store.ThemeId=theme;IntPtr handle=report.Handle;typeof(StatisticsReport).GetMethod("OnLoad",F).Invoke(report,new object[]{EventArgs.Empty});
                typeof(StatisticsReport).GetField("_scale",F).SetValue(report,scale);report.MinimumSize=Size.Empty;report.ClientSize=new Size((int)(1040*scale),(int)(840*scale));report.LayoutReport();
                ReportDeck deck=(ReportDeck)typeof(StatisticsReport).GetField("_tabs",F).GetValue(report);ReportPage page=deck.SelectedTab;
                Check(page.List.Columns.Count==(days>=7?5:3),"conditional baseline columns");Check(page.List.Items.Count==6,"six meaningful metrics");Check(!page.DetailsOpen,"details collapsed by default");
                string csv=StatisticsReport.BuildCsv(page.List);Check(csv.Contains((930.0/845).ToString("R",System.Globalization.CultureInfo.InvariantCulture)),"export keeps full precision");Check(!csv.Contains("样本不足"),"no repeated unavailable cells");
                Check(page.List.Items[0].ToolTipText.Contains("点击次数"),"definition in tooltip");
                Handles(report);using(Bitmap bitmap=new Bitmap(report.Width,report.Height)){report.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));if(days==1&&scale==1&&theme==8)bitmap.Save("tests/report-110-minecraft.png");if(days==7&&scale==1&&theme==5)bitmap.Save("tests/report-110-nikki.png");}
                ReportVisual chart=(ReportVisual)page.Chart;RectangleF[] bounds=(RectangleF[])typeof(ReportVisual).GetField("metricBounds",F).GetValue(chart);
                Check(bounds[5].Bottom*scale<=chart.Height,"cards fit DPI canvas");
                if(bounds[0].Width==0){using(Bitmap canvas=new Bitmap(chart.Width,chart.Height))chart.DrawToBitmap(canvas,new Rectangle(Point.Empty,canvas.Size));}
                MouseEventArgs click=new MouseEventArgs(MouseButtons.Left,1,(int)((bounds[0].X+25)*scale),(int)((bounds[0].Y+25)*scale),0);
                typeof(ReportVisual).GetMethod("OnMouseMove",F).Invoke(chart,new object[]{click});typeof(ReportVisual).GetMethod("OnMouseDown",F).Invoke(chart,new object[]{click});
                Check(chart.SelectedMetricText!=null&&chart.SelectedMetricText.Contains("1.10059"),"metric selection preserves precision: "+chart.SelectedMetricText+" scale="+scale+" theme="+theme);
                page.DetailsOpen=true;page.HelpOpen=true;deck.PerformLayout();Check(page.List.Top>=chart.Bottom,"expanded details below chart");Check(deck.AutoScrollMinSize.Height>=page.List.Bottom,"details reachable by scrolling");
                report.ClientSize=new Size((int)(720*scale),(int)(480*scale));report.LayoutReport();
                Check(deck.Left<50*scale,"compact navigation leaves content width");
                Handles(report);using(Bitmap bitmap=new Bitmap(report.Width,report.Height)){report.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));if(days==1&&scale==1.5f&&theme==8)bitmap.Save("tests/report-110-compact-150.png");}
            }
        }
        Check(ReportDesign.Number(.211623)=="0.21","rounded display");Check(ReportDesign.Number(.000012)=="<0.01","small nonzero distinguished");Check(ReportDesign.Number(double.NaN)=="—","invalid denominator distinct");
        Console.WriteLine("PASS: "+checks+" report 1.1 checks: 9 themes, 100/150% scaling, 0/1/7 baseline days, export precision, selection and scrolling.");
    }
}

