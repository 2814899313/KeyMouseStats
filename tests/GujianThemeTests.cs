using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>2.1.0:古剑奇谭 · 琴心剑魄主题的资源、九页、报告、HUD 与细节渲染回归。</summary>
internal static class GujianThemeTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static Image Wait(string name){DateTime until=DateTime.UtcNow.AddSeconds(20);Image image;while((image=ThemeImages.Get(name))==null){if(DateTime.UtcNow>until)throw new Exception("resource "+name);Application.DoEvents();Thread.Sleep(10);}return image;}
    [STAThread] static void Main()
    {
        try{Run();}catch(Exception ex){Console.WriteLine(ex.GetType().FullName+": "+ex.Message);Console.WriteLine(ex.StackTrace);Environment.Exit(1);}
    }
    static void Run()
    {
        Store.History.Clear();Store.Total=new Counters();Store.RollDay(DateTime.Today);Store.ThemeId=12;Store.KeyboardLayout=1;
        Store.Today.Keys=6388;Store.Today.Clicks=1764;Store.Today.Wheel=1130;Store.Today.ActiveSeconds=8110;Store.Today.MoveMeters=213.7;Store.Today.PeakApm=129;
        for(int i=0;i<24;i++){Store.Today.HourKeys[i]=(i>=7&&i<=23)?i*47:0;Store.Today.HourClicks[i]=Store.Today.HourKeys[i]/4;Store.Today.ActiveHours[i]=(i>=7&&i<=23)?960:0;}
        int[] keys={32,87,65,83,68,69,13,8,9,162,49,50};for(int i=0;i<keys.Length;i++)Store.Today.KeyCounts[keys[i]]=1040-i*63;
        string[] names={"GujianIcons","GujianKeyOrnaments","GujianWidget","GujianReport","GujianOverview","GujianTrend","GujianHours","GujianKeys","GujianApps","GujianInsights","GujianText","GujianDay","GujianHeat"};
        Check(ArtTheme.All.Length==13,"official theme list keeps thirteen entries");
        Check(ArtTheme.All[12].Name=="古剑奇谭 · 琴心剑魄","theme id 12");
        Check(ThemeAssets.ForTheme(12).Length==13,"all Gujian assets are registered");
        foreach(string name in names)Check(Wait(name)!=null,name);
        Check(ThemeImages.AllocatedBytes<=ThemeImages.Budget,"resource budget");
        System.IO.Directory.CreateDirectory("previews");
        BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using(Dashboard form=new Dashboard())
        {
            typeof(Dashboard).GetMethod("OnLoad",flags).Invoke(form,new object[]{EventArgs.Empty});form.ClientSize=new Size(1072,724);
            FieldInfo tab=typeof(Dashboard).GetField("_tab",flags);MethodInfo paint=typeof(Dashboard).GetMethod("OnPaint",flags);
            for(int page=0;page<9;page++)using(Bitmap bmp=new Bitmap(1072,724))using(Graphics g=Graphics.FromImage(bmp))
            {tab.SetValue(form,Enum.ToObject(tab.FieldType,page));paint.Invoke(form,new object[]{new PaintEventArgs(g,new Rectangle(0,0,bmp.Width,bmp.Height))});bmp.Save("previews/gujian-page-"+page+".png",ImageFormat.Png);}
            typeof(Dashboard).GetField("_keyView",flags).SetValue(form,2);tab.SetValue(form,Enum.ToObject(tab.FieldType,3));
            using(Bitmap bmp=new Bitmap(1072,724))using(Graphics g=Graphics.FromImage(bmp))
            {paint.Invoke(form,new object[]{new PaintEventArgs(g,new Rectangle(0,0,bmp.Width,bmp.Height))});bmp.Save("previews/gujian-keyboard.png",ImageFormat.Png);}
            typeof(Dashboard).GetMethod("OnFormClosed",flags).Invoke(form,new object[]{new FormClosedEventArgs(CloseReason.None)});
        }
        using(StatisticsReport report=new StatisticsReport(DateTime.Today))
        {
            report.StartPosition=FormStartPosition.Manual;report.Location=new Point(-30000,-30000);report.ClientSize=new Size(1140,800);
            report.Show();Application.DoEvents();report.Refresh();Application.DoEvents();
            using(Bitmap bmp=new Bitmap(1140,800)){report.DrawToBitmap(bmp,new Rectangle(0,0,bmp.Width,bmp.Height));bmp.Save("previews/gujian-report.png",ImageFormat.Png);}report.Hide();
        }
        using(GameHud hud=new GameHud())using(Bitmap bmp=new Bitmap(hud.ClientSize.Width,hud.ClientSize.Height))using(Graphics g=Graphics.FromImage(bmp))
        {typeof(GameHud).GetMethod("OnPaint",flags).Invoke(hud,new object[]{new PaintEventArgs(g,new Rectangle(Point.Empty,bmp.Size))});bmp.Save("previews/gujian-hud.png",ImageFormat.Png);}
        using(Bitmap sheet=new Bitmap(800,430))using(Graphics g=Graphics.FromImage(sheet))
        {g.Clear(ArtTheme.Current.Background);GujianArt.DrawWidget(g,new RectangleF(0,0,310,260));GujianArt.DrawReport(g,new RectangleF(320,0,480,340),.8f);for(int i=0;i<16;i++){GujianArt.Symbol(g,i,new RectangleF(325+(i%8)*56,345+(i/8)*40,34,34));GujianArt.Ornament(g,i,new RectangleF(350+(i%8)*56,345+(i/8)*40,22,22),.9f);}sheet.Save("previews/gujian-surfaces.png",ImageFormat.Png);}
        Console.WriteLine("PASS: "+checks+" Gujian theme checks; "+(ThemeImages.ResidentBytes/1048576.0).ToString("0.0")+" MiB originals");
    }
}
