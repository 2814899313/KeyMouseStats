using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace KeyMouseStats
{
    // A theme owns its original artwork for its entire active lifetime. No cross-theme LRU.
    internal static class ThemeAssets
    {
        private static readonly Dictionary<int,string[]> Catalog=new Dictionary<int,string[]>{
            {4,new[]{"CoreMaterials"}},
            {3,new[]{"CoreMaterials"}},
            {2,new[]{"CoreMaterials"}},
            {1,new[]{"CoreMaterials"}},
            {0,new[]{"CoreMaterials"}},
            {5,new[]{"AccessiblePatterns","NikkiEmotes","NikkiDream","NikkiPoseTrend","NikkiPoseHours","NikkiPoseKeys","NikkiPoseApps","NikkiPoseInsights"}},
            {6,new[]{"AccessiblePatterns","HaloIcons","HaloOverview","HaloTrend","HaloHours","HaloKeys","HaloApps","HaloInsights"}},
            {7,new[]{"AccessiblePatterns","ResidentIcons","ResidentOverview","ResidentTrend","ResidentHours","ResidentKeys","ResidentApps","ResidentInsights"}},
            {8,new[]{"AccessiblePatterns","MinecraftIcons","MinecraftOverview","MinecraftTrend","MinecraftHours","MinecraftKeys","MinecraftApps","MinecraftInsights"}},
            {9,new[]{"AccessiblePatterns","BalatroIcons","BalatroBackgrounds"}},
            {10,new[]{"AccessiblePatterns","WuxiaUi","WuxiaWidget","WuxiaBackgrounds"}}
        };
        internal static string[] ForTheme(int id){string[] names;return Catalog.TryGetValue(id,out names)?(string[])names.Clone():new string[0];}
    }
    internal static class ThemeImages
    {
        private sealed class Request{internal string Name;internal int Generation;}
        private static readonly object Gate=new object();
        private static readonly Dictionary<string,Image> Ready=new Dictionary<string,Image>(),Completed=new Dictionary<string,Image>();
        private static readonly HashSet<string> Pending=new HashSet<string>(),Failed=new HashSet<string>();
        private static readonly Queue<Request> Requests=new Queue<Request>();
        private static int theme=-1,generation;private static long bytes,reserved;private static bool running;
        internal const long Budget=96L*1024*1024;
        internal static int Revision,DecodeCount;
        internal static long ResidentBytes{get{return bytes;}}
        internal static long AllocatedBytes{get{lock(Gate)return reserved;}}
        internal static int Generation{get{EnsureTheme();return generation;}}
        internal static bool Loading{get{lock(Gate)return Pending.Count>0;}}
        // Called only by the UI thread, so a published bitmap cannot be disposed while being painted.
        internal static void EnsureTheme()
        {
            int next=ArtTheme.Validate(Store.ThemeId);if(theme==next)return;
            lock(Gate)
            {
                generation++;theme=next;foreach(Image image in Ready.Values)image.Dispose();Ready.Clear();
                foreach(Image image in Completed.Values)if(image!=null)image.Dispose();Completed.Clear();Pending.Clear();Failed.Clear();Requests.Clear();bytes=reserved=0;
                foreach(string name in ThemeAssets.ForTheme(theme)){if(!Pending.Add(name))continue;Requests.Enqueue(new Request{Name=name,Generation=generation});}
                if(Requests.Count>0&&!running){running=true;System.Threading.ThreadPool.QueueUserWorkItem(delegate{Decode();});}
                System.Threading.Interlocked.Increment(ref Revision);
            }
        }
        internal static Image Get(string name)
        {
            EnsureTheme();
            lock(Gate)
            {
                // Publish every completion, not only the image the current page happens to request.
                foreach(KeyValuePair<string,Image> pair in Completed){Pending.Remove(pair.Key);if(pair.Value==null)Failed.Add(pair.Key);else{Ready[pair.Key]=pair.Value;bytes+=(long)pair.Value.Width*pair.Value.Height*4;}}
                Completed.Clear();
            }
            Image result;return Ready.TryGetValue(name,out result)?result:null;
        }
        private static void Decode()
        {
            for(;;)
            {
                Request job;lock(Gate){if(Requests.Count==0){running=false;return;}job=Requests.Dequeue();}
                Image result=null;long size=0;bool reservation=false;
                try
                {
                    using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(job.Name))
                    if(stream!=null)using(Image original=Image.FromStream(stream))
                    {
                        size=(long)original.Width*original.Height*4;
                        lock(Gate){if(job.Generation==generation&&size<=Budget-reserved){reserved+=size;reservation=true;}}
                        if(reservation){result=new Bitmap(original);System.Threading.Interlocked.Increment(ref DecodeCount);}
                    }
                }
                catch(Exception error)
                {
                    if(!(error is ArgumentException||error is OutOfMemoryException||error is System.Runtime.InteropServices.ExternalException||error is IOException))throw;
                    Program.LogFailure("Theme image "+job.Name,error);
                }
                lock(Gate)
                {
                    if(job.Generation!=generation){if(result!=null)result.Dispose();continue;}
                    if(reservation&&result==null)reserved-=size;
                    Completed[job.Name]=result;System.Threading.Interlocked.Increment(ref Revision);
                }
            }
        }
    }
    internal static class NikkiArt
    {
        public static bool Active { get { return Store.ThemeId == 5; } }
        private static Image Artwork { get { return Load("NikkiDream"); } }
        private static Image Emotes { get { return Load("NikkiEmotes"); } }
        private static readonly string[] PageResources = { "NikkiDream", "NikkiPoseTrend", "NikkiPoseHours", "NikkiPoseKeys", "NikkiPoseApps", "NikkiPoseInsights" };
        public static bool HasPageArtwork(int page)
        {
            if(page<0 || page>=PageResources.Length)return false;
            if(page==0)return HasArtwork;
            return Load(PageResources[page])!=null;
        }
        public static void DrawPage(Graphics g,int page,RectangleF bounds)
        {
            if(!Active)return;
            if(page==0){Draw(g,bounds,.65f);return;}
            if(!HasPageArtwork(page))return;
            Image art=Load(PageResources[page]);
            DrawCached(g,art,new Rectangle(0,0,art.Width,art.Height),bounds,.92f,"page-background-"+page);
        }
        private static readonly Dictionary<string,Bitmap> Cache=new Dictionary<string,Bitmap>();
        private static readonly Queue<string> CacheOrder=new Queue<string>();
        private static long _cacheBytes;
        internal static int CacheBuilds;
        private static int cacheGeneration=-1,sourceSequence;
        private sealed class SourceIdentity{internal int Id;}
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Image,SourceIdentity> SourceIds=new System.Runtime.CompilerServices.ConditionalWeakTable<Image,SourceIdentity>();
        private static Bitmap background;private static string backgroundKey;
        internal static long ScaledBytes{get{return _cacheBytes+(background==null?0:(long)background.Width*background.Height*4);}}
        internal static void SyncTheme(){ResetScaledTheme();}
        private static void ResetScaledTheme()
        {
            int current=ThemeImages.Generation;if(cacheGeneration==current)return;cacheGeneration=current;
            foreach(Bitmap item in Cache.Values)item.Dispose();Cache.Clear();CacheOrder.Clear();_cacheBytes=0;
            if(background!=null)background.Dispose();background=null;backgroundKey=null;
        }
        internal static Image Load(string name)
        {
            return ThemeImages.Get(name);
        }
        public static bool HasArtwork { get { return Artwork!=null; } }
        public static bool HasEmotes { get { return Emotes!=null; } }
        public static void Draw(Graphics g, RectangleF bounds, float opacity)
        {
            if(!Active || Artwork==null) return;
            DrawCached(g,Artwork,new Rectangle(0,0,Artwork.Width,Artwork.Height),bounds,opacity,"background");
        }
        public static void Sticker(Graphics g,int index,RectangleF bounds)
        {
            if(!Active || Emotes==null)return;
            index=Math.Max(0,Math.Min(5,index));
            DrawCached(g,Emotes,new Rectangle(index%3*Emotes.Width/3,index/3*Emotes.Height/2,Emotes.Width/3,Emotes.Height/2),bounds,1,"emote"+index);
        }
        // Pre-scale and premultiply alpha once per device size, then blit without a scaling transform.
        // Bounded cache: resize/DPI changes cannot retain unbounded full-window bitmaps.
        internal static void DrawCached(Graphics g,Image image,Rectangle source,RectangleF bounds,float opacity,string name)
        {
            ResetScaledTheme();
            PointF[] corners={new PointF(bounds.Left,bounds.Top),new PointF(bounds.Right,bounds.Bottom)};
            using(Matrix matrix=g.Transform)matrix.TransformPoints(corners);
            Rectangle destination=Rectangle.Round(RectangleF.FromLTRB(corners[0].X,corners[0].Y,corners[1].X,corners[1].Y));
            if(destination.Width<=0||destination.Height<=0)return;
            if((long)destination.Width*destination.Height*4>36*1024*1024 || (destination.Width<400||destination.Height<200)&&(long)destination.Width*destination.Height*4>4*1024*1024)
            {
                using(ImageAttributes attributes=new ImageAttributes())
                {ColorMatrix alpha=new ColorMatrix();alpha.Matrix33=opacity;attributes.SetColorMatrix(alpha);g.DrawImage(image,Rectangle.Round(bounds),source.X,source.Y,source.Width,source.Height,GraphicsUnit.Pixel,attributes);}
                return;
            }
            int identity=SourceIds.GetValue(image,delegate(Image sourceImage){return new SourceIdentity{Id=++sourceSequence};}).Id;
            string key=identity+":"+source+":"+name+":"+destination.Width+":"+destination.Height+":"+opacity.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            bool large=destination.Width>=400&&destination.Height>=200;
            Bitmap cached;
            if(large?(cached=backgroundKey==key?background:null)==null:!Cache.TryGetValue(key,out cached))
            {
                long bytes=(long)destination.Width*destination.Height*4;
                while(!large && CacheOrder.Count>0 && (_cacheBytes+bytes>4*1024*1024 || Cache.Count>=128))
                { string old=CacheOrder.Dequeue();Bitmap bitmap=Cache[old];_cacheBytes-=(long)bitmap.Width*bitmap.Height*4;Cache.Remove(old);bitmap.Dispose(); }
                cached=new Bitmap(destination.Width,destination.Height,PixelFormat.Format32bppPArgb);
                using(Graphics buffer=Graphics.FromImage(cached))
                using(ImageAttributes attributes=new ImageAttributes())
                {
                    buffer.CompositingMode=CompositingMode.SourceCopy;buffer.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    ColorMatrix alpha=new ColorMatrix();alpha.Matrix33=opacity;attributes.SetColorMatrix(alpha);
                    buffer.DrawImage(image,new Rectangle(0,0,cached.Width,cached.Height),source.X,source.Y,source.Width,source.Height,GraphicsUnit.Pixel,attributes);
                }
                if(large){if(background!=null)background.Dispose();background=cached;backgroundKey=key;}
                else {Cache[key]=cached;CacheOrder.Enqueue(key);_cacheBytes+=bytes;}CacheBuilds++;
            }
            GraphicsState state=g.Save();g.ResetTransform();g.DrawImageUnscaled(cached,destination.Location);g.Restore(state);
        }
        public static void Star(Graphics g,float x,float y,float radius,Color color)
        {
            PointF[] points={new PointF(x,y-radius),new PointF(x+radius*.24f,y-radius*.24f),new PointF(x+radius,y),new PointF(x+radius*.24f,y+radius*.24f),new PointF(x,y+radius),new PointF(x-radius*.24f,y+radius*.24f),new PointF(x-radius,y),new PointF(x-radius*.24f,y-radius*.24f)};
            using(SolidBrush brush=new SolidBrush(color))g.FillPolygon(brush,points);
        }
        public static void Menu(ContextMenuStrip menu)
        {
            menu.Opening+=delegate
            {
                menu.Renderer=ThemeArt.Active?new ToolStripProfessionalRenderer(new NikkiMenuColors()):new ToolStripProfessionalRenderer();
                menu.BackColor=ThemeArt.Active?ArtTheme.Current.Card:SystemColors.Control;
                menu.ForeColor=ThemeArt.Active?ArtTheme.Current.Text:SystemColors.ControlText;
                foreach(ToolStripItem item in menu.Items) item.ForeColor=menu.ForeColor;
            };
        }
    }
    internal sealed class NikkiMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return ArtTheme.Current.Card; } }
        public override Color ImageMarginGradientBegin { get { return ArtTheme.Current.Raised; } }
        public override Color ImageMarginGradientMiddle { get { return ArtTheme.Current.Raised; } }
        public override Color ImageMarginGradientEnd { get { return ArtTheme.Current.Raised; } }
        public override Color MenuItemSelected { get { return ArtTheme.Mix(ArtTheme.Current.Card,ArtTheme.Current.Accent,.22); } }
        public override Color MenuItemBorder { get { return ArtTheme.Current.Line; } }
        public override Color MenuBorder { get { return ArtTheme.Current.Line; } }
        public override Color SeparatorDark { get { return ArtTheme.Current.Line; } }
        public override Color SeparatorLight { get { return ArtTheme.Current.Card; } }
    }
    internal sealed class NikkiNotice : ThemedDialog
    {
        public NikkiNotice(string text,string caption,MessageBoxButtons buttons)
        {
            Text=caption;Font=new Font("Microsoft YaHei UI",9f);AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(520,245);StartPosition=FormStartPosition.CenterParent;
            FormBorderStyle=FormBorderStyle.FixedDialog;MinimizeBox=false;MaximizeBox=false;ShowInTaskbar=false;
            TextBox content=new TextBox { Text=text,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,Location=new Point(24,28),Size=new Size(472,145) };
            Controls.Add(content);
            Button accept=new Button { Text=buttons==MessageBoxButtons.YesNo?"确定清空":"确定",DialogResult=buttons==MessageBoxButtons.YesNo?DialogResult.Yes:DialogResult.OK,Location=new Point(280,196),Size=new Size(100,30) };
            Controls.Add(accept);
            if(buttons==MessageBoxButtons.YesNo)
            {
                Button cancel=new Button {Text="取消",DialogResult=DialogResult.No,Location=new Point(396,196),Size=new Size(100,30)};
                Controls.Add(cancel);CancelButton=cancel;AcceptButton=cancel;
            }
            else {accept.Location=new Point(396,196);AcceptButton=accept;CancelButton=accept;}
        }
    }
    internal static class ThemeMessage
    {
        public static DialogResult Show(string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon)
        { return Show(null,text,caption,buttons,icon); }
        public static DialogResult Show(IWin32Window owner,string text,string caption="键鼠统计",MessageBoxButtons buttons=MessageBoxButtons.OK,MessageBoxIcon icon=MessageBoxIcon.Information)
        {
            if(!ThemeArt.Active || (buttons!=MessageBoxButtons.OK && buttons!=MessageBoxButtons.YesNo))return MessageBox.Show(owner,text,caption,buttons,icon);
            using(NikkiNotice notice=new NikkiNotice(text,caption,buttons)) return notice.ShowDialog(owner);
        }
    }
    internal class ThemedDialog : Form
    {
        private bool _styled;
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if(ThemeArt.Active && !_styled) { _styled=true; Style(this); }
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            if(ThemeArt.Active)
            {
                // Quiet corner emotes replace the repeated full-window illustration in dialogs.
                int emote=this is SessionDetails||this is ActivitySettings?3:this is AppCategoryDialog?4:5;
                float size=Math.Min(56,ClientSize.Height*.35f);
                RectangleF corner=new RectangleF(16,ClientSize.Height-size-4,size,size);
                if(this is SessionDetails)corner=new RectangleF(ClientSize.Width-80,0,68,68);
                if(this is StatisticsReport)corner=new RectangleF(ClientSize.Width-38,0,32,32);
                ThemeArt.Sticker(e.Graphics,emote,corner);
            }
        }
        protected static void Style(Control control)
        {
            ArtTheme theme=ArtTheme.Current;
            control.BackColor=control is Form?theme.Background:theme.Card;control.ForeColor=theme.Text;
            Button button=control as Button;
            if(button!=null)
            {
                button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=WuxiaArt.Active?theme.Orange:theme.Line;
                button.FlatAppearance.BorderSize=WuxiaArt.Active?1:1;
                button.FlatAppearance.MouseOverBackColor=ArtTheme.Mix(theme.Card,WuxiaArt.Active?theme.Green:theme.Accent,.18);
                button.FlatAppearance.MouseDownBackColor=ArtTheme.Mix(theme.Card,WuxiaArt.Active?theme.Red:theme.Accent,.30);
                button.BackColor=theme.Raised;button.UseVisualStyleBackColor=false;
            }
            ComboBox combo=control as ComboBox;if(combo!=null)combo.FlatStyle=FlatStyle.Flat;
            DateTimePicker date=control as DateTimePicker;
            if(date!=null) { date.CalendarMonthBackground=theme.Card;date.CalendarForeColor=theme.Text;date.CalendarTitleBackColor=theme.Raised;date.CalendarTitleForeColor=theme.Text;date.CalendarTrailingForeColor=theme.Muted; }
            Label label=control as Label;if(label!=null)label.BackColor=Color.Transparent;
            ListView list=control as ListView;
            if(list!=null)
            {
                list.BorderStyle=BorderStyle.FixedSingle;list.GridLines=false;list.OwnerDraw=true;
                list.DrawColumnHeader+=delegate(object sender,DrawListViewColumnHeaderEventArgs args)
                {
                    using(SolidBrush brush=new SolidBrush(theme.Raised))args.Graphics.FillRectangle(brush,args.Bounds);
                    Rectangle bounds=args.Bounds;bounds.Inflate(-8,0);
                    TextRenderer.DrawText(args.Graphics,args.Header.Text,list.Font,bounds,theme.Text,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                };
                list.DrawItem+=delegate(object sender,DrawListViewItemEventArgs args) { if(list.View!=View.Details)args.DrawDefault=true; };
                list.DrawSubItem+=delegate(object sender,DrawListViewSubItemEventArgs args)
                {
                    Color fill=args.Item.Selected?ArtTheme.Mix(theme.Card,theme.Accent,.27):args.ItemIndex%2==0?theme.Card:theme.Background;
                    if(list is ReportList&&!args.Item.Selected)fill=args.ItemIndex%2==0?theme.Card:ArtTheme.Mix(theme.Card,theme.Text,.055);
                    ReportList report=list as ReportList;bool hover=report!=null&&report.Hovered==args.ItemIndex;
                    if(hover)fill=ArtTheme.Mix(theme.Card,theme.Accent,.18);
                    using(SolidBrush brush=new SolidBrush(fill))args.Graphics.FillRectangle(brush,args.Bounds);
                    Rectangle bounds=args.Bounds;bounds.Inflate(-8,0);
                    TextRenderer.DrawText(args.Graphics,args.SubItem.Text,list.Font,bounds,theme.Text,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                    if(hover&&args.ColumnIndex==0)using(Pen line=new Pen(theme.Accent,2))args.Graphics.DrawLine(line,bounds.Left,bounds.Bottom-3,bounds.Right,bounds.Bottom-3);
                    if(args.Item.Focused && args.ColumnIndex==0)args.DrawFocusRectangle(args.Bounds);
                };
            }
            TabControl tabs=control as TabControl;
            if(tabs!=null)
            {
                tabs.Padding=new Point(12,7);
                tabs.DrawMode=TabDrawMode.OwnerDrawFixed;
                tabs.DrawItem+=delegate(object sender,DrawItemEventArgs args)
                {
                    using(SolidBrush brush=new SolidBrush(args.Index==tabs.SelectedIndex?theme.Raised:theme.Background))args.Graphics.FillRectangle(brush,args.Bounds);
                    TextRenderer.DrawText(args.Graphics,tabs.TabPages[args.Index].Text,tabs.Font,args.Bounds,theme.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                };
            }
            foreach(Control child in control.Controls)Style(child);
        }
    }
}



