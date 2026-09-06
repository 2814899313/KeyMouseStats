using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace KeyMouseStats
{
    internal static class ResidentArt
    {
        public static bool Active { get { return Store.ThemeId==7; } }
        private static readonly string[] Resources={"ResidentOverview","ResidentTrend","ResidentHours","ResidentKeys","ResidentApps","ResidentInsights"};
        
        public static bool HasScene(int page)
        {
            if(page<0||page>=6)return false;
            return NikkiArt.Load(Resources[page])!=null;
        }
        public static void DrawPage(Graphics g,int page,RectangleF bounds)
        {
            if(!Active||!HasScene(page))return;
            Image image=NikkiArt.Load(Resources[page]);NikkiArt.DrawCached(g,image,new Rectangle(0,0,image.Width,image.Height),bounds,.82f,"resident-page-"+page);
        }
        public static readonly string[] PageLabels={"LEON / KENNEDY","JILL / VALENTINE","CHRIS / REDFIELD","CLAIRE / REDFIELD","ADA / WONG","REBECCA / CHAMBERS"};
        private static Image Icons { get { return NikkiArt.Load("ResidentIcons"); } }
        public static bool HasIcons { get { return Icons!=null; } }
        public static void Symbol(Graphics g,int kind,RectangleF bounds)
        {
            if(Icons==null||kind<0||kind>15)return;
            int x=kind%4,y=kind/4;
            Rectangle source=Rectangle.FromLTRB(x*Icons.Width/4,y*Icons.Height/4,(x+1)*Icons.Width/4,(y+1)*Icons.Height/4);
            NikkiArt.DrawCached(g,Icons,source,bounds,1f,"resident-icon-"+kind);
        }

    }
}
