using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace KeyMouseStats
{
    internal static class BuildFlavor
    {
        internal const string ProductName="键鼠统计";
        internal const string EditionLabel="正式版";
    }

    /// <summary>2.0.6:奥日 · 灵光森林。原为独立定制版,现并入正式版主题列表(id 2)。</summary>
    internal static class OriArt
    {
        internal static bool Active { get { return ArtTheme.Validate(Store.ThemeId)==2; } }
        internal static readonly int[] NavSymbols={0,1,2,3,4,5,6,7,15};
        internal static readonly int[] MetricSymbols={3,9,10,11,2,5,1,14};
        internal static readonly string[] PageLabels={"SPIRIT / HOME","PATH / MEMORY","LIGHT / HOURS","LEAF / INPUT","WELLSPRING / APPS","WISDOM / INSIGHTS","GLYPH / WORDS","DAWN / REVIEW","SPIRIT / HEAT"};
        private static readonly string[] Pages={"OriOverview","OriTrend","OriHours","OriKeys","OriApps","OriInsights","OriText","OriDay","OriHeat"};
        private static Image Icons { get { return NikkiArt.Load("OriIcons"); } }
        private static Image KeyOrnaments { get { return NikkiArt.Load("OriKeyOrnaments"); } }
        private static Image Widget { get { return NikkiArt.Load("OriWidget"); } }
        private static Image Report { get { return NikkiArt.Load("OriReport"); } }

        internal static void DrawPage(Graphics g,int page,RectangleF bounds)
        {
            if(!Active||page<0||page>=Pages.Length)return;Image image=NikkiArt.Load(Pages[page]);if(image==null)return;
            Rectangle source=WuxiaArt.Cover(new Rectangle(0,0,image.Width,image.Height),bounds);
            NikkiArt.DrawCached(g,image,source,bounds,.88f,"ori-page-"+page);
            using(LinearGradientBrush veil=new LinearGradientBrush(bounds,Color.FromArgb(44,4,12,30),Color.FromArgb(8,21,86,111),0f))g.FillRectangle(veil,bounds);
        }
        internal static void DrawWidget(Graphics g,RectangleF bounds){Image image=Widget;if(!Active||image==null)return;NikkiArt.DrawCached(g,image,WuxiaArt.Cover(new Rectangle(0,0,image.Width,image.Height),bounds),bounds,.96f,"ori-widget");}
        internal static void DrawReport(Graphics g,RectangleF bounds,float opacity){Image image=Report;if(!Active||image==null)return;NikkiArt.DrawCached(g,image,WuxiaArt.Cover(new Rectangle(0,0,image.Width,image.Height),bounds),bounds,opacity,"ori-report");}
        internal static void Symbol(Graphics g,int kind,RectangleF bounds,float opacity=1f){DrawAtlas(g,Icons,"ori-icon-",kind,bounds,opacity);}
        internal static void Ornament(Graphics g,int kind,RectangleF bounds,float opacity=1f){DrawAtlas(g,KeyOrnaments,"ori-key-",kind,bounds,opacity);}
        private static void DrawAtlas(Graphics g,Image atlas,string key,int kind,RectangleF bounds,float opacity){if(!Active||atlas==null)return;kind=Math.Max(0,Math.Min(15,kind));int col=kind%4,row=kind/4;Rectangle source=Rectangle.FromLTRB(col*atlas.Width/4,row*atlas.Height/4,(col+1)*atlas.Width/4,(row+1)*atlas.Height/4);NikkiArt.DrawCached(g,atlas,source,bounds,opacity,key+kind);}

        internal static void CardOrnaments(Graphics g,RectangleF r)
        {
            Color cyan=ArtTheme.Current.Cyan,gold=ArtTheme.Current.Orange;
            using(Pen vine=new Pen(Color.FromArgb(135,cyan),1.15f)){vine.StartCap=LineCap.Round;vine.EndCap=LineCap.Round;g.DrawBezier(vine,r.X+8,r.Y+1,r.X+22,r.Y+7,r.X+35,r.Y-4,r.X+54,r.Y+1);g.DrawBezier(vine,r.Right-58,r.Bottom,r.Right-38,r.Bottom-6,r.Right-24,r.Bottom+4,r.Right-8,r.Bottom);}
            using(SolidBrush light=new SolidBrush(Color.FromArgb(200,cyan))){g.FillEllipse(light,r.X+7,r.Y+4,3,3);g.FillEllipse(light,r.Right-10,r.Bottom-8,3,3);}
            using(Pen rune=new Pen(Color.FromArgb(90,gold),.8f)){g.DrawArc(rune,r.Right-18,r.Y+4,10,10,205,250);g.DrawLine(rune,r.Right-13,r.Y+6,r.Right-13,r.Y+15);}
        }
        internal static void Button(Graphics g,RectangleF r,bool selected,bool hover)
        {
            Color glow=selected?ArtTheme.Current.Cyan:hover?ArtTheme.Current.Purple:ArtTheme.Current.Line;
            using(GraphicsPath p=Dashboard.RoundedRect(r.X,r.Y,r.Width,r.Height,12))
            using(LinearGradientBrush fill=new LinearGradientBrush(r,selected?Color.FromArgb(238,17,74,96):Color.FromArgb(224,9,25,48),Color.FromArgb(228,5,13,32),90))
            using(Pen edge=new Pen(Color.FromArgb(selected||hover?220:130,glow),selected?1.8f:1f)){g.FillPath(fill,p);g.DrawPath(edge,p);}
            using(Pen vine=new Pen(Color.FromArgb(selected?200:85,glow),1.2f))g.DrawBezier(vine,r.X+8,r.Bottom-4,r.X+r.Width*.33f,r.Bottom-8,r.X+r.Width*.66f,r.Bottom,r.Right-8,r.Bottom-4);
            if(selected)using(SolidBrush mote=new SolidBrush(Color.FromArgb(235,230,253,255)))g.FillEllipse(mote,r.X+6,r.Y+6,4,4);
        }
        internal static void KeyCap(Graphics g,HeatKey key,RectangleF rect,bool champion)
        {
            int ornament=key.Label=="Space"?15:key.Label.StartsWith("F")||key.Label=="Esc"?6:key.Label.Length==1&&char.IsDigit(key.Label[0])?7:key.Label.Length==1&&char.IsLetter(key.Label[0])?0:4;
            float size=Math.Min(rect.Height*.48f,champion?19:13);RectangleF mark=new RectangleF(rect.Right-size-2,rect.Y+2,size,size);Ornament(g,ornament,mark,champion?.92f:.46f);
            using(Pen glow=new Pen(Color.FromArgb(champion?225:85,champion?ArtTheme.Current.Orange:ArtTheme.Current.Cyan),champion?1.5f:.75f))g.DrawPath(glow,Dashboard.RoundedRect(rect.X+2,rect.Y+2,Math.Max(1,rect.Width-4),Math.Max(1,rect.Height-4),6));
        }
        internal static void Loading(Graphics g,float x,float y,int frame)
        {
            using(Pen trail=new Pen(Color.FromArgb(150,ArtTheme.Current.Cyan),1.4f)){trail.StartCap=LineCap.Round;g.DrawBezier(trail,x,y+16,x+16,y+4,x+28,y+28,x+47,y+12);}
            for(int i=0;i<5;i++){double t=((frame+i*3)%20)/19.0;float px=x+(float)(47*t),py=y+12+(float)Math.Sin(t*Math.PI*3)*7;int a=70+(int)(150*t);using(SolidBrush b=new SolidBrush(Color.FromArgb(a,225,252,255)))g.FillEllipse(b,px-2,py-2,4,4);}
            Ornament(g,10,new RectangleF(x+47,y+3,22,22),.9f);
        }
        internal static void RingAura(Graphics g,RectangleF ring)
        {
            using(Pen aura=new Pen(Color.FromArgb(105,ArtTheme.Current.Cyan),1.1f)){aura.DashStyle=DashStyle.Dot;g.DrawArc(aura,ring.X-7,ring.Y-7,ring.Width+14,ring.Height+14,-78,310);}
            for(int i=0;i<5;i++){double a=(-70+i*67)*Math.PI/180;float x=ring.X+ring.Width/2+(float)Math.Cos(a)*(ring.Width/2+7),y=ring.Y+ring.Height/2+(float)Math.Sin(a)*(ring.Height/2+7);using(SolidBrush b=new SolidBrush(Color.FromArgb(170,ArtTheme.Current.Cyan)))g.FillEllipse(b,x-2,y-2,4,4);}
        }
        internal static void DrawHud(Graphics g,Rectangle bounds,bool editing)
        {
            using(LinearGradientBrush bg=new LinearGradientBrush(bounds,Color.FromArgb(242,5,19,38),Color.FromArgb(236,12,48,66),0f))g.FillRectangle(bg,bounds);
            using(Pen root=new Pen(Color.FromArgb(editing?235:170,editing?ArtTheme.Current.Orange:ArtTheme.Current.Cyan),1.2f)){g.DrawBezier(root,1,bounds.Height-2,bounds.Width*.22f,bounds.Height-14,bounds.Width*.76f,bounds.Height+5,bounds.Width-2,bounds.Height-3);g.DrawBezier(root,0,3,bounds.Width*.25f,12,bounds.Width*.72f,-5,bounds.Width-1,3);}
            Ornament(g,10,new RectangleF(bounds.Width-31,3,26,26),.9f);
        }
    }
}
