using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeyMouseStats;

internal static class WuxiaThemeTests
{
    private static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private static Image Wait(string name){Stopwatch w=Stopwatch.StartNew();Image image;while((image=ThemeImages.Get(name))==null){if(w.ElapsedMilliseconds>15000)throw new Exception("missing "+name);Thread.Sleep(10);}return image;}
    [STAThread] private static void Main()
    {
        Store.History.Clear();Store.RollDay(DateTime.Today);Store.ThemeId=10;Store.KeyboardLayout=2;
        for(int h=7;h<23;h++){Store.Today.HourKeys[h]=100+h*23;Store.Today.HourClicks[h]=30+h*5;Store.Today.ActiveHours[h]=600+h*30;}
        Store.Today.Keys=18000;Store.Today.Clicks=3500;Store.Today.Wheel=1200;Store.Today.ActiveSeconds=9600;
        for(int key=32;key<123;key++)Store.Today.KeyCounts[key]=100+(key*97)%1800;
        Wait("WuxiaUi");Wait("WuxiaWidget");Wait("WuxiaBackgrounds");
        if(!WuxiaArt.HasUi||!WuxiaArt.HasWidget||!WuxiaArt.HasBackgrounds)throw new Exception("wuxia assets unavailable");
        if(WuxiaArt.KeyOrnament(new HeatKey("A",1,65,0,0,1,1))!=18||WuxiaArt.KeyOrnament(new HeatKey("7",1,55,0,0,1,1))!=19||WuxiaArt.KeyOrnament(new HeatKey("F5",1,116,0,0,1,1))!=20||WuxiaArt.KeyOrnament(new HeatKey("Ctrl",1,162,0,0,1,1))!=21||WuxiaArt.KeyOrnament(new HeatKey("↑",1,38,0,0,1,1))!=22||WuxiaArt.KeyOrnament(new HeatKey("Space",1,32,0,0,1,1))!=23)throw new Exception("key material mapping");
        Rectangle wide=WuxiaArt.Cover(new Rectangle(0,0,900,400),new RectangleF(0,0,600,400));if(wide.Width!=600||wide.Height!=400||wide.X!=150)throw new Exception("wide cover crop");
        Rectangle tall=WuxiaArt.Cover(new Rectangle(0,0,400,900),new RectangleF(0,0,600,400));if(tall.Width!=400||tall.Height!=267||tall.Y!=316)throw new Exception("tall cover crop");
        System.IO.Directory.CreateDirectory("previews");
        using(Dashboard form=new Dashboard())using(Bitmap bitmap=new Bitmap(1072,724))using(Graphics graphics=Graphics.FromImage(bitmap))
        {
            typeof(Dashboard).GetMethod("OnLoad",Flags).Invoke(form,new object[]{EventArgs.Empty});typeof(Dashboard).GetField("_s",Flags).SetValue(form,1f);
            FieldInfo tab=typeof(Dashboard).GetField("_tab",Flags);MethodInfo paint=typeof(Dashboard).GetMethod("OnPaint",Flags);
            for(int page=0;page<6;page++){tab.SetValue(form,Enum.ToObject(tab.FieldType,page));typeof(Dashboard).GetField("_showKeyboardHeatmap",Flags).SetValue(form,page==3);graphics.ResetTransform();paint.Invoke(form,new object[]{new PaintEventArgs(graphics,new Rectangle(Point.Empty,bitmap.Size))});bitmap.Save("previews/wuxia-page-"+page+".png",ImageFormat.Png);}
            using(Bitmap widget=new Bitmap(496,416))using(Graphics wg=Graphics.FromImage(widget)){WuxiaArt.DrawWidget(wg,new RectangleF(0,0,496,416));widget.Save("previews/wuxia-widget-background.png",ImageFormat.Png);}
            typeof(Dashboard).GetMethod("OnFormClosed",Flags).Invoke(form,new object[]{new FormClosedEventArgs(CloseReason.None)});
        }
        if(ThemeImages.AllocatedBytes>ThemeImages.Budget||NikkiArt.ScaledBytes>40L*1024*1024)throw new Exception("theme cache budget");
        Console.WriteLine("PASS: six wuxia pages, aspect-safe cover, key materials, widget artwork and cache bounds");
    }
}
