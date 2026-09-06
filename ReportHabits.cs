using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
namespace KeyMouseStats
{
    internal sealed partial class ReportVisual
    {
        internal float UiScale;
        internal event EventHandler MetricSelected;
        private readonly ToolTip metricTip=new ToolTip{InitialDelay=250,ReshowDelay=100,AutoPopDelay=18000};
        private readonly RectangleF[] metricBounds=new RectangleF[6];
        private int metricHover=-1,metricSelection=-1;
        internal string SelectedMetricText {get{return metricSelection<0?null:ReportDesign.Metrics[metricSelection]+"\t"+PersonalStats.Number(values[metricSelection]*(metricSelection==3?100:1),"R")+" "+ReportDesign.Units[metricSelection];}}
        internal static int HabitHeight(float width){return width>=780?496:636;}
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);if(page!=0)return;float scale=UiScale>0?UiScale:DeviceDpiScale();int next=-1;
            for(int i=0;i<6;i++)if(metricBounds[i].Contains(e.X/scale,e.Y/scale)){next=i;break;}
            if(next==metricHover)return;metricHover=next;Cursor=next<0?Cursors.Default:Cursors.Hand;
            metricTip.SetToolTip(this,next<0?"":ReportDesign.Definitions[next]+"\n"+(Valid(medians[next])?"个人参考来自之前 30 天中的有效记录。":"每项满 7 个有效历史日后显示个人参考。")+"\n点击选中后，Ctrl+C 复制完整数值。");Invalidate();
        }
        private float DeviceDpiScale(){using(Graphics g=CreateGraphics())return g.DpiX/96f;}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);if(metricHover>=0){metricHover=-1;Invalidate();}}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(page==0&&e.Button==MouseButtons.Left&&metricHover>=0){metricSelection=metricHover;Focus();Invalidate();if(MetricSelected!=null)MetricSelected(this,EventArgs.Empty);}}
        protected override void Dispose(bool disposing){if(disposing)metricTip.Dispose();base.Dispose(disposing);}
        private void PaintHabits(Graphics g,float width)
        {
            ArtTheme t=ArtTheme.Current;int columns=width>=780?3:2;float gap=12,pad=2,summary=(width-2*pad-2*gap)/3;
            using(Font label=new Font("Microsoft YaHei UI",13,FontStyle.Regular,GraphicsUnit.Pixel))
            using(Font small=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel))
            using(Font number=new Font("Segoe UI",30,FontStyle.Bold,GraphicsUnit.Pixel))
            using(Font title=new Font("Microsoft YaHei UI",14,FontStyle.Bold,GraphicsUnit.Pixel))
            {
                string[] labels={"键盘击键","鼠标点击","组合键占比"};string[] data={hasDay?Analysis.FmtCount((long)keys):"—",hasDay?Analysis.FmtCount((long)clicks):"—",keys>0?ReportDesign.Number(combos*100/keys)+"%":"—"};
                for(int i=0;i<3;i++){float x=pad+i*(summary+gap);ReportDesign.Surface(g,new RectangleF(x,2,summary,86),t.Card,t.Line);TextAt(g,labels[i],label,t.Muted,x+16,14,summary-32);TextAt(g,data[i],number,t.Text,x+16,37,summary-32);}
                bool available=false;foreach(double value in medians)available|=Valid(value);
                ReportDesign.Surface(g,new RectangleF(pad,102,width-2*pad,90),ArtTheme.Mix(t.Background,t.Accent,.07),t.Line);
                TextAt(g,available?"与你自己的使用习惯比较":"个人基准正在建立",title,t.Text,18,114,width-36);
                TextAt(g,available?"通常 = 中位数 P50；较高 = P90，90% 的历史日不超过它。":"近 30 天已有 "+observed+" 个记录日；每项满 7 个有效日后显示对比。",small,t.Muted,18,142,width-36);
                TextAt(g,available?"色条：当前数值  ·  竖线：通常水平；各指标独立尺度。":"缺失日期不计入基准；没有点击或活跃时间的指标不参与比较。",small,t.Muted,18,164,width-36);
                float cell=(width-2*pad-(columns-1)*gap)/columns;
                string[] hints={"键盘与鼠标的使用比例","活跃时间内的输入强度","活跃时间内的点击强度","击键与点击合计为操作次数","事件次数，不是滚动距离","组合动作与击键存在重叠"};
                for(int i=0;i<6;i++)
                {
                    float x=pad+(i%columns)*(cell+gap),y=208+(i/columns)*140;RectangleF r=new RectangleF(x,y,cell,128);metricBounds[i]=r;
                    ReportDesign.Surface(g,r,metricHover==i?ArtTheme.Mix(t.Card,t.Accent,.08):t.Card,metricSelection==i||metricHover==i?t.Accent:t.Line);
                    TextAt(g,ReportDesign.Metrics[i],label,t.Muted,x+16,y+12,cell-32);
                    double factor=i==3?100:1;TextAt(g,ReportDesign.Number(values[i]*factor),number,t.Text,x+16,y+34,cell-32);
                    TextAt(g,ReportDesign.Units[i],small,t.Muted,x+16,y+77,cell-32);
                    if(Valid(medians[i])&&Valid(values[i]))
                    {
                        double max=Math.Max(.0001,Math.Max(values[i],highs[i]))*1.12;float track=cell-32;
                        Fill(g,t.Raised,x+16,y+96,track,4);Fill(g,t.Accent,x+16,y+96,(float)(track*values[i]/max),4);
                        using(Pen pen=new Pen(t.Orange,2)){float px=x+16+(float)(track*medians[i]/max);g.DrawLine(pen,px,y+94,px,y+102);}
                        TextAt(g,"通常 "+ReportDesign.Number(medians[i]*factor)+"  ·  较高 "+ReportDesign.Number(highs[i]*factor),small,t.Muted,x+16,y+108,cell-32);
                    }
                    else TextAt(g,Valid(values[i])?hints[i]:"缺少可计算的输入或活跃时间",small,t.Muted,x+16,y+102,cell-32);
                }
            }
        }
    }
}
