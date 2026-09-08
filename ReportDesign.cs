using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal static class ReportDesign
    {
        public static readonly string[] Names={"输入习惯","每日趋势","应用时间","使用节奏","实时状态","应用时段","应用操作","会话应用","鼠标动作","键位分布","快捷键节省"};
        public static readonly string[] Scope={"操作强度描述使用方式，不代表工作效率。","今日只展示已累计部分；缺失日期不补零。","按活跃观测归因，无法识别的时间单独保留。","连续使用段包含空闲阈值内的短暂停顿。","APM = 最近 60 秒击键与点击；下图观察最近 5 分钟。","只展示实际观测到的应用时间，不从旧汇总反推。","比较各应用的输入构成，不将滚轮次数视为距离。","每一段独立分析，不按全天占比分摊应用时间。","方向和快速移动是行为描述，不推断左右手或操作效果。","按默认键位分组，不代表这些按键用于游戏。","情景估算，不是实际测得的省时；点击「模型」调整假设。"};
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
    }
    internal sealed class ReportPage : Panel
    {
        internal Control Chart;internal ReportList List;internal int Index;
        internal string Note;internal bool DetailsOpen,HelpOpen;
        private readonly Label scope=new Label(),help=new Label();
        private readonly Button details=new Button();
        internal event EventHandler SizeRequested;
        public ReportPage()
        {
            DoubleBuffered=true;
            Controls.Add(scope);Controls.Add(help);Controls.Add(details);
            details.FlatStyle=FlatStyle.Flat;details.TextAlign=ContentAlignment.MiddleLeft;
            details.Click+=delegate{DetailsOpen=!DetailsOpen;if(SizeRequested!=null)SizeRequested(this,EventArgs.Empty);};
        }
        internal int Arrange(int width,float scale)
        {
            ArtTheme t=ArtTheme.Current;BackColor=t.Background;if(List!=null)List.SetScale(scale);
            int gap=(int)(14*scale),y=0;
            scope.AutoEllipsis=true;scope.Text=ReportDesign.Scope[Index];scope.ForeColor=t.Muted;scope.BackColor=t.Background;
            scope.SetBounds(0,0,width,(int)(36*scale));y+=scope.Height;
            help.Visible=HelpOpen;
            if(HelpOpen)
            {
                help.Text=Note;help.Padding=new Padding((int)(14*scale));help.BackColor=t.Raised;help.ForeColor=t.Text;
                int h=TextRenderer.MeasureText(Note,Font,new Size(Math.Max(80,width-28*(int)Math.Ceiling(scale)),0),TextFormatFlags.WordBreak).Height+(int)(30*scale);
                help.SetBounds(0,y,width,h);y+=h+gap;
            }
            ReportVisual visual=Chart as ReportVisual;if(visual!=null)visual.UiScale=scale;
            CrossReportVisual cross=Chart as CrossReportVisual;if(cross!=null)cross.UiScale=scale;
            int chartHeight=(int)(scale*(Index==0?ReportVisual.HabitHeight(width/scale):Index==7?325:300));
            Chart.SetBounds(0,y,width,chartHeight);y+=chartHeight+gap;
            details.Text=(DetailsOpen?"▾ 收起明细":"▸ 查看明细")+"   ·   "+List.Items.Count+" 项";
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
        public ReportPage SelectedTab {get{return selected>=0&&selected<TabPages.Count?TabPages[selected]:null;}}
        public int SelectedIndex {get{return selected;}set{if(value<0||value>=TabPages.Count)return;if(SelectedTab!=null)SelectedTab.Visible=false;selected=value;SelectedTab.Visible=true;AutoScrollPosition=Point.Empty;PerformLayout();if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}}
        public void AddPage(ReportPage page){TabPages.Add(page);page.Visible=false;Controls.Add(page);page.SizeRequested+=delegate{PerformLayout();};}
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
            using(LinearGradientBrush bg=new LinearGradientBrush(new RectangleF(0,0,Math.Max(1,width),120),t.Card,t.Background,0f))g.FillRectangle(bg,0,0,width,Height/UiScale);
            using(Font title=new Font("Microsoft YaHei UI",25,FontStyle.Bold,GraphicsUnit.Pixel))
            using(Font body=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel))
            {
                ReportDesign.Text(g,Heading,title,t.Text,new RectangleF(24,19,270,39));
                if(EuroTruckArt.Active)EuroTruckArt.Symbol(g,6,new RectangleF(264,15,42,42));
                else if(WuxiaArt.Active)WuxiaArt.Symbol(g,0,new RectangleF(264,15,42,42));
                else if(BalatroArt.Active)BalatroArt.Symbol(g,8,new RectangleF(264,19,34,34));
                ReportDesign.Text(g,Date+"   ·   "+Status,body,t.Muted,new RectangleF(24,64,width-48,23));
            }
            using(Pen line=new Pen(t.Line))g.DrawLine(line,24,Height/UiScale-1,width-24,Height/UiScale-1);
        }
    }
    internal sealed partial class StatisticsReport
    {
        private readonly DateTime _date;private DateTime _snapshot;
        private readonly ReportDeck _tabs=new ReportDeck();private readonly Timer _timer=new Timer{Interval=1000};
        private readonly ReportHeader _header=new ReportHeader();private readonly FlowLayoutPanel _navigation=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};
        private readonly Button _compact=new Button{FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleLeft,Cursor=Cursors.Hand};
        private readonly ContextMenuStrip _navMenu=new ContextMenuStrip();
        private readonly Button _export=new Button{Text="导出 CSV"},_copy=new Button{Text="复制指标"},_help=new Button{Text="说明"},_refresh=new Button{Text="刷新"};
        private readonly Label _feedback=new Label();private readonly List<Button> _navButtons=new List<Button>();
        private ListView _live;private float _scale=1;private bool _ready,_reportStyled;
        public StatisticsReport(DateTime date,int initialTab=0)
        {
            _date=date.Date;Text="统计报告";Font=new Font("Microsoft YaHei UI",10f);AutoScaleMode=AutoScaleMode.None;
            ClientSize=new Size(1140,800);MinimumSize=new Size(720,480);StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;KeyPreview=true;DoubleBuffered=true;
            _feedback.Visible=false;_feedback.TextChanged+=delegate{_feedback.Visible=!string.IsNullOrEmpty(_feedback.Text);};
            Controls.AddRange(new Control[]{_header,_navigation,_tabs,_compact,_export,_copy,_help,_refresh,_feedback});
            _header.Controls.AddRange(new Control[]{_feedback,_compact,_export,_copy,_help,_refresh});
            _navMenu.Renderer=new ToolStripProfessionalRenderer(new NikkiMenuColors());
            for(int i=0;i<ReportDesign.Names.Length;i++){int index=i;ToolStripMenuItem item=new ToolStripMenuItem(ReportDesign.Names[i]);item.Click+=delegate{_tabs.SelectedIndex=index;};_navMenu.Items.Add(item);}
            _compact.Click+=delegate{ArtTheme t=ArtTheme.Current;_navMenu.BackColor=t.Card;_navMenu.ForeColor=t.Text;for(int i=0;i<_navMenu.Items.Count;i++){ToolStripMenuItem item=(ToolStripMenuItem)_navMenu.Items[i];item.Checked=i==_tabs.SelectedIndex;item.ForeColor=t.Text;}_navMenu.Show(_compact,new Point(0,_compact.Height));};
            _tabs.SelectedIndexChanged+=delegate{UpdateSelection();};
            _export.Click+=delegate{Export();};_copy.Click+=delegate{CopyMetric();};_help.Click+=delegate{if(_tabs.SelectedTab!=null){_tabs.SelectedTab.HelpOpen=!_tabs.SelectedTab.HelpOpen;_tabs.PerformLayout();}};
            _refresh.Click+=delegate{int index=_tabs.SelectedIndex;if(index==10)using(ShortcutSavingsSettings settings=new ShortcutSavingsSettings()){if(settings.ShowDialog(this)!=DialogResult.OK)return;}bool details=_tabs.SelectedTab.DetailsOpen,help=_tabs.SelectedTab.HelpOpen;BuildPages();_tabs.SelectedIndex=index;_tabs.SelectedTab.DetailsOpen=details;_tabs.SelectedTab.HelpOpen=help;_tabs.PerformLayout();_feedback.Text="已刷新";};
            foreach(Button button in new[]{_export,_copy,_help,_refresh}){button.FlatStyle=FlatStyle.Flat;button.Cursor=Cursors.Hand;}
            BuildNavigation();BuildPages();_tabs.SelectedIndex=Math.Max(0,Math.Min(10,initialTab));
            _timer.Tick+=delegate{if(Visible&&WindowState!=FormWindowState.Minimized&&_tabs.SelectedIndex==4){PopulateLiveRows();_tabs.SelectedTab.Chart.Invalidate();UpdateHeader();}};
            _timer.Start();_ready=true;
        }
        private void BuildPages()
        {
            _tabs.ClearPages();_snapshot=DateTime.Now;
            PopulateRatios();PopulateChanges();PopulateApps();PopulateSessions();PopulateLive();
            for(int index=5;index<=10;index++){CrossReportData data=CrossReportData.Build(_date,index);ListView list=PageCore(data.Title,data.Note,data,data.Headings);foreach(string[] fields in data.Rows)Row(list,fields);}
            if(IsHandleCreated)foreach(ReportPage page in _tabs.TabPages)Style(page);
        }
        private void BuildNavigation()
        {
            string[] groups={"时间与节奏","应用使用","输入方式"};int[][] ids={new[]{0,1,3,4},new[]{2,5,6,7},new[]{8,9,10}};
            for(int group=0;group<groups.Length;group++)
            {
                _navigation.Controls.Add(new Label{Text=groups[group],Tag="group",TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(0,14,0,3)});
                foreach(int id in ids[group]){int page=id;Button button=new Button{Text=ReportDesign.Names[id],Tag=id,TextAlign=ContentAlignment.MiddleLeft,FlatStyle=FlatStyle.Flat,Margin=new Padding(0,2,0,2),Cursor=Cursors.Hand};button.Click+=delegate{_tabs.SelectedIndex=page;};_navigation.Controls.Add(button);_navButtons.Add(button);}
            }
        }
        private void UpdateHeader()
        {
            _refresh.Text=_tabs.SelectedIndex==10?"模型":"刷新";_header.Heading=ReportDesign.Names[Math.Max(0,_tabs.SelectedIndex)];_header.Date=(_tabs.SelectedIndex==4?DateTime.Today:_date).ToString("yyyy.MM.dd  dddd");
            _header.Status=_tabs.SelectedIndex==4?"实时更新 · 使用状态："+ActivityMonitor.Status:_date==DateTime.Today?"今日尚未结束 · 数据截至 "+_snapshot.ToString("HH:mm:ss")+"，刷新以更新":"历史记录 · 未采集时段不补零";
            _header.Invalidate();
        }
        private void UpdateSelection()
        {
            _compact.Text=ReportDesign.Names[Math.Max(0,_tabs.SelectedIndex)]+"   ▾";_copy.Enabled=true;_feedback.Text="";
            ArtTheme t=ArtTheme.Current;
            foreach(Button b in _navButtons){bool selected=(int)b.Tag==_tabs.SelectedIndex;b.BackColor=selected?ArtTheme.Mix(t.Card,t.Accent,.18):t.Background;b.ForeColor=selected?t.Accent:t.Muted;b.FlatAppearance.BorderSize=selected?1:0;b.FlatAppearance.BorderColor=t.Line;}
            UpdateHeader();
        }
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
            Button[] actions={_export,_copy,_help,_refresh};int[] widths={102,96,64,64};int right=ClientSize.Width-(int)(24*s);
            for(int i=0;i<actions.Length;i++){int w=(int)(widths[i]*s);right-=w;actions[i].SetBounds(right,(int)(25*s),w,(int)(33*s));right-=(int)(8*s);actions[i].ForeColor=i==0?t.OnAccent:t.Text;actions[i].BackColor=i==0?t.Accent:t.Card;actions[i].FlatAppearance.BorderColor=t.Line;}
            _navigation.Visible=!compact;_navigation.BackColor=t.Background;int nav=compact?0:(int)(178*s);
            _navigation.SetBounds((int)(20*s),header,Math.Max(1,nav-(int)(20*s)),ClientSize.Height-header-(int)(12*s));
            foreach(Control c in _navigation.Controls){c.Width=(int)(146*s);c.Height=(int)((c is Button?34:24)*s);if(c is Label){c.ForeColor=t.Muted;c.BackColor=t.Background;}}
            _compact.BringToFront();_compact.Visible=compact;_compact.SetBounds((int)(24*s),(int)(99*s),(int)(190*s),(int)(30*s));_compact.BackColor=t.Card;_compact.ForeColor=t.Text;_compact.FlatAppearance.BorderColor=t.Line;
            _feedback.SetBounds(compact?(int)(230*s):(int)(590*s),compact?(int)(105*s):(int)(86*s),(int)(240*s),(int)(23*s));_feedback.ForeColor=t.Green;_feedback.BackColor=Color.Transparent;
            _tabs.UiScale=s;_tabs.BackColor=t.Background;_tabs.SetBounds(nav+(int)(24*s),header+(int)(16*s),ClientSize.Width-nav-(int)(48*s),Math.Max(1,ClientSize.Height-header-(int)(28*s)));_tabs.PerformLayout();
        }
        private ListView Page(string title,string note,params string[] headings){return PageCore(title,note,null,headings);}
        private ListView PageCore(string title,string note,CrossReportData snapshot,string[] headings)
        {
            int index=_tabs.TabPages.Count;ReportPage page=new ReportPage{Text=title,Note=note,Index=index};_tabs.AddPage(page);
            ReportList list=new ReportList{View=View.Details,FullRowSelect=true,HideSelection=false,ShowItemToolTips=true,BorderStyle=BorderStyle.None};
            foreach(string heading in headings)list.Columns.Add(heading,180);
            Control chart=index<5?(Control)new ReportVisual(_date,index):new CrossReportVisual(_date,index,snapshot);
            page.List=list;page.Chart=chart;page.Controls.Add(list);page.Controls.Add(chart);
            ReportVisual visual=chart as ReportVisual;if(visual!=null)visual.MetricSelected+=delegate{list.SelectedItems.Clear();_copy.Enabled=true;_feedback.Text="已选中指标 · Ctrl+C 复制";};
            list.SelectedIndexChanged+=delegate{_copy.Enabled=true;};
            list.SizeChanged+=delegate{int space=Math.Max(200,list.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-4);int first=(int)(space*.29);list.Columns[0].Width=first;for(int i=1;i<list.Columns.Count;i++)list.Columns[i].Width=(space-first)/(list.Columns.Count-1);};
            return list;
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
        protected override void Dispose(bool disposing){if(disposing){_timer.Dispose();_navMenu.Dispose();}base.Dispose(disposing);}
    }
}
