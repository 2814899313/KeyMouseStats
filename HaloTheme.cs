using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace KeyMouseStats
{
    internal static class HaloArt
    {
        public static bool Active { get { return Store.ThemeId==6; } }
        private static readonly string[] Resources={"HaloOverview","HaloTrend","HaloHours","HaloKeys","HaloApps","HaloInsights"};
        
        public static bool HasScene(int page)
        {
            if(page<0||page>=6)return false;
            return NikkiArt.Load(Resources[page])!=null;
        }
        public static void DrawPage(Graphics g,int page,RectangleF bounds)
        {
            if(!Active||!HasScene(page))return;
            Image image=NikkiArt.Load(Resources[page]);NikkiArt.DrawCached(g,image,new Rectangle(0,0,image.Width,image.Height),bounds,.82f,"halo-page-"+page);
        }
        public static readonly string[] PageLabels={"HALO / ORBIT","SIGNAL / TREND","RADAR / 24H","INPUT / TERMINAL","MAP / UPLINK","AI / ANALYSIS"};
        private static Image Icons { get { return NikkiArt.Load("HaloIcons"); } }
        public static bool HasIcons { get { return Icons!=null; } }
        public static void Symbol(Graphics g,int kind,RectangleF bounds)
        {
            if(Icons==null||kind<0||kind>15)return;
            int x=kind%4,y=kind/4;
            Rectangle source=Rectangle.FromLTRB(x*Icons.Width/4,y*Icons.Height/4,(x+1)*Icons.Width/4,(y+1)*Icons.Height/4);
            NikkiArt.DrawCached(g,Icons,source,bounds,1f,"halo-icon-"+kind);
        }
        public static void Helmet(Graphics g,RectangleF bounds) { Symbol(g,14,bounds); }
    }
    internal static class ThemeArt
    {
        public static bool Active { get { return NikkiArt.Active||HaloArt.Active||ResidentArt.Active||MinecraftArt.Active||BalatroArt.Active||WuxiaArt.Active||EuroTruckArt.Active; } }
        public static bool Dark { get { return HaloArt.Active||ResidentArt.Active||MinecraftArt.Active||BalatroArt.Active||WuxiaArt.Active||EuroTruckArt.Active; } }
        public static void Symbol(Graphics g,int kind,RectangleF bounds) { if(EuroTruckArt.Active)EuroTruckArt.Symbol(g,kind,bounds);else if(WuxiaArt.Active)WuxiaArt.Symbol(g,kind,bounds);else if(BalatroArt.Active)BalatroArt.Symbol(g,kind,bounds);else if(MinecraftArt.Active)MinecraftArt.Symbol(g,kind,bounds);else if(ResidentArt.Active)ResidentArt.Symbol(g,kind,bounds);else HaloArt.Symbol(g,kind,bounds); }
        public static void Sticker(Graphics g,int index,RectangleF bounds)
        { if(EuroTruckArt.Active){EuroTruckArt.Symbol(g,index==0?1:index==2?14:index==3?13:index==4?6:3,bounds);}else if(WuxiaArt.Active){WuxiaArt.Symbol(g,index==0?14:index==2?16:index==3?17:index==4?11:5,bounds);}else if(BalatroArt.Active){BalatroArt.Symbol(g,index==0?8:index==2?10:index==3?9:index==4?11:5,bounds);}else if(MinecraftArt.Active){MinecraftArt.Symbol(g,index==0?14:index==2?12:index==3?13:index==4?4:5,bounds);}else if(ResidentArt.Active){ResidentArt.Symbol(g,index==0?14:index==2?12:index==3?13:index==4?4:5,bounds);}else if(HaloArt.Active){if(index==0)HaloArt.Helmet(g,bounds);else HaloArt.Symbol(g,index==2?12:index==3?13:index==4?4:5,bounds);}else NikkiArt.Sticker(g,index,bounds); }
    }
}

