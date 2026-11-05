using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace KeyMouseStats
{
    /// <summary>2.1.0:古剑奇谭 · 琴心剑魄。九页独立场景,并覆盖组件、报告、HUD 与键帽。</summary>
    internal static class GujianArt
    {
        internal static bool Active { get { return ArtTheme.Validate(Store.ThemeId)==12; } }
        internal static readonly int[] NavSymbols={0,1,2,3,4,5,6,7,8};
        internal static readonly int[] MetricSymbols={9,10,3,1,11,12,2,14};
        internal static readonly string[] PageLabels={
            "天墉 · 剑鸣初醒","灵途 · 山河旧忆","星仪 · 十二时辰",
            "琴剑 · 百键流光","云城 · 万象行止","镜台 · 灵忆洞察",
            "墨卷 · 字里乾坤","归山 · 暮色小憩","灵脉 · 落指星图"};
        private static readonly string[] Pages={"GujianOverview","GujianTrend","GujianHours","GujianKeys","GujianApps","GujianInsights","GujianText","GujianDay","GujianHeat"};
        private static Image Icons { get { return NikkiArt.Load("GujianIcons"); } }
        private static Image KeyOrnaments { get { return NikkiArt.Load("GujianKeyOrnaments"); } }
        private static Image Widget { get { return NikkiArt.Load("GujianWidget"); } }
        private static Image Report { get { return NikkiArt.Load("GujianReport"); } }

        internal static void DrawPage(Graphics g,int page,RectangleF bounds)
        {
            if(!Active||page<0||page>=Pages.Length)return;Image image=NikkiArt.Load(Pages[page]);if(image==null)return;
            NikkiArt.DrawCached(g,image,WuxiaArt.Cover(new Rectangle(0,0,image.Width,image.Height),bounds),bounds,.9f,"gujian-page-"+page);
            using(LinearGradientBrush veil=new LinearGradientBrush(bounds,Color.FromArgb(40,4,13,22),Color.FromArgb(7,33,105,105),0f))g.FillRectangle(veil,bounds);
        }
        internal static void DrawWidget(Graphics g,RectangleF bounds){Image image=Widget;if(!Active||image==null)return;NikkiArt.DrawCached(g,image,WuxiaArt.Cover(new Rectangle(0,0,image.Width,image.Height),bounds),bounds,.97f,"gujian-widget");}
        internal static void DrawReport(Graphics g,RectangleF bounds,float opacity){Image image=Report;if(!Active||image==null)return;NikkiArt.DrawCached(g,image,WuxiaArt.Cover(new Rectangle(0,0,image.Width,image.Height),bounds),bounds,opacity,"gujian-report");}
        internal static void Symbol(Graphics g,int kind,RectangleF bounds,float opacity=1f){DrawAtlas(g,Icons,"gujian-icon-",kind,bounds,opacity);}
        internal static void Ornament(Graphics g,int kind,RectangleF bounds,float opacity=1f){DrawAtlas(g,KeyOrnaments,"gujian-key-",kind,bounds,opacity);}
        private static void DrawAtlas(Graphics g,Image atlas,string key,int kind,RectangleF bounds,float opacity)
        {
            if(!Active||atlas==null)return;kind=Math.Max(0,Math.Min(15,kind));int col=kind%4,row=kind/4;
            Rectangle source=Rectangle.FromLTRB(col*atlas.Width/4,row*atlas.Height/4,(col+1)*atlas.Width/4,(row+1)*atlas.Height/4);
            NikkiArt.DrawCached(g,atlas,source,bounds,opacity,key+kind);
        }

        internal static void CardOrnaments(Graphics g,RectangleF r)
        {
            Color cyan=ArtTheme.Current.Cyan,gold=ArtTheme.Current.Orange;
            using(Pen filigree=new Pen(Color.FromArgb(145,gold),1.1f))
            {
                filigree.StartCap=LineCap.Round;filigree.EndCap=LineCap.Round;
                g.DrawBezier(filigree,r.X+7,r.Y+13,r.X+7,r.Y+3,r.X+17,r.Y+2,r.X+33,r.Y+2);
                g.DrawBezier(filigree,r.Right-35,r.Bottom-2,r.Right-18,r.Bottom-2,r.Right-8,r.Bottom-4,r.Right-8,r.Bottom-14);
            }
            using(Pen spirit=new Pen(Color.FromArgb(115,cyan),1f)){spirit.DashStyle=DashStyle.Dot;g.DrawLine(spirit,r.X+38,r.Y+2,r.X+Math.Min(r.Width-40,85),r.Y+2);}
            using(SolidBrush jade=new SolidBrush(Color.FromArgb(220,cyan))){g.FillEllipse(jade,r.X+5,r.Y+5,3,3);g.FillEllipse(jade,r.Right-10,r.Bottom-10,3,3);}
        }
        internal static void Button(Graphics g,RectangleF r,bool selected,bool hover)
        {
            Color glow=selected?ArtTheme.Current.Cyan:hover?ArtTheme.Current.Orange:ArtTheme.Current.Line;
            using(GraphicsPath p=Dashboard.RoundedRect(r.X,r.Y,r.Width,r.Height,7))
            using(LinearGradientBrush fill=new LinearGradientBrush(r,selected?Color.FromArgb(238,17,68,73):Color.FromArgb(226,8,27,38),Color.FromArgb(232,5,15,24),90))
            using(Pen edge=new Pen(Color.FromArgb(selected||hover?225:135,glow),selected?1.8f:1f)){g.FillPath(fill,p);g.DrawPath(edge,p);}
            using(Pen line=new Pen(Color.FromArgb(selected?210:95,glow),1.2f))g.DrawBezier(line,r.X+8,r.Bottom-4,r.X+r.Width*.34f,r.Bottom-8,r.X+r.Width*.66f,r.Bottom,r.Right-8,r.Bottom-4);
            if(selected)using(SolidBrush jewel=new SolidBrush(ArtTheme.Current.Cyan))g.FillDiamond(jewel,r.X+8,r.Y+8,3);
        }
        private static void FillDiamond(this Graphics g,Brush brush,float x,float y,float radius)
        {g.FillPolygon(brush,new[]{new PointF(x,y-radius),new PointF(x+radius,y),new PointF(x,y+radius),new PointF(x-radius,y)});}
        internal static void KeyCap(Graphics g,HeatKey key,RectangleF rect,bool champion)
        {
            int ornament=key.Label=="Space"?15:key.Label.StartsWith("F")||key.Label=="Esc"?1:key.Label.Length==1&&char.IsDigit(key.Label[0])?7:key.Label.Length==1&&char.IsLetter(key.Label[0])?0:4;
            float size=Math.Min(rect.Height*.48f,champion?19:13);Ornament(g,ornament,new RectangleF(rect.Right-size-2,rect.Y+2,size,size),champion?.94f:.42f);
            using(Pen edge=new Pen(Color.FromArgb(champion?230:90,champion?ArtTheme.Current.Orange:ArtTheme.Current.Cyan),champion?1.55f:.8f))g.DrawPath(edge,Dashboard.RoundedRect(rect.X+2,rect.Y+2,Math.Max(1,rect.Width-4),Math.Max(1,rect.Height-4),4));
        }
        internal static void Loading(Graphics g,float x,float y,int frame)
        {
            using(Pen path=new Pen(Color.FromArgb(160,ArtTheme.Current.Cyan),1.3f)){path.StartCap=LineCap.Round;g.DrawBezier(path,x,y+17,x+16,y+2,x+35,y+27,x+58,y+10);}
            for(int i=0;i<5;i++){double t=((frame+i*4)%24)/23.0;float px=x+(float)(58*t),py=y+11+(float)Math.Sin(t*Math.PI*3)*7;using(SolidBrush b=new SolidBrush(Color.FromArgb(85+(int)(150*t),220,250,246)))g.FillEllipse(b,px-2,py-2,4,4);}
            Ornament(g,1,new RectangleF(x+56,y+1,22,22),.9f);
        }
        internal static void RingAura(Graphics g,RectangleF ring)
        {
            using(Pen aura=new Pen(Color.FromArgb(110,ArtTheme.Current.Cyan),1.15f)){aura.DashStyle=DashStyle.Dash;g.DrawArc(aura,ring.X-7,ring.Y-7,ring.Width+14,ring.Height+14,-85,320);}
            for(int i=0;i<4;i++){double a=(-60+i*91)*Math.PI/180;float x=ring.X+ring.Width/2+(float)Math.Cos(a)*(ring.Width/2+7),y=ring.Y+ring.Height/2+(float)Math.Sin(a)*(ring.Height/2+7);using(SolidBrush b=new SolidBrush(Color.FromArgb(190,ArtTheme.Current.Orange)))g.FillEllipse(b,x-2,y-2,4,4);}
        }
        internal static void DrawHud(Graphics g,Rectangle bounds,bool editing)
        {
            using(LinearGradientBrush bg=new LinearGradientBrush(bounds,Color.FromArgb(244,5,18,27),Color.FromArgb(238,12,48,52),0f))g.FillRectangle(bg,bounds);
            using(Pen line=new Pen(Color.FromArgb(editing?235:175,editing?ArtTheme.Current.Orange:ArtTheme.Current.Cyan),1.2f)){g.DrawBezier(line,1,4,bounds.Width*.28f,12,bounds.Width*.72f,-5,bounds.Width-2,4);g.DrawBezier(line,1,bounds.Height-3,bounds.Width*.25f,bounds.Height-14,bounds.Width*.75f,bounds.Height+5,bounds.Width-2,bounds.Height-3);}
            Symbol(g,14,new RectangleF(bounds.Width-33,3,28,28),.9f);
        }
    }
}
