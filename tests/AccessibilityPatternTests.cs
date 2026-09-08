using System;
using System.Drawing;
using KeyMouseStats;
internal static class AccessibilityPatternTests
{
    private static long Hash(Bitmap bitmap){unchecked{long h=17;for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)h=h*31+bitmap.GetPixel(x,y).ToArgb();return h;}}
    private static long SegmentHash(int semantic)
    {using(Bitmap b=new Bitmap(100,100))using(Graphics g=Graphics.FromImage(b)){g.Clear(Color.FromArgb(20,30,40));using(Pen p=new Pen(Color.FromArgb(80,170,220),18))g.DrawArc(p,new RectangleF(12,12,76,76),-90,300);AccessiblePattern.Segment(g,new RectangleF(12,12,76,76),-90,300,18,semantic,Color.FromArgb(80,170,220));return Hash(b);}}
    private static long StructureHash(int theme)
    {Store.ThemeId=theme;using(Bitmap b=new Bitmap(140,90))using(Graphics g=Graphics.FromImage(b)){g.Clear(Color.FromArgb(18,25,32));AccessiblePattern.Segment(g,new RectangleF(12,12,60,60),-80,110,18,theme%4,ArtTheme.Current.Accent);RectangleF key=new RectangleF(82,24,48,34);using(SolidBrush fill=new SolidBrush(ArtTheme.Current.Raised))g.FillRectangle(fill,key);ThemeChrome.KeyCap(g,key,ArtTheme.Current.Raised,false);return Hash(b);}}
    private static long HeatHash(int theme)
    {Store.ThemeId=theme;DateTime deadline=DateTime.UtcNow.AddSeconds(5);while(ThemeImages.Get(theme<5?"CoreMaterials":"AccessiblePatterns")==null){if(DateTime.UtcNow>deadline)throw new Exception("Image texture did not load for theme "+theme);System.Threading.Thread.Sleep(10);}using(Bitmap b=new Bitmap(80,48))using(Graphics g=Graphics.FromImage(b)){g.Clear(HeatScale.At(.72));AccessiblePattern.Heat(g,new RectangleF(3,3,74,42),.72);return Hash(b);}}
    public static void Main()
    {
        Store.ThemeId=0;DateTime ready=DateTime.UtcNow.AddSeconds(5);while(ThemeImages.Get("CoreMaterials")==null){if(DateTime.UtcNow>ready)throw new Exception("Core material did not load");System.Threading.Thread.Sleep(10);}
        long solid=SegmentHash(0),striped=SegmentHash(1),dotted=SegmentHash(2),aux=SegmentHash(3);
        if(solid==striped||striped==dotted||dotted==aux||solid==aux)throw new Exception("Segment semantic markers are not visually distinct");
        long last=0;System.Collections.Generic.HashSet<long> structures=new System.Collections.Generic.HashSet<long>();for(int theme=0;theme<=10;theme++){long current=HeatHash(theme);if(theme>0&&current==last)throw new Exception("Theme heat materials collapsed at "+theme);last=current;if(!structures.Add(StructureHash(theme)))throw new Exception("Theme component structures collapsed at "+theme);}
        Console.WriteLine("Accessibility pattern tests passed.");
    }
}
