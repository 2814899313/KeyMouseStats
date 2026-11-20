using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal static class ReportDesign
    {
        public static readonly string[] Names={"输入习惯","实时状态","鼠标动作","分布","按住时长趋势"};
        public static readonly string[] Scope={"操作强度描述使用方式，不代表工作效率。","","方向和快速移动是行为描述，不推断左右手或操作效果。","用箱线图与直方图看分布，不只比均值。","累计按住按逐键求和；有键按下是墙钟并集，不重复计时。"};
        public static readonly string[] Metrics={"每次点击的击键数","每秒活跃击键","每分钟活跃点击","每次操作移动","每分钟滚轮事件","组合键使用占比"};
        public static readonly string[] Units={"次击键 / 点击","次 / 活跃秒","次 / 活跃分钟","厘米 / 操作","次 / 活跃分钟","%"};
        public static readonly string[] Definitions={
            "击键次数 ÷ 鼠标点击次数。表示键盘与鼠标的使用比例；没有点击时不能计算。",
            "击键次数 ÷ 活跃秒数。活跃时间包括空闲阈值以内的短暂停顿，不等于一直打字。",
            "鼠标点击次数 ÷ 活跃分钟数。它描述操作密度，不评价效率；没有活跃时间时不能计算。",
            "估算移动距离 ÷（击键 + 点击）。卡片以厘米显示；距离受鼠标 DPI 设置影响。",
            "滚轮事件次数 ÷ 活跃分钟数。事件次数不等于滚动距离；没有活跃时间时不能计算。",
            "组合动作次数 ÷ 击键次数 × 100%。组合动作与击键有重叠，不能相加作为总操作量。"};
        public static string Number(double value)
        {
            if(double.IsNaN(value)||double.IsInfinity(value))return "—";
            if(value>0&&value<.01)return "<0.01";
            if(value<0&&value>-.01)return ">-0.01";
            return value.ToString(Math.Abs(value)>=1000?"N0":"0.##",CultureInfo.InvariantCulture);
        }
        public static string Display(string value)
        {double number;if(value=="--")return "—";return value!=null&&value.IndexOf('.')>=0&&double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out number)?Number(number):value;}
        public static GraphicsPath Round(RectangleF r,float radius)
        {
            GraphicsPath path=new GraphicsPath();float d=Math.Max(1,Math.Min(Math.Min(r.Width,r.Height),radius*2));
            path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        public static void Surface(Graphics g,RectangleF rect,Color fill,Color line)
        {using(GraphicsPath path=Round(rect,Math.Min(12,Math.Max(3,ArtTheme.Current.Radius)))){using(SolidBrush b=new SolidBrush(fill))g.FillPath(b,path);using(Pen p=new Pen(line))g.DrawPath(p,path);}}
        public static void Text(Graphics g,string text,Font font,Color color,RectangleF rect)
        {using(SolidBrush b=new SolidBrush(color))using(StringFormat f=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(text,font,b,rect,f);}

        // ---- 连续段(专注段)口径:面板与报告共用同一套阈值和公式 ----------------------
        // 以前两处各写一遍,面板按浮点算成 98%、报告按整数除法算成 97%;
        // 而且分子把 5–30 分钟的段也算成"碎片",和左侧四档分布卡的第一档语义打架。

        /// <summary>四档段长分布的边界(秒):5 / 30 / 60 分钟。标签与阈值必须一起看。</summary>
        public static readonly string[] SegmentBinNames={"< 5 分钟","5–30 分钟","30–60 分钟","≥ 60 分钟"};
        /// <summary>短段(旧称碎片)阈值:短于 5 分钟的段,同时是四档分布第一档的上界。</summary>
        public const double ShortSegmentSeconds=300;
        /// <summary>第二档上界:30 分钟。</summary>
        public const double MediumSegmentSeconds=1800;
        /// <summary>第三档上界:60 分钟。恰好 3600 秒归入最后一档(标签是「≥ 60 分钟」)。</summary>
        public const double LongSegmentSeconds=3600;
        /// <summary>段长落在第几档:0 &lt;5 分钟 / 1 5–30 分钟 / 2 30–60 分钟 / 3 ≥60 分钟。</summary>
        public static int SegmentBin(double seconds)
        {
            if(seconds<ShortSegmentSeconds)return 0;
            if(seconds<MediumSegmentSeconds)return 1;
            if(seconds<LongSegmentSeconds)return 2;
            return 3;
        }
        /// <summary>把一组段按四档计数。空值与非正时长不计入,与面板的 sessions 过滤一致。</summary>
        public static int[] SegmentBins(IEnumerable<ActiveSession> sessions)
        {
            int[] bins=new int[4];
            if(sessions==null)return bins;
            foreach(ActiveSession session in sessions)
                if(session!=null&&session.Seconds>0)bins[SegmentBin(session.Seconds)]++;
            return bins;
        }
        /// <summary>
        /// 短段占比 = 短于 5 分钟的段数 ÷ 总段数。只把第一档算碎片,与左侧「&lt; 5 分钟」卡对齐;
        /// 按段数加权而不是时长:它描述"有多少段是短的",不描述"多少时间被切碎"。
        /// </summary>
        public static string ShortShare(int shortCount,int total)
        {
            if(total<=0)return "--";
            return (100.0*shortCount/total).ToString("0",CultureInfo.InvariantCulture)+"%";
        }
    }
    internal sealed class ReportPage : Panel
    {
        internal Control Chart;internal ReportList List;internal int Index;internal int Kind;
        internal string Note;internal bool DetailsOpen;
        private readonly Label scope=new Label();
        private readonly Button details=new Button();
        internal event EventHandler SizeRequested;
        public ReportPage()
        {
            DoubleBuffered=true;
            Controls.Add(scope);Controls.Add(details);
            details.FlatStyle=FlatStyle.Flat;details.TextAlign=ContentAlignment.MiddleLeft;
            details.Click+=delegate{DetailsOpen=!DetailsOpen;if(SizeRequested!=null)SizeRequested(this,EventArgs.Empty);};
        }
        internal int Arrange(int width,float scale)
        {
            ArtTheme t=ArtTheme.Current;BackColor=(GujianArt.Active||OriArt.Active)?Color.Transparent:t.Background;if(List!=null)List.SetScale(scale);
            int gap=(int)(14*scale),y=0;
            scope.AutoEllipsis=true;scope.Text=ReportDesign.Scope[Index];scope.Visible=scope.Text.Length>0;scope.ForeColor=t.Muted;scope.BackColor=(GujianArt.Active||OriArt.Active)?Color.Transparent:t.Background;
            scope.SetBounds(0,0,width,scope.Visible?(int)(36*scale):0);y+=scope.Height;
            ReportVisual visual=Chart as ReportVisual;if(visual!=null)visual.UiScale=scale;
            CrossReportVisual cross=Chart as CrossReportVisual;if(cross!=null)cross.UiScale=scale;
            RangeReportVisual rangeVisual=Chart as RangeReportVisual;if(rangeVisual!=null)rangeVisual.UiScale=scale;
            RhythmReportVisual rhythmVisual=Chart as RhythmReportVisual;if(rhythmVisual!=null)rhythmVisual.UiScale=scale;
            DistributionReportVisual distributionVisual=Chart as DistributionReportVisual;if(distributionVisual!=null)distributionVisual.UiScale=scale;
            HoldReportVisual holdVisual=Chart as HoldReportVisual;if(holdVisual!=null)holdVisual.UiScale=scale;
            AppKeyReportVisual appKeyVisual=Chart as AppKeyReportVisual;if(appKeyVisual!=null)appKeyVisual.UiScale=scale;
            HoldTrendVisual holdTrendVisual=Chart as HoldTrendVisual;if(holdTrendVisual!=null)holdTrendVisual.UiScale=scale;
            int chartHeight=(int)(scale*(Kind==0?ReportVisual.HabitHeight(width/scale):Kind==4?560:Kind==13?430:Kind==16?400:300));
            Chart.SetBounds(0,y,width,chartHeight);y+=chartHeight+gap;
            details.Text=(DetailsOpen?"▾ 收起底层明细":Kind==4?"▸ 查看底层计算口径与采样规范 ↗":"▸ 查看底层计算公式与日志明细 ↗");
            details.ForeColor=t.Accent;details.BackColor=t.Card;details.FlatAppearance.BorderColor=t.Line;
            details.SetBounds(0,y,width,(int)(38*scale));y+=details.Height;
            List.Visible=DetailsOpen;
            if(DetailsOpen)
            {
                y+=gap;int listHeight=(int)(Math.Min(350,Math.Max(140,40+List.Items.Count*32))*scale);
                List.SetBounds(0,y,width,listHeight);y+=listHeight;
            }
            return y+gap;
        }
    }
    internal sealed class ReportDeck : Panel
    {
        public readonly List<ReportPage> TabPages=new List<ReportPage>();
        public float UiScale=1;private int selected=-1;private bool arranging;
        public event EventHandler SelectedIndexChanged;
        public ReportDeck(){DoubleBuffered=true;AutoScroll=true;}
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            if(GujianArt.Active){GujianArt.DrawReport(e.Graphics,new RectangleF(0,0,ClientSize.Width,ClientSize.Height),.34f);using(SolidBrush veil=new SolidBrush(Color.FromArgb(172,ArtTheme.Current.Background)))e.Graphics.FillRectangle(veil,ClientRectangle);}
            else if(OriArt.Active){OriArt.DrawReport(e.Graphics,new RectangleF(0,0,ClientSize.Width,ClientSize.Height),.28f);using(SolidBrush veil=new SolidBrush(Color.FromArgb(165,ArtTheme.Current.Background)))e.Graphics.FillRectangle(veil,ClientRectangle);}
        }
        public ReportPage SelectedTab {get{return selected>=0&&selected<TabPages.Count?TabPages[selected]:null;}}
        public int SelectedIndex {get{return selected;}set{if(value<0||value>=TabPages.Count)return;if(SelectedTab!=null)SelectedTab.Visible=false;selected=value;SelectedTab.Visible=true;AutoScrollPosition=Point.Empty;PerformLayout();if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}}
        public void AddPage(ReportPage page){TabPages.Add(page);page.Visible=false;Controls.Add(page);page.SizeRequested+=delegate{PerformLayout();if(page.DetailsOpen&&page.List!=null)AutoScrollPosition=new Point(0,Math.Max(0,page.List.Top-8));};}
        public void ClearPages(){selected=-1;foreach(ReportPage page in TabPages)page.Dispose();TabPages.Clear();Controls.Clear();AutoScrollMinSize=Size.Empty;}
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);if(arranging||SelectedTab==null)return;arranging=true;
            try{int width=Math.Max(200,ClientSize.Width-SystemInformation.VerticalScrollBarWidth-2);int height=SelectedTab.Arrange(width,UiScale);SelectedTab.SetBounds(AutoScrollPosition.X,AutoScrollPosition.Y,width,height);AutoScrollMinSize=new Size(0,height);}
            finally{arranging=false;}
        }
    }
    internal sealed class ReportHeader : Control
    {
        internal float UiScale=1;internal string Heading="输入习惯",Status="",Date="";
        public ReportHeader(){DoubleBuffered=true;ResizeRedraw=true;}
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.ScaleTransform(UiScale,UiScale);float width=Width/UiScale;ArtTheme t=ArtTheme.Current;
            if(GujianArt.Active){GujianArt.DrawReport(g,new RectangleF(0,0,width,Height/UiScale),.62f);using(LinearGradientBrush veil=new LinearGradientBrush(new RectangleF(0,0,Math.Max(1,width),120),Color.FromArgb(190,t.Card),Color.FromArgb(220,t.Background),0f))g.FillRectangle(veil,0,0,width,Height/UiScale);}
            else if(OriArt.Active){OriArt.DrawReport(g,new RectangleF(0,0,width,Height/UiScale),.54f);using(LinearGradientBrush veil=new LinearGradientBrush(new RectangleF(0,0,Math.Max(1,width),120),Color.FromArgb(190,t.Card),Color.FromArgb(215,t.Background),0f))g.FillRectangle(veil,0,0,width,Height/UiScale);}
            else using(LinearGradientBrush bg=new LinearGradientBrush(new RectangleF(0,0,Math.Max(1,width),120),t.Card,t.Background,0f))g.FillRectangle(bg,0,0,width,Height/UiScale);
            using(Font title=new Font("Microsoft YaHei UI",25,FontStyle.Bold,GraphicsUnit.Pixel))
            using(Font body=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel))
            {
                ReportDesign.Text(g,Heading,title,t.Text,new RectangleF(24,19,270,39));
                if(GujianArt.Active)GujianArt.Symbol(g,14,new RectangleF(264,15,42,42));
                else if(OriArt.Active)OriArt.Symbol(g,5,new RectangleF(264,15,42,42));
                else if(EuroTruckArt.Active)EuroTruckArt.Symbol(g,6,new RectangleF(264,15,42,42));
                else if(WuxiaArt.Active)WuxiaArt.Symbol(g,0,new RectangleF(264,15,42,42));
                else if(BalatroArt.Active)BalatroArt.Symbol(g,8,new RectangleF(264,19,34,34));
                ReportDesign.Text(g,Date+"   ·   "+Status,body,t.Muted,new RectangleF(24,64,width-48,23));
            }
            using(Pen line=new Pen(t.Line))g.DrawLine(line,24,Height/UiScale-1,width-24,Height/UiScale-1);
        }
    }
    /// <summary>
    /// 报告页「说明」抽屉。页面高度不再因说明文字伸缩:打开时截取报告并做轻度模糊,
    /// 右侧滑出抽屉,关闭后恢复。模糊只在小图上做,避免每帧处理整窗像素。
    /// </summary>
    internal sealed class ReportHelpOverlay : Form
    {
        private readonly Timer _timer=new Timer{Interval=16};
        private readonly Font _titleFont=new Font("Microsoft YaHei UI",18f,FontStyle.Bold,GraphicsUnit.Pixel);
        private readonly Font _bodyFont=new Font("Microsoft YaHei UI",12f,GraphicsUnit.Pixel);
        private readonly Font _smallFont=new Font("Microsoft YaHei UI",11f,GraphicsUnit.Pixel);
        private float _progress,_scroll,_scale=1;
        private int _direction;private string _title="",_scope="",_note="";
        private RectangleF _closeRect,_drawerRect;private float _contentHeight;
        internal ReportHelpOverlay()
        {
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;KeyPreview=true;
            BackColor=ArtTheme.Current.Background;DoubleBuffered=true;TopMost=true;
            _timer.Tick+=delegate
            {
                _progress+=_direction*0.12f;
                if(_direction>0&&_progress>=1){_progress=1;_timer.Stop();}
                if(_direction<0&&_progress<=0){_progress=0;_timer.Stop();Close();}
                Invalidate();
            };
        }
        private void Prepare(Control source,string title,string scope,string note,float scale)
        {
            _title=title??"";_scope=scope??"";_note=note??"";_scale=scale>0?scale:1;_scroll=0;_direction=1;_progress=0;
            Form owner=source==null?null:source.FindForm();
            ClientSize=owner==null?new Size(900,700):owner.ClientSize;
            if(owner!=null)Location=owner.PointToScreen(Point.Empty);
            Owner=owner;
        }
        internal void ShowHelp(Control source,string title,string scope,string note,float scale)
        {
            Prepare(source,title,scope,note,scale);
            _timer.Start();
            Form owner=Owner;
            if(owner!=null)ShowDialog(owner);else ShowDialog();
        }
        /// <summary>离屏预览用:不阻塞、由调用方关闭。</summary>
        internal void ShowPreview(Control source,string title,string scope,string note,float scale)
        {
            Prepare(source,title,scope,note,scale);
            Show(Owner);_timer.Start();
        }
        internal void CloseHelp()
        {
            if(!Visible)return;
            _direction=-1;_timer.Start();
        }
        private float Ease(float t){return t*t*(3-2*t);}
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float s=_scale>0?_scale:1,p=Ease(Math.Max(0,Math.Min(1,_progress)));
            using(SolidBrush bg=new SolidBrush(ArtTheme.Current.Background))g.FillRectangle(bg,ClientRectangle);
            using(SolidBrush scrim=new SolidBrush(Color.FromArgb((int)(155*p),0,0,0)))g.FillRectangle(scrim,ClientRectangle);
            float drawerW=Math.Min(470*s,Width*0.9f),drawerX=Width-drawerW*p;
            _drawerRect=new RectangleF(drawerX,0,drawerW,Height);
            using(SolidBrush shadow=new SolidBrush(Color.FromArgb((int)(90*p),0,0,0)))g.FillRectangle(shadow,new RectangleF(drawerX-16*s,0,16*s,Height));
            ArtTheme t=ArtTheme.Current;
            using(GraphicsPath path=ReportDesign.Round(new RectangleF(drawerX,0,drawerW,Height),0))
            using(LinearGradientBrush fill=new LinearGradientBrush(new RectangleF(drawerX,0,drawerW,Math.Max(1,Height)),ArtTheme.Mix(t.Card,t.Text,.05),t.Background,90f))
                g.FillPath(fill,path);
            using(Pen edge=new Pen(Color.FromArgb((int)(60*p),t.Line)))g.DrawLine(edge,drawerX,0,drawerX,Height);
            float pad=24*s,titleY=24*s;
            using(SolidBrush ink=new SolidBrush(t.Text))g.DrawString(_title,_titleFont,ink,new RectangleF(drawerX+pad,titleY,drawerW-pad*2-28*s,34*s));
            using(SolidBrush muted=new SolidBrush(t.Muted))g.DrawString("统计口径与计算定义",_smallFont,muted,drawerX+pad,titleY+30*s);
            _closeRect=new RectangleF(drawerX+drawerW-44*s,20*s,28*s,28*s);
            using(SolidBrush closeBack=new SolidBrush(t.Raised))g.FillEllipse(closeBack,_closeRect);
            using(Pen closePen=new Pen(t.Muted,1.4f))
            {
                g.DrawLine(closePen,_closeRect.X+9*s,_closeRect.Y+9*s,_closeRect.Right-9*s,_closeRect.Bottom-9*s);
                g.DrawLine(closePen,_closeRect.Right-9*s,_closeRect.Y+9*s,_closeRect.X+9*s,_closeRect.Bottom-9*s);
            }
            using(Pen line=new Pen(t.Line))g.DrawLine(line,drawerX+pad,72*s,drawerX+drawerW-pad,72*s);
            float bodyTop=88*s,bodyBottom=Height-24*s,bodyX=drawerX+pad,bodyW=drawerW-pad*2;
            if(bodyW<40||bodyBottom<=bodyTop)return;
            float scopeH=0;
            if(_scope.Length>0)
            {
                using(SolidBrush accent=new SolidBrush(t.Cyan))g.DrawString(_scope,_smallFont,accent,new RectangleF(bodyX,bodyTop,bodyW,46*s));
                scopeH=34*s;
            }
            float noteTop=bodyTop+scopeH;
            using(StringFormat wrap=new StringFormat{Trimming=StringTrimming.Word,FormatFlags=StringFormatFlags.LineLimit})
            using(SolidBrush ink=new SolidBrush(t.Text))
            {
                SizeF measured=g.MeasureString(_note,_bodyFont,new SizeF(bodyW,10000),wrap);
                _contentHeight=measured.Height;
                float viewH=bodyBottom-noteTop;
                _scroll=Math.Max(0,Math.Min(_scroll,Math.Max(0,_contentHeight-viewH)));
                RectangleF body=new RectangleF(bodyX,noteTop,bodyW,viewH);
                GraphicsState state=g.Save();g.SetClip(body);
                g.DrawString(_note,_bodyFont,ink,new RectangleF(bodyX,noteTop-_scroll,bodyW,_contentHeight+20),wrap);
                g.Restore(state);
                if(_contentHeight>viewH)
                {
                    float track=body.Height-4*s,thumb=Math.Max(24*s,track*viewH/Math.Max(viewH,_contentHeight));
                    float thumbY=body.Y+(_scroll/Math.Max(1,_contentHeight-viewH))*(track-thumb);
                    using(SolidBrush bar=new SolidBrush(Color.FromArgb(40,t.Text)))g.FillRectangle(bar,drawerX+drawerW-10*s,body.Y,3*s,track);
                    using(SolidBrush thumbBrush=new SolidBrush(Color.FromArgb(120,t.Text)))g.FillRectangle(thumbBrush,drawerX+drawerW-10*s,thumbY,3*s,thumb);
                }
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if(_closeRect.Contains(e.Location)||e.X<_drawerRect.X)CloseHelp();
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _scroll-=e.Delta/120f*90*_scale;
            Invalidate();
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if(e.KeyCode==Keys.Escape){CloseHelp();e.Handled=true;e.SuppressKeyPress=true;}
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing){_timer.Dispose();_titleFont.Dispose();_bodyFont.Dispose();_smallFont.Dispose();}
            base.Dispose(disposing);
        }
    }

    /// <summary>报告头部的极简图标按钮:刷新会转一圈并闪一次绿点,复制只做悬停反馈。</summary>
    internal sealed class ReportIconButton : Control
    {
        private readonly int _glyph;private readonly ToolTip _tip=new ToolTip{InitialDelay=300,AutoPopDelay=12000};
        private readonly Timer _timer=new Timer{Interval=30};
        private bool _hover,_spinning,_pulsing;private DateTime _spinAt,_pulseAt;
        internal string Caption="";
        internal ReportIconButton(int glyph,string tip)
        {
            _glyph=glyph;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);
            BackColor=Color.Transparent;Cursor=Cursors.Hand;TabStop=false;
            _tip.SetToolTip(this,tip);
            _timer.Tick+=delegate
            {
                if((DateTime.Now-_pulseAt).TotalMilliseconds>2000){_spinning=false;_pulsing=false;_timer.Stop();}
                Invalidate();
            };
        }
        internal void Flash()
        {
            if(!Enabled)return;
            _spinning=true;_pulsing=true;_spinAt=DateTime.Now;_pulseAt=DateTime.Now;_timer.Start();Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;ArtTheme t=ArtTheme.Current;
            float w=Width-1,h=Height-1;
            if(_hover)using(GraphicsPath hover=ReportDesign.Round(new RectangleF(0,0,Width,Height),7))
                using(SolidBrush bg=new SolidBrush(Color.FromArgb(30,t.Text)))g.FillPath(bg,hover);
            Color ink=Enabled?(_hover?t.Accent:t.Text):t.Muted;
            float cx=Width/2f,cy=Height/2f;
            if(Caption.Length>0)
            {
                using(Font font=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel))
                using(SolidBrush text=new SolidBrush(ink))
                using(StringFormat center=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap})
                    g.DrawString(Caption,font,text,new RectangleF(0,0,Width,Height),center);
            }
            else if(_glyph==0)
            {
                float angle=0;
                if(_spinning){double p=Math.Min(1,(DateTime.Now-_spinAt).TotalMilliseconds/650.0);angle=(float)(360*(1-(1-p)*(1-p)*(1-p)));}
                using(Pen pen=new Pen(ink,1.9f))
                {
                    GraphicsState state=g.Save();g.TranslateTransform(cx,cy);g.RotateTransform(angle);
                    g.DrawArc(pen,new RectangleF(-6,-6,12,12),-70,280);
                    g.DrawLine(pen,4.6f,-5.2f,7.2f,-8.2f);g.DrawLine(pen,4.6f,-5.2f,8.2f,-3.2f);
                    g.Restore(state);
                }
            }
            else
            {
                using(Pen pen=new Pen(ink,1.4f))
                {
                    using(GraphicsPath back=ReportDesign.Round(new RectangleF(cx-7,cy-8,11,12),2))g.DrawPath(pen,back);
                    using(GraphicsPath front=ReportDesign.Round(new RectangleF(cx-3,cy-5,11,12),2))g.DrawPath(pen,front);
                }
            }
            if(_pulsing)
            {
                double fade=1-(DateTime.Now-_pulseAt).TotalMilliseconds/2000.0;
                if(fade>0)
                {
                    int alpha=(int)(220*Math.Min(1,fade*2));
                    using(SolidBrush dot=new SolidBrush(Color.FromArgb(Math.Max(0,alpha),t.Green)))g.FillEllipse(dot,Width-8,3,4.5f,4.5f);
                }
            }
        }
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);_hover=true;Invalidate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);_hover=false;Invalidate();}
        protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
        protected override void Dispose(bool disposing){if(disposing){_timer.Dispose();_tip.Dispose();}base.Dispose(disposing);}
    }

    internal sealed partial class StatisticsReport
    {
        private readonly DateTime _date;private DateTime _snapshot;
        private readonly ReportDeck _tabs=new ReportDeck();private readonly Timer _timer=new Timer{Interval=1000};
        private readonly ReportHeader _header=new ReportHeader();private readonly FlowLayoutPanel _navigation=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};
        private readonly Button _compact=new Button{FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleLeft,Cursor=Cursors.Hand};
        private readonly ContextMenuStrip _navMenu=new ContextMenuStrip();
        private readonly Button _export=new Button{Text="导出 ▾"},_panel=new Button{Text="在面板中查看 ↗"},_help=new Button{Text="说明"};
        private readonly ReportIconButton _copy=new ReportIconButton(1,"复制指标 (Ctrl+C)"),_refresh=new ReportIconButton(0,"刷新数据");
        private readonly ContextMenuStrip _exportMenu=new ContextMenuStrip();
        private readonly ToolTip _actionsTip=new ToolTip{InitialDelay=350,AutoPopDelay=12000};
        private readonly Label _feedback=new Label();private readonly List<Button> _navButtons=new List<Button>();
        private readonly ComboBox _range=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat};
        private readonly ComboBox _compare=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat};
        private DateTime _rangeStart,_rangeEnd,_compareStart,_compareEnd;private int _rangePreset,_comparePreset;
        private readonly DateTime _initialStart=DateTime.MinValue,_initialEnd=DateTime.MinValue;private bool _rangeInitialized;
        private string _compareLabel="紧随其前";
        private ListView _live;private float _scale=1;private bool _ready,_reportStyled;
        public StatisticsReport(DateTime date,int initialTab=0) : this(date,initialTab,DateTime.MinValue,DateTime.MinValue)
        {
        }
        /// <summary>带初始区间打开:面板跳转时把当前筛选一并带过来。</summary>
        internal StatisticsReport(DateTime date,int initialTab,DateTime rangeStart,DateTime rangeEnd)
        {
            _initialStart=rangeStart;_initialEnd=rangeEnd;
            _date=date.Date;Program.LogStall("统计报告 打开 tab=" + initialTab, 0);Font=new Font("Microsoft YaHei UI",10f);AutoScaleMode=AutoScaleMode.None;
            ClientSize=new Size(1140,800);MinimumSize=new Size(720,480);StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;KeyPreview=true;DoubleBuffered=true;
            _feedback.Visible=false;_feedback.TextChanged+=delegate{_feedback.Visible=!string.IsNullOrEmpty(_feedback.Text);};
            Controls.AddRange(new Control[]{_header,_navigation,_tabs,_compact,_export,_panel,_copy,_help,_refresh,_feedback});
            _header.Controls.AddRange(new Control[]{_feedback,_compact,_export,_panel,_copy,_help,_refresh,_range,_compare});
            _range.Items.AddRange(new object[]{"近 7 天","近 30 天","本月","上月","本季度","今年","自定义…"});
            _range.SelectedIndexChanged+=delegate{if(!_ready||_suppressRange)return;if(_range.SelectedIndex==6){ChooseRange();return;}_rangePreset=_range.SelectedIndex;RebuildRangePages();};
            _compare.Items.AddRange(new object[]{"对比：紧随其前","对比：去年同期","对比：自定义…"});
            _compare.SelectedIndexChanged+=delegate{if(!_ready||_suppressRange)return;if(_compare.SelectedIndex==2){ChooseCompareRange();return;}_comparePreset=_compare.SelectedIndex;RebuildRangePages();};
            _exportMenu.Items.Add(new ToolStripMenuItem("导出 CSV…",null,delegate{Export();}));
            _exportMenu.Items.Add(new ToolStripMenuItem("导出图片 PNG…",null,delegate{ExportImage();}));
            _exportMenu.Items.Add(new ToolStripMenuItem("导出网页 HTML…",null,delegate{ExportHtml();}));
            _navMenu.Renderer=new ToolStripProfessionalRenderer(new NikkiMenuColors());
            for(int i=0;i<ReportDesign.Names.Length;i++){int index=i;ToolStripMenuItem item=new ToolStripMenuItem(ReportDesign.Names[i]);item.Click+=delegate{_tabs.SelectedIndex=index;};_navMenu.Items.Add(item);}
            _compact.Click+=delegate{ArtTheme t=ArtTheme.Current;_navMenu.BackColor=t.Card;_navMenu.ForeColor=t.Text;for(int i=0;i<_navMenu.Items.Count;i++){ToolStripMenuItem item=(ToolStripMenuItem)_navMenu.Items[i];item.Checked=i==_tabs.SelectedIndex;item.ForeColor=t.Text;}_navMenu.Show(_compact,new Point(0,_compact.Height));};
            _tabs.SelectedIndexChanged+=delegate{UpdateSelection();};
            _export.Click+=delegate{ArtTheme t=ArtTheme.Current;_exportMenu.BackColor=t.Card;_exportMenu.ForeColor=t.Text;foreach(ToolStripItem item in _exportMenu.Items)item.ForeColor=t.Text;_exportMenu.Show(_export,new Point(0,_export.Height));};_copy.Click+=delegate{CopyMetric();};_help.Click+=delegate{ReportPage page=_tabs.SelectedTab;if(page!=null)using(ReportHelpOverlay help=new ReportHelpOverlay())help.ShowHelp(this,page.Text,ReportDesign.Scope[page.Index],BuildHelpText(page),_scale);};
            _refresh.Click+=delegate{int index=_tabs.SelectedIndex;_refresh.Flash();bool details=_tabs.SelectedTab.DetailsOpen;BuildPages();_tabs.SelectedIndex=Math.Min(ReportDesign.Names.Length-1,index);_tabs.SelectedTab.DetailsOpen=details;_tabs.PerformLayout();_feedback.Text="";};
            foreach(Button button in new[]{_export,_panel,_help}){button.FlatStyle=FlatStyle.Flat;button.Cursor=Cursors.Hand;}
            _panel.Visible=false;_panel.Click+=delegate{ShowInPanel();};
            _actionsTip.SetToolTip(_export,"导出当前页：CSV / PNG / HTML");
            _actionsTip.SetToolTip(_panel,"在详细统计面板中查看当前区间");
            _actionsTip.SetToolTip(_help,"查看当前页的统计口径与计算定义");
            BuildNavigation();BuildPages();_tabs.SelectedIndex=Math.Max(0,Math.Min(ReportDesign.Names.Length-1,initialTab));
            _suppressRange=true;
            try{_range.SelectedIndex=_initialStart!=DateTime.MinValue?6:0;_compare.SelectedIndex=0;}finally{_suppressRange=false;}
            UpdateSelection();
            _timer.Tick+=delegate{if(Visible&&WindowState!=FormWindowState.Minimized&&_tabs.SelectedTab!=null&&_tabs.SelectedTab.Kind==4){PopulateLiveRows();_tabs.SelectedTab.Chart.Invalidate();UpdateHeader();}};
            _timer.Start();_ready=true;
        }
        private void BuildPages()
        {
            _tabs.ClearPages();_snapshot=DateTime.Now;ResolveRange();
            PopulateRatios();PopulateLive();
            CrossReportData mouse=CrossReportData.Build(_date,8);ListView mouseList=PageCore(8,mouse.Title,mouse.Note,mouse,mouse.Headings);foreach(string[] fields in mouse.Rows)Row(mouseList,fields);
            DistributionReportData distribution=DistributionReportData.Build(_rangeStart,_rangeEnd);ListView distributionList=PageCore(13,ReportDesign.Names[3],DistributionReportData.Note,null,distribution.Headings);foreach(string[] fields in distribution.Rows)Row(distributionList,fields);
            HoldTrendData holdTrend=HoldTrendData.Build(_rangeStart,_rangeEnd);ListView holdTrendList=PageCore(16,ReportDesign.Names[4],HoldTrendData.Note,null,holdTrend.Headings);foreach(string[] fields in holdTrend.Rows)Row(holdTrendList,fields);
            if(IsHandleCreated)foreach(ReportPage page in _tabs.TabPages)Style(page);
        }
        /// <summary>把区间预设解析成完整日区间;终点不超过最近一个已结束的日。</summary>
        private void ResolveRange()
        {
            if(!_rangeInitialized&&_initialStart!=DateTime.MinValue)
            {
                _rangeInitialized=true;_rangeStart=_initialStart;_rangeEnd=_initialEnd;_rangePreset=6;
            }
            else _rangeInitialized=false;
            DateTime anchor=RangeRules.CompleteEnd(_date);
            if(_rangePreset!=6) switch(_rangePreset)
            {
                case 1:_rangeStart=anchor.AddDays(-29);_rangeEnd=anchor;break;
                case 2:_rangeStart=new DateTime(anchor.Year,anchor.Month,1);_rangeEnd=anchor;break;
                case 3:{DateTime previous=new DateTime(anchor.Year,anchor.Month,1).AddDays(-1);_rangeStart=new DateTime(previous.Year,previous.Month,1);_rangeEnd=previous;break;}
                case 4:{int month=((anchor.Month-1)/3)*3+1;_rangeStart=new DateTime(anchor.Year,month,1);_rangeEnd=anchor;break;}
                case 5:_rangeStart=new DateTime(anchor.Year,1,1);_rangeEnd=anchor;break;
                case 6:break;   // 自定义:沿用对话框设定的区间
                default:_rangeStart=anchor.AddDays(-6);_rangeEnd=anchor;break;
            }
            // 对比区间:紧随其前 / 去年同期(按 365 天前) / 自定义
            if(_comparePreset==1)
            {
                _compareStart=_rangeStart.AddDays(-365);
                _compareEnd=_rangeEnd.AddDays(-365);
            }
            else if(_comparePreset==0)
            {
                _compareEnd=_rangeStart.AddDays(-1);
                _compareStart=_compareEnd.AddDays(-((_rangeEnd-_rangeStart).Days));
            }
            _compareLabel=_comparePreset==1?"去年同期":_comparePreset==2?"自定义":"紧随其前";
        }
        /// <summary>图上拖拽选择子区间:直接切换为自定义区间。</summary>
        private void SetCustomRange(DateTime start,DateTime end)
        {
            if(end<start){DateTime swap=start;start=end;end=swap;}
            _rangeStart=start;_rangeEnd=end;_rangePreset=6;
            _suppressRange=true;
            try{_range.SelectedIndex=6;}finally{_suppressRange=false;}
            RebuildRangePages();
        }
        /// <summary>回到面板查看同一天:报告由面板的 ShowDialog 打开时可用。</summary>
        private void ShowInPanel()
        {
            Dashboard dashboard=Owner as Dashboard;
            if(dashboard==null)return;
            dashboard.FocusFromReport(_date);
            Close();
        }
        private void ChooseCompareRange()
        {
            DateTime start=_compareStart,end=_compareEnd;
            using(RangePickerDialog dialog=new RangePickerDialog(start,end))
            {
                if(dialog.ShowDialog(this)!=DialogResult.OK)
                {
                    _suppressRange=true;
                    try{_compare.SelectedIndex=_comparePreset;}finally{_suppressRange=false;}
                    return;
                }
                start=dialog.Start;end=dialog.End;
            }
            if(end<start){DateTime swap=start;start=end;end=swap;}
            _compareStart=start;_compareEnd=end;_comparePreset=2;
            RebuildRangePages();
        }
        private bool _suppressRange;
        private void ChooseRange()
        {
            DateTime start=_rangeStart,end=_rangeEnd;
            using(RangePickerDialog dialog=new RangePickerDialog(start,end))
            {
                if(dialog.ShowDialog(this)!=DialogResult.OK)
                {
                    _suppressRange=true;
                    try{_range.SelectedIndex=_rangePreset;}finally{_suppressRange=false;}
                    return;
                }
                start=dialog.Start;end=dialog.End;
            }
            if(end<start){DateTime swap=start;start=end;end=swap;}
            _rangeStart=start;_rangeEnd=end;_rangePreset=6;
            RebuildRangePages();
        }
        private void RebuildRangePages()
        {
            int index=_tabs.SelectedIndex;bool details=false;
            if(_tabs.SelectedTab!=null){details=_tabs.SelectedTab.DetailsOpen;}
            BuildPages();
            _tabs.SelectedIndex=Math.Max(0,Math.Min(ReportDesign.Names.Length-1,index));
            if(_tabs.SelectedTab!=null){_tabs.SelectedTab.DetailsOpen=details;_tabs.PerformLayout();}
            UpdateHeader();
        }
        private void BuildNavigation()
        {
            string[] groups={"时间与节奏","输入与形态"};int[][] ids={new[]{0,1},new[]{2,3,4}};
            for(int group=0;group<groups.Length;group++)
            {
                _navigation.Controls.Add(new Label{Text=groups[group],Tag="group",TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(0,14,0,3)});
                foreach(int id in ids[group]){int page=id;Button button=new Button{Text=ReportDesign.Names[id],Tag=id,TextAlign=ContentAlignment.MiddleLeft,FlatStyle=FlatStyle.Flat,Margin=new Padding(0,2,0,2),Cursor=Cursors.Hand};button.Click+=delegate{_tabs.SelectedIndex=page;};_navigation.Controls.Add(button);_navButtons.Add(button);}
            }
        }
        private void UpdateHeader()
        {
            _refresh.Caption="";_refresh.Invalidate();_header.Heading=ReportDesign.Names[Math.Max(0,_tabs.SelectedIndex)];_header.Date=(_tabs.SelectedTab!=null&&_tabs.SelectedTab.Kind==4?DateTime.Today:_date).ToString("yyyy.MM.dd  dddd");
            _header.Status=_tabs.SelectedTab!=null&&_tabs.SelectedTab.Kind==4?"实时更新 · 使用状态："+ActivityMonitor.Status:_date==DateTime.Today?"今日尚未结束 · 数据截至 "+_snapshot.ToString("HH:mm:ss")+"，刷新以更新":"历史记录 · 未采集时段不补零";
            _header.Invalidate();
        }
        private void UpdateSelection()
        {
            _compact.Text=ReportDesign.Names[Math.Max(0,_tabs.SelectedIndex)]+"   ▾";_copy.Enabled=true;_feedback.Text="";
            ArtTheme t=ArtTheme.Current;
            foreach(Button b in _navButtons){bool selected=(int)b.Tag==_tabs.SelectedIndex;b.BackColor=selected?ArtTheme.Mix(t.Card,t.Accent,.18):t.Background;b.ForeColor=selected?t.Accent:t.Muted;b.FlatAppearance.BorderSize=selected?1:0;b.FlatAppearance.BorderColor=t.Line;}
            // 区间选择器只在用到区间的两页显示;对比选择器只在区间回顾显示;窄窗口没有空间放置。
            _panel.Visible=Owner is Dashboard;
            _range.Visible=_tabs.SelectedTab!=null&&UsesRange(_tabs.SelectedTab.Kind)&&ClientSize.Width/_scale>=1000;
            _compare.Visible=false;
            UpdateHeader();
        }
        private static bool UsesRange(int kind){return kind==13||kind==16;}
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);using(Graphics g=CreateGraphics())_scale=g.DpiX/96f;
            Rectangle work=Screen.FromControl(this).WorkingArea;MinimumSize=new Size(Math.Min((int)(720*_scale),work.Width-24),Math.Min((int)(480*_scale),work.Height-24));
            Size=new Size(Math.Min((int)(1140*_scale),work.Width-40),Math.Min((int)(830*_scale),work.Height-40));LayoutReport();UpdateSelection();
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);if(!_reportStyled&&!ThemeArt.Active){Style(this);_reportStyled=true;}}
        protected override void OnResize(EventArgs e){base.OnResize(e);if(_ready)LayoutReport();}
        internal void LayoutReport()
        {
            float s=_scale;bool compact=ClientSize.Width/s<1000;int header=(int)((compact?142:106)*s);ArtTheme t=ArtTheme.Current;BackColor=t.Background;
            _header.UiScale=s;_header.SetBounds(0,0,ClientSize.Width,header);
            Control[] actions={_export,_panel,_copy,_help,_refresh};int[] widths={92,126,36,52,40};int right=ClientSize.Width-(int)(24*s);
            for(int i=0;i<actions.Length;i++){int w=(int)(widths[i]*s);right-=w;actions[i].SetBounds(right,(int)(25*s),w,(int)(33*s));right-=(int)(8*s);}
            _export.ForeColor=t.OnAccent;_export.BackColor=ArtTheme.Mix(t.Accent,Color.Black,.18f);_export.FlatAppearance.BorderColor=ArtTheme.Mix(t.Accent,Color.Black,.25f);
            _panel.ForeColor=t.Text;_panel.BackColor=Color.Transparent;_panel.FlatAppearance.BorderSize=1;_panel.FlatAppearance.BorderColor=Color.FromArgb(46,t.Text);
            _help.ForeColor=t.Muted;_help.BackColor=Color.Transparent;_help.FlatAppearance.BorderSize=0;
            _range.BackColor=t.Card;_range.ForeColor=t.Text;_range.SetBounds((int)(302*s),(int)(26*s),(int)(168*s),(int)(28*s));
            _compare.BackColor=t.Card;_compare.ForeColor=t.Text;_compare.SetBounds((int)(478*s),(int)(26*s),(int)(168*s),(int)(28*s));
            _navigation.Visible=!compact;_navigation.BackColor=(GujianArt.Active||OriArt.Active)?ArtTheme.Mix(t.Background,t.Cyan,.04):t.Background;int nav=compact?0:(int)(178*s);
            _navigation.SetBounds((int)(20*s),header,Math.Max(1,nav-(int)(20*s)),ClientSize.Height-header-(int)(12*s));
            foreach(Control c in _navigation.Controls){c.Width=(int)(146*s);c.Height=(int)((c is Button?34:24)*s);if(c is Label){c.ForeColor=t.Muted;c.BackColor=t.Background;}}
            _compact.BringToFront();_compact.Visible=compact;_compact.SetBounds((int)(24*s),(int)(99*s),(int)(190*s),(int)(30*s));_compact.BackColor=t.Card;_compact.ForeColor=t.Text;_compact.FlatAppearance.BorderColor=t.Line;
            _feedback.SetBounds(compact?(int)(230*s):(int)(590*s),compact?(int)(105*s):(int)(86*s),(int)(240*s),(int)(23*s));_feedback.ForeColor=t.Green;_feedback.BackColor=Color.Transparent;
            _tabs.UiScale=s;_tabs.BackColor=t.Background;_tabs.SetBounds(nav+(int)(24*s),header+(int)(16*s),ClientSize.Width-nav-(int)(48*s),Math.Max(1,ClientSize.Height-header-(int)(28*s)));_tabs.PerformLayout();
        }
        private ListView Page(int kind,string title,string note,params string[] headings){return PageCore(kind,title,note,null,headings);}
        private ListView PageCore(int kind,string title,string note,CrossReportData snapshot,string[] headings)
        {
            int index=_tabs.TabPages.Count;ReportPage page=new ReportPage{Text=title,Note=note,Index=index,Kind=kind};_tabs.AddPage(page);
            ReportList list=new ReportList{View=View.Details,FullRowSelect=true,HideSelection=false,ShowItemToolTips=true,BorderStyle=BorderStyle.None};
            foreach(string heading in headings)list.Columns.Add(heading,180);
            Control chart;
            switch(kind)
            {
                case 4:chart=new ReportVisual(_date,4);break;
                case 5:case 6:case 7:case 8:chart=new CrossReportVisual(_date,kind,snapshot);break;
                case 13:chart=new DistributionReportVisual(_rangeStart,_rangeEnd);break;
                case 16:chart=new HoldTrendVisual(_rangeStart,_rangeEnd);break;
                default:chart=new ReportVisual(_date,kind);break;
            }
            RangeReportVisual brush=chart as RangeReportVisual;if(brush!=null)brush.RangeSelected+=delegate(DateTime start,DateTime end){SetCustomRange(start,end);};
            page.List=list;page.Chart=chart;page.Controls.Add(list);page.Controls.Add(chart);
            ReportVisual visual=chart as ReportVisual;if(visual!=null)visual.MetricSelected+=delegate{list.SelectedItems.Clear();_copy.Enabled=true;_feedback.Text="已选中指标 · Ctrl+C 复制";};
            list.SelectedIndexChanged+=delegate{_copy.Enabled=true;};
            list.SizeChanged+=delegate{int space=Math.Max(200,list.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-4);int first=(int)(space*.29);list.Columns[0].Width=first;for(int i=1;i<list.Columns.Count;i++)list.Columns[i].Width=(space-first)/(list.Columns.Count-1);};
            return list;
        }
        private string BuildHelpText(ReportPage page)
        {
            string text=page.Note??"";
            if(page.Kind==0)
            {
                StringBuilder body=new StringBuilder(text);
                body.AppendLine();body.AppendLine();
                body.AppendLine("个人基准指标");
                for(int i=0;i<ReportDesign.Metrics.Length;i++)
                    body.AppendLine("• "+ReportDesign.Metrics[i]+"（"+ReportDesign.Units[i]+"）："+ReportDesign.Definitions[i]);
                body.AppendLine();
                body.AppendLine("刻度与颜色");
                body.AppendLine("• 灰线 = P50（通常），表示过去 30 天有效日的中位数。");
                body.AppendLine("• 金线 = P90（较高），表示 90% 的历史日不超过它。");
                body.AppendLine("• 当前值超过 P50 后色条转为青色，超过 P90 转为金色；颜色只描述相对区间，不是评分。");
                body.AppendLine("• 六项指标单位不同，色条各自独立缩放，长度不能跨指标横向比较。");
                text=body.ToString();
            }
            return text;
        }
        private void CopyMetric()
        {
            ReportPage page=_tabs.SelectedTab;if(page==null)return;string text=null;
            if(page.List.SelectedItems.Count>0){ListViewItem item=page.List.SelectedItems[0];string[] raw=item.Tag as string[];text=string.Join("\t",raw??new[]{item.Text});}
            else {ReportVisual chart=page.Chart as ReportVisual;if(chart!=null)text=chart.SelectedMetricText;}
            if(string.IsNullOrEmpty(text)){_feedback.Text="请先选择指标卡或明细行";return;}
            try{Clipboard.SetText(text);_feedback.Text="指标已复制";}catch(System.Runtime.InteropServices.ExternalException){_feedback.Text="剪贴板忙，请重试";}
        }
        protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.Control&&e.KeyCode==Keys.C){CopyMetric();e.Handled=true;e.SuppressKeyPress=true;}if(e.KeyCode==Keys.Escape)Close();}
        protected override void Dispose(bool disposing){if(disposing){Program.LogStall("统计报告 关闭", 0);_timer.Dispose();_navMenu.Dispose();_exportMenu.Dispose();_actionsTip.Dispose();}base.Dispose(disposing);}
    }
}
