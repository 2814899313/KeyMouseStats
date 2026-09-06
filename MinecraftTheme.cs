using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace KeyMouseStats
{
    internal static class MinecraftArt
    {
        public static bool Active { get { return Store.ThemeId==8; } }
        private static readonly string[] Resources={"MinecraftOverview","MinecraftTrend","MinecraftHours","MinecraftKeys","MinecraftApps","MinecraftInsights"};
        
        public static bool HasScene(int page)
        {
            if(page<0||page>=6)return false;
            return NikkiArt.Load(Resources[page])!=null;
        }
        public static void DrawPage(Graphics g,int page,RectangleF bounds)
        {
            if(!Active||!HasScene(page))return;
            Image image=NikkiArt.Load(Resources[page]);NikkiArt.DrawCached(g,image,new Rectangle(0,0,image.Width,image.Height),bounds,.82f,"minecraft-page-"+page);
        }
        public static readonly string[] PageLabels={"OVERWORLD","MINECART / TRAIL","AMETHYST / CAVE","CRAFTING / TABLE","VILLAGE / TRADE","ENCHANTING / BOOK"};
        private static Image Icons { get { return NikkiArt.Load("MinecraftIcons"); } }
        public static bool HasIcons { get { return Icons!=null; } }
        public static void Symbol(Graphics g,int kind,RectangleF bounds)
        {
            if(Icons==null||kind<0||kind>15)return;
            int x=kind%4,y=kind/4;
            Rectangle source=Rectangle.FromLTRB(x*Icons.Width/4,y*Icons.Height/4,(x+1)*Icons.Width/4,(y+1)*Icons.Height/4);
            NikkiArt.DrawCached(g,Icons,source,bounds,1f,"minecraft-icon-"+kind);
        }

    }
}

namespace KeyMouseStats
{
    internal static class BalatroArt
    {
        internal static bool Active{get{return Store.ThemeId==9;}}
        internal static readonly int[] NavSymbols={8,7,4,0,5,11};
        internal static readonly string[] PageLabels={"JOKER / ANTE","ORBIT / STREAK","SUN / MOON","DECK / HAND","TABLE / CAST","ARCANA / SCORE"};
        internal static void DrawPage(Graphics g,int page,RectangleF bounds)
        {
            if(!Active||page<0||page>=6)return;Image atlas=NikkiArt.Load("BalatroBackgrounds");if(atlas==null)return;
            int col=page%2,row=page/2;Rectangle source=Rectangle.FromLTRB(col*atlas.Width/2,row*atlas.Height/3,(col+1)*atlas.Width/2,(row+1)*atlas.Height/3);
            NikkiArt.DrawCached(g,atlas,source,bounds,.86f,"balatro-page-"+page);
        }
        internal static void Symbol(Graphics g,int kind,RectangleF bounds)
        {
            if(!Active)return;Image atlas=NikkiArt.Load("BalatroIcons");if(atlas==null)return;kind=Math.Max(0,Math.Min(15,kind));
            Rectangle source=Rectangle.FromLTRB(kind%4*atlas.Width/4,kind/4*atlas.Height/4,(kind%4+1)*atlas.Width/4,(kind/4+1)*atlas.Height/4);
            NikkiArt.DrawCached(g,atlas,source,bounds,1,"balatro-icon-"+kind);
        }
    }
}
