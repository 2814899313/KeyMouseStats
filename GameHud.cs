using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal sealed class GameHud : Form
    {
        private readonly Timer _refresh=new Timer{Interval=500};
        private readonly Font _label=new Font("Microsoft YaHei UI",10,FontStyle.Regular,GraphicsUnit.Pixel);
        private readonly Font _value=new Font("Segoe UI",21,FontStyle.Bold,GraphicsUnit.Pixel);
        private bool _editing;
        private Point _dragStart,_windowStart;
        public bool Editing { get { return _editing; } set { _editing=value;Capture=false;RecreateHandle();Invalidate(); } }
        public GameHud()
        {
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
            StartPosition=FormStartPosition.Manual;ClientSize=new Size(330,92);
            BackColor=Color.FromArgb(12,18,24);Opacity=.8;DoubleBuffered=true;
            Place(false);
            _refresh.Tick+=delegate{Invalidate();};
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams p=base.CreateParams;p.ExStyle|=0x08000000|0x00080000|0x00000080;
                if(!_editing)p.ExStyle|=0x20;return p; }
        }
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x21){m.Result=new IntPtr(3);return;} // MA_NOACTIVATE, including edit mode.
            base.WndProc(ref m);
        }
        public void Place(bool right)
        {
            Rectangle area=Screen.FromPoint(Cursor.Position).WorkingArea;
            Location=new Point(right?area.Right-Width-20:area.Left+20,area.Top+20);
        }
        protected override void OnVisibleChanged(EventArgs e)
        { base.OnVisibleChanged(e);if(Visible)_refresh.Start();else _refresh.Stop(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {base.OnMouseDown(e);if(_editing && e.Button==MouseButtons.Left){_dragStart=Cursor.Position;_windowStart=Location;Capture=true;}}
        protected override void OnMouseMove(MouseEventArgs e)
        {base.OnMouseMove(e);if(_editing && Capture){Point p=Cursor.Position;Location=new Point(_windowStart.X+p.X-_dragStart.X,_windowStart.Y+p.Y-_dragStart.Y);}}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);Capture=false;}
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            Color accent=ArtTheme.Current.Cyan;
            using(Pen edge=new Pen(_editing?Color.Gold:accent))g.DrawRectangle(edge,0,0,Width-1,Height-1);
            ActiveSession session=ActivityMonitor.CurrentSession;
            string[] labels={"实时 APM","今日击键","本段时长"};
            string[] values={Analysis.FmtCount(LiveRate.Apm),Analysis.FmtCount(Store.Today.Keys),session==null?"--":TimeSpan.FromSeconds(session.Seconds).ToString(@"hh\:mm\:ss")};
            using(SolidBrush muted=new SolidBrush(Color.FromArgb(170,191,199)))using(SolidBrush white=new SolidBrush(Color.White))
            {
                for(int i=0;i<3;i++){float x=12+i*108;g.DrawString(labels[i],_label,muted,x,9);g.DrawString(values[i],i==2?_label:_value,white,x,i==2?38:29);}
                g.DrawString(_editing?"拖动调整位置 · 在托盘菜单关闭调整模式":"HUD · 鼠标穿透 · 最近 5 分钟 APM",_label,muted,12,73);
            }
            if(!_editing)LiveRate.PaintTrend(g,new RectangleF(12,59,306,10),accent);
        }
        protected override void Dispose(bool disposing)
        {if(disposing){_refresh.Dispose();_label.Dispose();_value.Dispose();}base.Dispose(disposing);}
    }
}
