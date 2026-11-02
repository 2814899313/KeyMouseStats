using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Collections.Generic;
using System.Windows.Forms;
using KeyMouseStats;

internal static class DailyReportLayoutTests
{
    static BindingFlags P = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(Dashboard f, string name, object value) { typeof(Dashboard).GetField(name,P).SetValue(f,value); }
    static object Get(Dashboard f, string name) { return typeof(Dashboard).GetField(name,P).GetValue(f); }
    [STAThread]
    static void Main()
    {
        // Isolated fixture: no real statistics are loaded or saved.
        Store.DailyReportDay = DateTime.Today;
        Application.EnableVisualStyles();
        DailyReport r = new DailyReport { Day = DateTime.Today.AddDays(-1), HasData = true, HasChars = true,
            Keys=13420, Clicks=3387, ActiveSeconds=33780, Chars=766, Commits=276, DistinctChars=315,
            AvgCharsPerCommit=2.8, BaseDays=7, BaseKeys=6000, BaseClicks=2000, BaseActiveSeconds=18000,
            BaseChars=640, BusiestHour=14, BusiestHourKeys=1912, LongestSessionSeconds=11400,
            SessionCount=18, MoveMeters=864.2, PeakApm=237, BusiestCharHour=20, LongestBurstMinutes=8,
            Headline="敲了 1.3万 次键、点了 3,387 次，有效使用 9 时 23 分；主要时间花在浏览器和写作工具。"
        };
        for(int i=9;i<22;i++) r.HourKeys[i]=200+(i*379)%1712;
        r.HourKeys[14]=1912;
        string[] names={"Microsoft Edge","Visual Studio Code","ChatGPT.exe","explorer.exe","Application with a very long name.exe"};
        for(int i=0;i<5;i++) r.Apps.Add(new DailyReport.AppLine { Name=names[i], Seconds=18000/(i*3+1) });
        r.Combos.AddRange(new[]{"CTRL+V 95","CTRL+C 55","CTRL+ALT+A 42","ALT+TAB 37"});
        r.TopKeys.AddRange(new[]{"I 1,247","空格 1,018","A 980"});
        r.TopChars.AddRange(new[]{"的 33","，28","是 19"});
        r.Composition[0]=600; r.Composition[1]=60; r.Composition[2]=40; r.Composition[3]=30; r.Composition[4]=36;
        r.Reminders.AddRange(new[]{"击键比日均高 124%，今天是真在干活。","最长一段连续使用 3 时 10 分，记得起身休息。","浏览器占了 53%，今天基本住在那儿。","目标（活跃时长）已达成 156%"});
        using(Dashboard f=new Dashboard())
        {
            typeof(Dashboard).GetMethod("OnLoad",P).Invoke(f,new object[]{EventArgs.Empty});
            var tab=typeof(Dashboard).GetField("_tab",P);
            tab.SetValue(f,Enum.ToObject(tab.FieldType,7));
            Set(f,"_dayShown",r.Day); Set(f,"_dayReport",r);
            foreach(float scale in new[]{1f,1.5f,2f})
            {
                Render(f,"daily-202-"+scale.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture),scale);
                var rect=(RectangleF)Get(f,"_dayPrevRect");
                var ids=(List<int>)Get(f,"_chipIds");
                var chips=(List<RectangleF>)Get(f,"_chips");
                if(chips[ids.IndexOf(420)]!=rect) throw new Exception("Date hit target mismatch");
                typeof(Dashboard).GetMethod("OnMouseClick",P).Invoke(f,new object[]{
                    new MouseEventArgs(MouseButtons.Left,1,(int)((rect.X+rect.Width/2+176)*scale),(int)((rect.Y+rect.Height/2+64)*scale),0)});
                if((DateTime)Get(f,"_dayShown")!=r.Day.AddDays(-1)) throw new Exception("Previous day click failed");
                Set(f,"_dayShown",r.Day); Set(f,"_dayReport",r);
            }
            Store.ThemeId=1; Render(f,"daily-202-light",1f);
            Store.ThemeId=0;
            DailyReport empty = new DailyReport { Day=DateTime.Today, InProgress=true, Headline="这一天还没有记录。" };
            empty.Reminders.Add("开始使用键盘和鼠标后，这里会显示当天的活动概况。");
            Set(f,"_dayShown",empty.Day); Set(f,"_dayReport",empty);
            Render(f,"daily-202-empty",1f);
            if(((List<int>)Get(f,"_chipIds")).Contains(421)) throw new Exception("Tomorrow should be disabled");
        }
        Console.WriteLine("DailyReportLayoutTests: render at 100/150/200%, light/empty states and date hit targets passed.");
    }
    static void Render(Dashboard f,string name,float scale)
    {
        Set(f,"_s",scale);
        using(Bitmap b=new Bitmap((int)(1072*scale),(int)(724*scale)))
        using(Graphics g=Graphics.FromImage(b))
        {
            typeof(Dashboard).GetMethod("OnPaint",P).Invoke(f,new object[]{new PaintEventArgs(g,new Rectangle(Point.Empty,b.Size))});
            b.Save("previews/"+name+".png",ImageFormat.Png);
        }
    }
}
