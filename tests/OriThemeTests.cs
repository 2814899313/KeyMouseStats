using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>2.0.6:奥日 · 灵光森林(正式版主题 id 2)的资源登记与离屏渲染回归。</summary>
internal static class OriThemeTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static Image Wait(string name){DateTime until=DateTime.UtcNow.AddSeconds(20);Image image;while((image=ThemeImages.Get(name))==null){if(DateTime.UtcNow>until)throw new Exception("resource "+name);Application.DoEvents();Thread.Sleep(10);}return image;}
    [STAThread] static void Main()
    {
        try { Run(); } catch(Exception ex) { Console.WriteLine(ex.GetType().FullName+": "+ex.Message); Console.WriteLine(ex.StackTrace); Environment.Exit(1); }
    }
    static void Run()
    {
        Console.WriteLine("stage data");
        Store.History.Clear();Store.Total=new Counters();Store.RollDay(DateTime.Today);Store.ThemeId=2;Store.KeyboardLayout=1;
        Store.Today.Keys=5432;Store.Today.Clicks=1480;Store.Today.Wheel=820;Store.Today.ActiveSeconds=7200;Store.Today.MoveMeters=184.3;Store.Today.PeakApm=116;
        for(int i=0;i<24;i++){Store.Today.HourKeys[i]=(i>=8&&i<=22)?i*41:0;Store.Today.HourClicks[i]=Store.Today.HourKeys[i]/4;Store.Today.ActiveHours[i]=(i>=8&&i<=22)?900:0;}
        int[] keys={32,69,65,83,68,87,13,8,9,162};for(int i=0;i<keys.Length;i++)Store.Today.KeyCounts[keys[i]]=900-i*61;
        string[] names={"OriIcons","OriKeyOrnaments","OriWidget","OriReport","OriOverview","OriTrend","OriHours","OriKeys","OriApps","OriInsights","OriText","OriDay","OriHeat"};
        Check(ArtTheme.All.Length==13,"official theme list keeps thirteen entries");
        Check(ArtTheme.Validate(0)==0&&ArtTheme.Validate(99)==0,"theme id validates against the official list");
        Check(ArtTheme.All[2].Name=="奥日 · 灵光森林","theme id 2 is the Ori theme");
        Check(ThemeAssets.ForTheme(2).Length==13,"all Ori assets are registered");
        Console.WriteLine("stage resources");foreach(string name in names){Console.WriteLine(name);Check(Wait(name)!=null,name);}
        Check(ThemeImages.AllocatedBytes<=ThemeImages.Budget,"resource budget");
        System.IO.Directory.CreateDirectory("previews");
        BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
        Console.WriteLine("stage dashboard");
        using(Dashboard form=new Dashboard())
        {
            typeof(Dashboard).GetMethod("OnLoad",f).Invoke(form,new object[]{EventArgs.Empty});form.ClientSize=new Size(1072,724);
            FieldInfo tab=typeof(Dashboard).GetField("_tab",f);MethodInfo paint=typeof(Dashboard).GetMethod("OnPaint",f);
            for(int page=0;page<9;page++)using(Bitmap bmp=new Bitmap(1072,724))using(Graphics g=Graphics.FromImage(bmp))
            {tab.SetValue(form,Enum.ToObject(tab.FieldType,page));paint.Invoke(form,new object[]{new PaintEventArgs(g,new Rectangle(0,0,bmp.Width,bmp.Height))});bmp.Save("previews/ori-page-"+page+".png",ImageFormat.Png);}
            typeof(Dashboard).GetField("_keyView",f).SetValue(form,2);
            tab.SetValue(form,Enum.ToObject(tab.FieldType,3));
            using(Bitmap bmp=new Bitmap(1072,724))using(Graphics g=Graphics.FromImage(bmp))
            {paint.Invoke(form,new object[]{new PaintEventArgs(g,new Rectangle(0,0,bmp.Width,bmp.Height))});bmp.Save("previews/ori-keyboard.png",ImageFormat.Png);}
            typeof(Dashboard).GetMethod("OnFormClosed",f).Invoke(form,new object[]{new FormClosedEventArgs(CloseReason.None)});
        }
        Console.WriteLine("stage report and hud");
        using(StatisticsReport report=new StatisticsReport(DateTime.Today))
        {
            report.StartPosition=FormStartPosition.Manual;report.Location=new Point(-30000,-30000);report.ClientSize=new Size(1140,800);
            report.Show();Application.DoEvents();report.Refresh();Application.DoEvents();
            using(Bitmap bmp=new Bitmap(1140,800)){report.DrawToBitmap(bmp,new Rectangle(0,0,bmp.Width,bmp.Height));bmp.Save("previews/ori-report.png",ImageFormat.Png);}
            report.Hide();
        }
        using(GameHud hud=new GameHud())using(Bitmap bmp=new Bitmap(hud.ClientSize.Width,hud.ClientSize.Height))using(Graphics g=Graphics.FromImage(bmp))
        {typeof(GameHud).GetMethod("OnPaint",f).Invoke(hud,new object[]{new PaintEventArgs(g,new Rectangle(Point.Empty,bmp.Size))});bmp.Save("previews/ori-hud.png",ImageFormat.Png);}
        Console.WriteLine("stage surfaces");
        using(Bitmap sheet=new Bitmap(800,430))using(Graphics g=Graphics.FromImage(sheet))
        {g.Clear(ArtTheme.Current.Background);OriArt.DrawWidget(g,new RectangleF(0,0,310,260));OriArt.DrawReport(g,new RectangleF(320,0,480,340),.8f);for(int i=0;i<16;i++){OriArt.Symbol(g,i,new RectangleF(325+(i%8)*56,345+(i/8)*40,34,34));OriArt.Ornament(g,i,new RectangleF(350+(i%8)*56,345+(i/8)*40,22,22),.9f);}sheet.Save("previews/ori-surfaces.png",ImageFormat.Png);}
        Console.WriteLine("PASS: "+checks+" Ori theme checks; "+(ThemeImages.ResidentBytes/1048576.0).ToString("0.0")+" MiB originals");
    }
}
