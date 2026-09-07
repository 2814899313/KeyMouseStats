using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal static class InteractionBadge
    {
        internal static void Draw(Graphics g,RectangleF rect,Font font,string text)
        {
            ArtTheme theme=ArtTheme.Current;
            int id=Store.ThemeId;
            Color accent=id==5?theme.Purple:id==6?theme.Cyan:id==7?theme.Red:id==8?theme.Green:id==10?theme.Orange:theme.Accent;
            // Quiet status, not another outlined action button. Keep its footprint fixed across themes.
            using(GraphicsPath shape=new GraphicsPath())
            {
                float radius=id==8?2:id==6||id==7||id==4?4:rect.Height/2;
                float d=radius*2;
                shape.AddArc(rect.X,rect.Y,d,d,180,90);shape.AddArc(rect.Right-d,rect.Y,d,d,270,90);
                shape.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90);shape.AddArc(rect.X,rect.Bottom-d,d,d,90,90);shape.CloseFigure();
                using(SolidBrush bg=new SolidBrush(Color.FromArgb(id==5?230:215,ArtTheme.Mix(theme.Card,accent,id==5?.07:.045))))g.FillPath(bg,shape);
            }
            float x=rect.X+13,y=rect.Y+rect.Height/2;
            if(id==5)NikkiArt.Star(g,x,y,4,accent);
            else if(id==6)
            {
                using(Pen pen=new Pen(accent,1.3f)){g.DrawArc(pen,x-4,y-4,8,8,35,280);g.DrawLine(pen,x,y,x+3,y-3);}
            }
            else if(id==7)
            {
                using(Pen pen=new Pen(accent,1.3f))g.DrawLines(pen,new[]{new PointF(x-5,y),new PointF(x-2,y),new PointF(x,y-4),new PointF(x+2,y+3),new PointF(x+4,y)});
            }
            else if(id==8)
            {
                using(SolidBrush brush=new SolidBrush(accent)){g.FillRectangle(brush,x-4,y-3,3,3);g.FillRectangle(brush,x,y-3,3,3);g.FillRectangle(brush,x-4,y+1,3,3);g.FillRectangle(brush,x,y+1,3,3);}
            }
            else if(id==10)
            { using(Pen pen=new Pen(accent,1.2f)){g.DrawEllipse(pen,x-4,y-4,8,8);g.DrawLine(pen,x-3,y,x+3,y);g.DrawLine(pen,x,y-3,x,y+3);} }
            else using(SolidBrush dot=new SolidBrush(accent))g.FillEllipse(dot,x-2.5f,y-2.5f,5,5);
            string label=text.Replace("●","").Trim();
            if(label=="今日累积中")label="今日 · 累积中";
            RectangleF labelRect=new RectangleF(rect.X+24,rect.Y,rect.Width-29,rect.Height);
            using(SolidBrush ink=new SolidBrush(ArtTheme.Mix(theme.Text,accent,.18)))
            using(StringFormat format=new StringFormat{Alignment=StringAlignment.Near,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(label,font,ink,labelRect,format);
        }
    }
    internal static class ThemePattern
    {
        private static readonly ImageAttributes Soft=Opacity(.24f),Strong=Opacity(.38f);
        private static ImageAttributes Opacity(float alpha){ImageAttributes a=new ImageAttributes();ColorMatrix m=new ColorMatrix();m.Matrix33=alpha;a.SetColorMatrix(m);return a;}
        internal static bool Draw(Graphics g,RectangleF target,int variant,bool strong)
        {
#if CLEAN_EDITION
            return false;
#else
            int theme=ArtTheme.Validate(Store.ThemeId);string resource=theme<5?"CoreMaterials":"AccessiblePatterns";
            Image atlas=ThemeImages.Get(resource);if(atlas==null)return false;
            int tile=theme<5?theme:theme-5,col=tile%3,row=tile/3,tw=atlas.Width/3,th=atlas.Height/2;
            int cropW=Math.Max(24,tw/2),cropH=Math.Max(24,th/2),mode=Math.Abs(variant)%4;
            int sx=col*tw+(mode%2)*(tw-cropW),sy=row*th+(mode/2)*(th-cropH);
            InterpolationMode old=g.InterpolationMode;g.InterpolationMode=theme==8?InterpolationMode.NearestNeighbor:InterpolationMode.HighQualityBilinear;
            Rectangle dest=Rectangle.Round(target);if(dest.Width>0&&dest.Height>0)g.DrawImage(atlas,dest,sx,sy,cropW,cropH,GraphicsUnit.Pixel,strong?Strong:Soft);
            g.InterpolationMode=old;return true;
#endif
        }
    }

    internal static class AccessiblePattern
    {
        // Semantic patterns stay stable across themes: primary/active is solid,
        // secondary/idle is striped, tertiary is dotted, and auxiliary is dash-dot.
        internal static void Ring(Graphics g, RectangleF rect, float start, float sweep, float width, int semantic, Color surface)
        {
            if(sweep<2f)return;
            bool material;
            using(GraphicsPath band=new GraphicsPath())using(Pen widthPen=new Pen(Color.Black,width))
            {
                band.AddArc(rect,start,sweep);band.Widen(widthPen);GraphicsState state=g.Save();g.SetClip(band,CombineMode.Intersect);
                material=ThemePattern.Draw(g,rect,semantic,true);g.Restore(state);
            }
            Color edge=ArtTheme.Mix(surface,ArtTheme.Current.Text,.64),shade=ArtTheme.Mix(surface,ArtTheme.Current.Background,.76);
            RectangleF outer=RectangleF.Inflate(rect,width*.48f,width*.48f),inner=RectangleF.Inflate(rect,-width*.48f,-width*.48f);
            using(Pen shadow=new Pen(Color.FromArgb(material?220:175,shade),2.2f))using(Pen highlight=new Pen(Color.FromArgb(material?220:165,edge),1.25f))
            {g.DrawArc(shadow,outer,start+.7f,Math.Max(.1f,sweep-1.4f));g.DrawArc(highlight,inner,start+.7f,Math.Max(.1f,sweep-1.4f));}
            float cx=rect.X+rect.Width/2,cy=rect.Y+rect.Height/2,rad=rect.Width/2,angle=(float)((start+sweep/2)*Math.PI/180.0),rr=rad-width*.18f;
            float px=cx+(float)Math.Cos(angle)*rr,py=cy+(float)Math.Sin(angle)*rr;
            using(SolidBrush led=new SolidBrush(Color.FromArgb(material?220:175,edge)))g.FillEllipse(led,px-1.7f,py-1.7f,3.4f,3.4f);
            if(semantic<=0)return;
            using(Pen mark=new Pen(Color.FromArgb(material?185:155,edge),Math.Max(1.1f,width*.11f)))
            {
                mark.StartCap=LineCap.Flat;mark.EndCap=LineCap.Flat;
                if(semantic==1)mark.DashPattern=new float[]{5,3};else if(semantic==2)mark.DashStyle=DashStyle.Dot;else mark.DashPattern=new float[]{6,2,1,2};
                RectangleF guide=RectangleF.Inflate(rect,-width*.22f,-width*.22f);g.DrawArc(mark,guide,start+1,Math.Max(.1f,sweep-2));
            }
        }

        internal static void Heat(Graphics g, RectangleF rect, double fraction)
        {
            if (fraction <= 0 || rect.Width < 5 || rect.Height < 5) return;
            int tier = Math.Min(4, Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Min(1, fraction)) * 4)));
            if(ThemePattern.Draw(g,rect,tier,false))return;
            int theme = ArtTheme.Validate(Store.ThemeId);
            Color ink = ArtTheme.Mix(HeatScale.At(fraction), ArtTheme.Current.Text, ThemeArt.Dark ? .72 : .55);
            using (Pen pen = new Pen(Color.FromArgb(145, ink), Math.Max(1f, Math.Min(rect.Width, rect.Height) * .055f)))
            using (GraphicsPath clip = new GraphicsPath())
            {
                clip.AddRectangle(rect); GraphicsState state = g.Save(); g.SetClip(clip, CombineMode.Intersect);
                float l=rect.Left+3,t=rect.Top+3,r=rect.Right-3,b=rect.Bottom-3,cx=(l+r)/2,cy=(t+b)/2;
                if (theme == 5) // star-stitch / Nikki
                { g.DrawLine(pen,cx-3,cy,cx+3,cy); g.DrawLine(pen,cx,cy-3,cx,cy+3); if(tier>=3){g.DrawLine(pen,cx-2,cy-2,cx+2,cy+2);g.DrawLine(pen,cx+2,cy-2,cx-2,cy+2);} }
                else if (theme == 6) // tactical circuit / Halo
                { g.DrawLine(pen,l,cy,cx,cy);g.DrawLine(pen,cx,cy,r,cy);if(tier>=3){g.DrawLine(pen,cx,cy,cx,t);g.DrawEllipse(pen,cx-1.5f,cy-1.5f,3,3);} }
                else if (theme == 7) // hazard stripes / Resident Evil
                { for(float x=l-rect.Height;x<r;x+=tier>=3?7:11)g.DrawLine(pen,x,b,x+rect.Height,t); }
                else if (theme == 8) // pixel checker / Minecraft
                { float q=Math.Max(3,Math.Min(6,Math.Min(rect.Width,rect.Height)/3));using(SolidBrush dot=new SolidBrush(Color.FromArgb(115,ink)))for(int yy=0;yy<(tier>=3?2:1);yy++)for(int xx=0;xx<(tier>=4?3:2);xx++)if((xx+yy)%2==0)g.FillRectangle(dot,l+xx*q,t+yy*q,q,q); }
                else if (theme == 9) // card diamonds / Balatro
                { PointF[] d={new PointF(cx,cy-3),new PointF(cx+3,cy),new PointF(cx,cy+3),new PointF(cx-3,cy)};g.DrawPolygon(pen,d);if(tier>=3)g.DrawEllipse(pen,l,t,3,3); }
                else if (theme == 10) // ink strokes / Wuxia
                { g.DrawLine(pen,l,b,r,t);if(tier>=3)g.DrawArc(pen,cx-4,cy-4,8,8,25,245); }
                else
                { g.DrawLine(pen,l,b,r,t);if(tier>=3)g.DrawLine(pen,l,t,r,b);if(tier>=4)g.DrawEllipse(pen,cx-2,cy-2,4,4); }
                g.Restore(state);
            }
        }
        internal static void Heat(Graphics g, GraphicsPath path, RectangleF rect, double fraction)
        {
            GraphicsState state=g.Save();g.SetClip(path,CombineMode.Intersect);Heat(g,rect,fraction);g.Restore(state);
        }
    }

    internal static class HeatScale
    {
        private static readonly Color[] Stops = {
            Color.FromArgb(42,80,204), Color.FromArgb(20,180,220),
            Color.FromArgb(245,221,67), Color.FromArgb(244,131,40), Color.FromArgb(209,47,45)
        };
        private static readonly Color[] DreamStops = {Color.FromArgb(80,112,190),Color.FromArgb(83,166,189),Color.FromArgb(225,196,115),Color.FromArgb(219,133,116),Color.FromArgb(186,57,101)};
        private static readonly Color[] BalatroStops={Color.FromArgb(54,88,191),Color.FromArgb(77,189,211),Color.FromArgb(228,201,105),Color.FromArgb(233,132,91),Color.FromArgb(219,71,79)};
        private static readonly Color[] WuxiaStops={Color.FromArgb(24,67,91),Color.FromArgb(36,132,132),Color.FromArgb(213,190,107),Color.FromArgb(191,91,54),Color.FromArgb(143,35,43)};
        private static readonly Color[] HaloStops = {Color.FromArgb(47,96,181),Color.FromArgb(57,163,201),Color.FromArgb(219,216,143),Color.FromArgb(218,153,67),Color.FromArgb(205,75,54)};
        public static Color At(double fraction)
        {
            if(WuxiaArt.Active){Color[] stops=WuxiaStops;double p=Math.Max(0,Math.Min(1,fraction))*4;int i=Math.Min(3,(int)p);return ArtTheme.Mix(stops[i],stops[i+1],p-i);}
            if(BalatroArt.Active){Color[] stops=BalatroStops;double p=Math.Max(0,Math.Min(1,fraction))*4;int i=Math.Min(3,(int)p);return ArtTheme.Mix(stops[i],stops[i+1],p-i);}
            if(HaloArt.Active)
            {
                double p=Math.Max(0,Math.Min(1,fraction))*4;int i=Math.Min(3,(int)p);
                return ArtTheme.Mix(HaloStops[i],HaloStops[i+1],p-i);
            }
            if(NikkiArt.Active)
            {
                double p=Math.Max(0,Math.Min(1,fraction))*4;int i=Math.Min(3,(int)p);
                return ArtTheme.Mix(DreamStops[i],DreamStops[i+1],p-i);
            }
            double position = Math.Max(0, Math.Min(1, fraction)) * (Stops.Length - 1);
            int index = Math.Min(Stops.Length - 2, (int)position);
            return ArtTheme.Mix(Stops[index], Stops[index + 1], position - index);
        }
        public static Color Zero { get { return ArtTheme.Mix(ArtTheme.Current.Card, ArtTheme.Current.Text, 0.12); } }
        public static void Bar(Graphics g, RectangleF rect)
        {
            int steps = Math.Max(2, (int)rect.Width);
            for (int i = 0; i < steps; i++)
                using (SolidBrush brush = new SolidBrush(At((double)i / (steps - 1))))
                    g.FillRectangle(brush, rect.X + i * rect.Width / steps, rect.Y, rect.Width / steps + 0.5f, rect.Height);
        }
    }

    internal sealed class ArtTheme
    {
        public string Name;
        public Color Background, Card, Raised, Line, Text, Muted, Accent, Green, Orange, Purple, Red, Cyan, Sidebar, OnAccent;
        public int Radius;
        public static readonly ArtTheme[] All = {
            Create("午夜蓝", 13, "0F141E", "18202E", "1D2737", "303F54", "E9EDF6", "A0ABBC", "729BFF", "4BD2A8", "F1BB6F", "B19DF8", "FF6B81", "4CC9F0", "0C111A", "101622"),
            Create("奶油手账", 4, "F1EBDD", "FFFCF5", "FFFCF5", "CEC1AB", "382F27", "746452", "9C4A30", "357258", "93651B", "805C82", "B73943", "336E80", "E8DECB", "FFFFFF"),
            Create("苔原森林", 22, "101E1B", "1A2E28", "223C31", "3A5849", "E4EEE2", "A2B8A6", "AFCE88", "71CDB0", "E3B574", "C0B2D5", "ED9391", "85C6CF", "0B1714", "142319"),
            Create("霓虹夜航", 5, "130E25", "201536", "2C1B49", "604579", "F5ECFF", "C0AED7", "E68EFF", "63E3BC", "FFCB7A", "B49BFF", "FF83B4", "62DCEE", "0D091C", "200E30"),
            Create("蓝图工坊", 0, "EAF0F7", "FFFFFF", "FFFFFF", "9BB2CD", "142C4D", "4F6581", "235DB4", "237654", "976016", "7655A7", "B73353", "176C8E", "DDE7F3", "FFFFFF"),
            Create("暖暖 · 星愿织梦", 18, "F8F4FC", "FFFCFF", "F4ECF8", "DACBE3", "473B59", "796884", "9263AA", "548F8D", "B78945", "AE75A2", "BF537B", "578BAD", "F0E8F6", "FFFFFF"),
            Create("士官长 · 战术终端", 6, "0C141C", "14212A", "1C2C35", "3C5360", "E5ECE8", "A9BABF", "ADC184", "7EB99F", "D5AF62", "A0AFBF", "E27464", "63B8D8", "0A1118", "152119"),
            Create("生化危机 · 档案", 5, "111416", "1B2326", "293236", "536366", "EBEEE9", "B7C2C1", "DA7773", "8AB9A1", "D6AE72", "ACA6BA", "E27474", "7BAEBB", "0B1012", "221113"),
            Create("我的世界 · 方块", 2, "101D18", "1B2C23", "293E2D", "617458", "EDF0DB", "B5C6A5", "9BCB70", "75BDA0", "DEB26B", "B6A2D6", "DF8573", "70C9CF", "0B1711", "16220F"),
            Create("小丑牌 · 幻彩牌桌", 7, "101F23", "193037", "25424A", "507178", "F5E9CB", "ADC5C5", "F08075", "87C3A2", "E6B964", "BB99D6", "ED6F68", "6AD3DD", "0B181C", "271316"),
            Create("大侠立志传 · 碧血丹心", 5, "091318", "122226", "1C3031", "6B6048", "F2E7CA", "BDB49D", "B7453F", "54A194", "C8A55E", "8872A4", "B74449", "62B5B3", "071014", "FFF1D2")
        };
        private static Color Hex(string value) { return Color.FromArgb(255, Color.FromArgb(Convert.ToInt32(value, 16))); }
        private static ArtTheme Create(string name, int radius, params string[] c)
        {
            return new ArtTheme { Name = name, Radius = radius, Background = Hex(c[0]), Card = Hex(c[1]), Raised = Hex(c[2]),
                Line = Hex(c[3]), Text = Hex(c[4]), Muted = Hex(c[5]), Accent = Hex(c[6]), Green = Hex(c[7]), Orange = Hex(c[8]),
                Purple = Hex(c[9]), Red = Hex(c[10]), Cyan = Hex(c[11]), Sidebar = Hex(c[12]), OnAccent = Hex(c[13]) };
        }
        public static int Validate(int id)
        {
#if CLEAN_EDITION
            return 0;
#else
            return id >= 0 && id < All.Length ? id : 0;
#endif
        }
        public static ArtTheme Current { get { return All[Validate(Store.ThemeId)]; } }
        public static Color Mix(Color a, Color b, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb((int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
        }
    }

    internal sealed partial class Dashboard
    {
        private readonly RectangleF[] _themeRects = new RectangleF[ArtTheme.All.Length];
        private void PaintThemePicker(Graphics g)
        {
#if CLEAN_EDITION
            AppText(g, "外观 · 清爽版", _fSmall, Ctext, new RectangleF(24, 492, 132, 24), false);
            AppText(g, "轻量基础界面", _fAxis, Csub, new RectangleF(24, 513, 132, 18), false);
            return;
#else
            PointF mouse = ToBase(_mouse);
            string name = WuxiaArt.Active ? "碧血丹心" : ArtTheme.Current.Name;
            for (int i = 0; i < ArtTheme.All.Length; i++)
            {
                RectangleF r = new RectangleF(24 + i % 4 * 35, 516 + i / 4 * 21, 29, 16);
                _themeRects[i] = new RectangleF(r.X-2,r.Y-2,33,21);
                ArtTheme theme = ArtTheme.All[i];
                bool selected=Store.ThemeId==i;
                if(selected)using(Pen glow=new Pen(Color.FromArgb(70,Cblue),6))g.DrawRectangle(glow,r.X,r.Y,r.Width,r.Height);
                using (SolidBrush b = new SolidBrush(theme.Background)) g.FillRectangle(b, r);
                using (SolidBrush b = new SolidBrush(theme.Accent)) g.FillRectangle(b, r.X + 4, r.Y + 4, 21, 8);
                using (Pen p = new Pen(selected || _themeRects[i].Contains(mouse) ? Ctext : Cline, selected ? 3 : 1))
                    g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                if(selected)using(SolidBrush marker=new SolidBrush(Ctext))g.FillPolygon(marker,new[]{new PointF(r.Right-9,r.Bottom+1),new PointF(r.Right-3,r.Bottom+1),new PointF(r.Right-6,r.Bottom+4)});
                if (_themeRects[i].Contains(mouse)) name = i==10 ? "碧血丹心" : theme.Name;
            }
            AppText(g, "外观 · " + name, _fSmall, Ctext, new RectangleF(24, 486, 132, 24), false);
            AppText(g, "粗框 ▼ 当前主题", _fAxis, Csub, new RectangleF(24, 503, 132, 14), false);
#endif
        }
        private bool HandleThemeClick(PointF point)
        {
#if CLEAN_EDITION
            return false;
#else
            for (int i = 0; i < _themeRects.Length; i++) if (_themeRects[i].Contains(point))
            {
                Store.ThemeId = i;NikkiArt.SyncTheme();
                BackColor = Cbg;
                Store.Save();
                foreach (Form form in Application.OpenForms)
                {
                    StatsWidget widget=form as StatsWidget;if(widget!=null)widget.RefreshTheme();
                    form.Invalidate();
                }
                return true;
            }
            return false;
#endif
        }
        private void PaintThemeTexture(Graphics g)
        {
            if(WuxiaArt.Active)WuxiaArt.DrawPage(g,(int)_tab,new RectangleF(0,0,BW,BH));
            if(BalatroArt.Active)BalatroArt.DrawPage(g,(int)_tab,new RectangleF(0,0,BW,BH));
            if(MinecraftArt.Active)MinecraftArt.DrawPage(g,(int)_tab,new RectangleF(0,0,BW,BH));
            if(ResidentArt.Active)ResidentArt.DrawPage(g,(int)_tab,new RectangleF(0,0,BW,BH));
            if(HaloArt.Active)HaloArt.DrawPage(g,(int)_tab,new RectangleF(0,0,BW,BH));
            if(NikkiArt.Active)
            {
                NikkiArt.DrawPage(g,(int)_tab,new RectangleF(0,0,BW,BH));
            }
            if (Store.ThemeId == 1)
            {
                using (Pen p = new Pen(Color.FromArgb(22, Csub)))
                    for (int y = 130; y < BH; y += 8) g.DrawLine(p, ContentX, y, BW, y);
            }
            if (Store.ThemeId == 3 || Store.ThemeId == 4)
            {
                using (Pen p = new Pen(Color.FromArgb(Store.ThemeId == 3 ? 15 : 23, Cblue)))
                {
                    for (int x = ContentX; x < BW; x += 24) g.DrawLine(p, x, 0, x, BH);
                    for (int y = 0; y < BH; y += 24) g.DrawLine(p, ContentX, y, BW, y);
                }
            }
        }
    }
}

