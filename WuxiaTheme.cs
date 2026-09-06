using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace KeyMouseStats
{
    internal static class WuxiaArt
    {
        internal static bool Active { get { return Store.ThemeId == 10; } }
        internal static readonly int[] NavSymbols = { 0, 1, 2, 3, 4, 5 };
        internal static readonly int[] MetricSymbols = { 6, 7, 8, 9, 10, 11, 12, 13 };
        internal static readonly string[] PageLabels = { "神捕 · 江湖录", "本草 · 行迹图", "红玉 · 十二时辰", "紫烟 · 百键谱", "倾城 · 万象册", "彩蝶 · 悟心鉴" };

        private static Image Backgrounds { get { return NikkiArt.Load("WuxiaBackgrounds"); } }
        private static Image Ui { get { return NikkiArt.Load("WuxiaUi"); } }
        private static Image Widget { get { return NikkiArt.Load("WuxiaWidget"); } }
        internal static bool HasBackgrounds { get { return Backgrounds != null; } }
        internal static bool HasUi { get { return Ui != null; } }
        internal static bool HasWidget { get { return Widget != null; } }

        internal static void DrawPage(Graphics g, int page, RectangleF bounds)
        {
            Image atlas = Backgrounds; if (!Active || atlas == null || page < 0 || page >= 6) return;
            int col = page % 2, row = page / 2;
            Rectangle source = Rectangle.FromLTRB(col * atlas.Width / 2, row * atlas.Height / 3, (col + 1) * atlas.Width / 2, (row + 1) * atlas.Height / 3);
            source = Cover(source, bounds);
            NikkiArt.DrawCached(g, atlas, source, bounds, .90f, "wuxia-page-" + page);
        }

        internal static void DrawWidget(Graphics g, RectangleF bounds)
        {
            Image image = Widget; if (!Active || image == null) return;
            Rectangle source=Cover(new Rectangle(0,0,image.Width,image.Height),bounds);
            NikkiArt.DrawCached(g, image, source, bounds, .96f, "wuxia-widget");
        }

        internal static Rectangle Cover(Rectangle source, RectangleF destination)
        {
            if(source.Width<=0||source.Height<=0||destination.Width<=0||destination.Height<=0)return source;
            double sourceAspect=(double)source.Width/source.Height,targetAspect=destination.Width/destination.Height;
            if(sourceAspect>targetAspect)
            {
                int width=Math.Max(1,(int)Math.Round(source.Height*targetAspect));
                return new Rectangle(source.X+(source.Width-width)/2,source.Y,width,source.Height);
            }
            if(sourceAspect<targetAspect)
            {
                int height=Math.Max(1,(int)Math.Round(source.Width/targetAspect));
                return new Rectangle(source.X,source.Y+(source.Height-height)/2,source.Width,height);
            }
            return source;
        }

        internal static void Symbol(Graphics g, int kind, RectangleF bounds, float opacity = 1f)
        {
            Image atlas = Ui; if (!Active || atlas == null) return; kind = Math.Max(0, Math.Min(23, kind));
            int col = kind % 6, row = kind / 6;
            Rectangle source = Rectangle.FromLTRB(col * atlas.Width / 6, row * atlas.Height / 4, (col + 1) * atlas.Width / 6, (row + 1) * atlas.Height / 4);
            GraphicsState state=g.Save();
            using(GraphicsPath clip=Dashboard.RoundedRect(bounds.X,bounds.Y,bounds.Width,bounds.Height,Math.Min(6,Math.Min(bounds.Width,bounds.Height)*.18f)))g.SetClip(clip,CombineMode.Intersect);
            NikkiArt.DrawCached(g, atlas, source, bounds, opacity, "wuxia-ui-" + kind);g.Restore(state);
        }

        internal static void CardOrnaments(Graphics g, RectangleF r)
        {
            Color gold = ArtTheme.Current.Orange;
            using (Pen p = new Pen(Color.FromArgb(155, gold), 1.25f))
            {
                g.DrawLine(p, r.X + 8, r.Y, r.X + 34, r.Y); g.DrawLine(p, r.X, r.Y + 8, r.X, r.Y + 30);
                g.DrawLine(p, r.Right - 34, r.Bottom, r.Right - 8, r.Bottom); g.DrawLine(p, r.Right, r.Bottom - 30, r.Right, r.Bottom - 8);
                g.DrawArc(p, r.X + 1, r.Y + 1, 14, 14, 180, 90); g.DrawArc(p, r.Right - 15, r.Bottom - 15, 14, 14, 0, 90);
            }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(190, gold)))
            { g.FillPolygon(b, new[] { new PointF(r.X + 7, r.Y + 1), new PointF(r.X + 11, r.Y + 5), new PointF(r.X + 7, r.Y + 9), new PointF(r.X + 3, r.Y + 5) }); }
        }

        internal static void Button(Graphics g, RectangleF r, bool selected, bool hover)
        {
            Color edge = selected ? ArtTheme.Current.Red : hover ? ArtTheme.Current.Green : ArtTheme.Current.Orange;
            using (GraphicsPath p = Dashboard.RoundedRect(r.X, r.Y, r.Width, r.Height, 4))
            using (LinearGradientBrush fill = new LinearGradientBrush(r,
                Color.FromArgb(selected ? 238 : 220, selected ? Color.FromArgb(93, 31, 27) : Color.FromArgb(18, 39, 39)),
                Color.FromArgb(selected ? 232 : 220, selected ? Color.FromArgb(45, 17, 18) : Color.FromArgb(11, 24, 28)), 90))
            using (Pen pen = new Pen(Color.FromArgb(selected || hover ? 235 : 150, edge), selected ? 1.8f : 1.1f))
            { g.FillPath(fill, p); g.DrawPath(pen, p); }
            using (Pen p = new Pen(Color.FromArgb(125, edge)))
            { g.DrawLine(p, r.X + 7, r.Y + 3, r.X + 17, r.Y + 3); g.DrawLine(p, r.Right - 17, r.Bottom - 3, r.Right - 7, r.Bottom - 3); }
            if (selected) using (SolidBrush b = new SolidBrush(ArtTheme.Current.Red))
                g.FillPolygon(b, new[] { new PointF(r.X + 4, r.Y + 4), new PointF(r.X + 10, r.Y + 4), new PointF(r.X + 4, r.Y + 10) });
        }

        internal static int KeyOrnament(HeatKey key)
        {
            if (key.Label == "Space") return 23;
            if (key.Label == "←" || key.Label == "→" || key.Label == "↑" || key.Label == "↓") return 22;
            if (key.Label.StartsWith("F") || key.Label == "Esc") return 20;
            if (key.Label.Length == 1 && char.IsDigit(key.Label[0])) return 19;
            if (key.Label.Length == 1 && char.IsLetter(key.Label[0])) return 18;
            return 21;
        }

        internal static void KeyCap(Graphics g, HeatKey key, RectangleF rect, bool champion)
        {
            int ornament = KeyOrnament(key);
            float size = Math.Min(rect.Height * .44f, ornament == 23 ? 26 : 13);
            RectangleF mark = ornament == 23 ? new RectangleF(rect.Right - Math.Min(34, rect.Width * .36f), rect.Y + 2, Math.Min(32, rect.Width * .34f), size)
                : new RectangleF(rect.X + 2, rect.Y + 2, size, size);
            Symbol(g, ornament, mark, champion ? .88f : .48f);
            using (Pen inner = new Pen(Color.FromArgb(champion ? 210 : 90, champion ? ArtTheme.Current.Orange : ArtTheme.Current.Green), champion ? 1.2f : .65f))
                g.DrawRectangle(inner, rect.X + 2, rect.Y + 2, Math.Max(1, rect.Width - 4), Math.Max(1, rect.Height - 4));
        }
    }
}
