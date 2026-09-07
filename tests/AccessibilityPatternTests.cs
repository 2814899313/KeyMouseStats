using System;
using System.Drawing;
using KeyMouseStats;
internal static class AccessibilityPatternTests
{
    private static long Hash(Bitmap bitmap){unchecked{long h=17;for(int y=0;y<bitmap.Height;y+=2)for(int x=0;x<bitmap.Width;x+=2)h=h*31+bitmap.GetPixel(x,y).ToArgb();return h;}}
    private static long RingHash(int semantic)
    {using(Bitmap b=new Bitmap(100,100))using(Graphics g=Graphics.FromImage(b)){g.Clear(Color.FromArgb(20,30,40));using(Pen p=new Pen(Color.FromArgb(80,170,220),18))g.DrawArc(p,new RectangleF(12,12,76,76),-90,300);AccessiblePattern.Ring(g,new RectangleF(12,12,76,76),-90,300,18,semantic,Color.FromArgb(80,170,220));return Hash(b);}}
    private static long HeatHash(int theme)
    {Store.ThemeId=theme;DateTime deadline=DateTime.UtcNow.AddSeconds(5);while(ThemeImages.Get("AccessiblePatterns")==null){if(DateTime.UtcNow>deadline)throw new Exception("Image texture did not load for theme "+theme);System.Threading.Thread.Sleep(10);}using(Bitmap b=new Bitmap(80,48))using(Graphics g=Graphics.FromImage(b)){g.Clear(HeatScale.At(.72));AccessiblePattern.Heat(g,new RectangleF(3,3,74,42),.72);return Hash(b);}}
    public static void Main()
    {
        long solid=RingHash(0),striped=RingHash(1),dotted=RingHash(2),aux=RingHash(3);
        if(solid==striped||striped==dotted||dotted==aux||solid==aux)throw new Exception("Ring semantic patterns are not visually distinct");
        long last=0;for(int theme=5;theme<=10;theme++){long current=HeatHash(theme);if(theme>5&&current==last)throw new Exception("Theme heat motifs collapsed at "+theme);last=current;}
        Console.WriteLine("Accessibility pattern tests passed.");
    }
}
