using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private RectangleF infoBounds;
        private int metricHover=-1,metricSelection=-1;private bool infoHover;
        internal string SelectedMetricText {get{return metricSelection<0?null:ReportDesign.Metrics[metricSelection]+"\t"+PersonalStats.Number(values[metricSelection]*(metricSelection==3?100:1),"R")+ReportDesign.Units[metricSelection];}}
        internal static int HabitHeight(float width){return width>=780?500:640;}
        private const string InfoText="P50 = 过去 30 天历史中位数；P90 = 90% 的历史日不超过它。\n样本只取所选日之前 30 天、每项至少 7 个有效日；缺失日不补零，分母为 0 记为 —。\n个人基线与“是否高效”无关，只描述你自己的日常区间。";
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if(page==4){LiveMouseMove(e);return;}
            if(page==3){RhythmMouseMove(e);return;}
            if(page==1){DailyMouseMove(e);return;}
            if(page!=0)return;float scale=UiScale>0?UiScale:DeviceDpiScale();PointF point=new PointF(e.X/scale,e.Y/scale);
            bool overInfo=infoBounds.Contains(point);int next=-1;
            for(int i=0;i<6;i++)if(metricBounds[i].Contains(point)){next=i;break;}
            if(next==metricHover&&overInfo==infoHover)return;
            metricHover=next;infoHover=overInfo;Cursor=overInfo||next>=0?Cursors.Hand:Cursors.Default;
            metricTip.SetToolTip(this,overInfo?InfoText:next<0?"":ReportDesign.Definitions[next]+"\n"+(Valid(medians[next])?"个人参考来自之前 30 天中的有效记录。":"每项满 7 个有效历史日后显示个人参考。")+"\n点击选中后，Ctrl+C 复制完整数值。");
            Invalidate();
        }
        private float DeviceDpiScale(){using(Graphics g=CreateGraphics())return g.DpiX/96f;}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);if(page==4){metricTip.SetToolTip(this,"");return;}if(page==3){if(rhythmHover>=0){rhythmHover=-1;Invalidate();}metricTip.SetToolTip(this,"");return;}if(page==1){if(dailyHover>=0){dailyHover=-1;Invalidate();}metricTip.SetToolTip(this,"");return;}if(metricHover>=0||infoHover){metricHover=-1;infoHover=false;Invalidate();}}
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if(page==4)return;
            if(page==3)return;
            if(page==1)
            {
                if(e.Button==MouseButtons.Left)
                {
                    float scale=UiScale>0?UiScale:DeviceDpiScale();PointF point=new PointF(e.X/scale,e.Y/scale);
                    if(dailyToggle.Contains(point)){dailyCompact=point.X>dailyToggle.X+dailyToggle.Width/2;dailyHover=-1;Invalidate();}
                }
                return;
            }
            if(page==0&&e.Button==MouseButtons.Left&&metricHover>=0){metricSelection=metricHover;Focus();Invalidate();if(MetricSelected!=null)MetricSelected(this,EventArgs.Empty);}
        }
        protected override void Dispose(bool disposing){if(disposing)metricTip.Dispose();base.Dispose(disposing);}

        private static void CardSurface(Graphics g,RectangleF r,ArtTheme t,bool hover,bool selected)
        {
            RectangleF shadow=new RectangleF(r.X,r.Y+2,r.Width,r.Height);
            using(GraphicsPath shadowPath=ReportDesign.Round(shadow,8))
            using(SolidBrush shade=new SolidBrush(Color.FromArgb(52,0,0,0)))g.FillPath(shade,shadowPath);
            using(GraphicsPath path=ReportDesign.Round(r,8))
            {
                Color top=ArtTheme.Mix(t.Card,t.Text,hover?.10:.055),bottom=ArtTheme.Mix(t.Background,t.Card,.58);
                using(LinearGradientBrush fill=new LinearGradientBrush(r,top,bottom,90f))g.FillPath(fill,path);
                Color border=selected?t.Accent:hover?Color.FromArgb(90,t.Text):Color.FromArgb(32,t.Text);
                using(Pen pen=new Pen(border,hover||selected?1.4f:1f))g.DrawPath(pen,path);
            }
        }
        private static void DrawValue(Graphics g,string value,string unit,float x,float y,float width,ArtTheme t,bool percent)
        {
            using(Font number=new Font("Segoe UI",28,FontStyle.Bold,GraphicsUnit.Pixel))
            using(Font unitFont=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel))
            using(SolidBrush ink=new SolidBrush(t.Text))
            using(SolidBrush muted=new SolidBrush(t.Muted))
            {
                if(percent)
                {
                    string text=value.EndsWith("%",StringComparison.Ordinal)?value:value+"%";
                    g.DrawString(text,number,ink,new RectangleF(x,y,width,34));
                }
                else
                {
                    SizeF valueSize=g.MeasureString(value,number);
                    SizeF unitSize=g.MeasureString(unit,unitFont);
                    bool inline=unit.Length>0&&valueSize.Width+unitSize.Width+6<=width;
                    g.DrawString(value,number,ink,new RectangleF(x,y,width,34));
                    if(unit.Length>0)
                        g.DrawString(unit,unitFont,muted,new RectangleF(inline?x+valueSize.Width+6:x,inline?y+17:y+38,width,16));
                }
            }
        }
        private static void ScaleBar(Graphics g,float x,float y,float width,double value,double p50,double p90,double max,ArtTheme t,bool hover)
        {
            if(width<=8)return;
            max=Math.Max(.0001,max);float track=width,height=6;
            using(GraphicsPath basePath=ReportDesign.Round(new RectangleF(x,y,track,height),3))
            {
                using(SolidBrush baseBrush=new SolidBrush(Color.FromArgb(44,t.Text)))g.FillPath(baseBrush,basePath);
                float fill=(float)(track*Math.Max(0,value)/max);if(value<=0)fill=0;
                if(fill>0)
                {
                    Color color=value>p90&&Valid(p90)?t.Orange:value>p50&&Valid(p50)?t.Cyan:t.Accent;
                    using(GraphicsPath fillPath=ReportDesign.Round(new RectangleF(x,y,Math.Max(2,fill),height),3))
                    using(GraphicsPath glowPath=ReportDesign.Round(new RectangleF(x-1,y-1,Math.Max(3,fill)+2,height+2),4))
                    using(SolidBrush glow=new SolidBrush(Color.FromArgb(hover?55:30,color)))
                    {
                        g.FillPath(glow,glowPath);
                        using(SolidBrush fillBrush=new SolidBrush(color))g.FillPath(fillBrush,fillPath);
                    }
                }
            }
            float p50x=x+(float)(track*Math.Max(0,p50)/max),p90x=x+(float)(track*Math.Max(0,p90)/max);
            using(Pen mid=new Pen(Color.FromArgb(105,t.Text),1f))g.DrawLine(mid,p50x,y-3,p50x,y+height+3);
            using(Pen high=new Pen(Color.FromArgb(150,t.Orange),1f))g.DrawLine(high,p90x,y-3,p90x,y+height+3);
            using(Font tick=new Font("Microsoft YaHei UI",8.5f,FontStyle.Regular,GraphicsUnit.Pixel))
            using(SolidBrush muted=new SolidBrush(t.Muted))
            using(SolidBrush warm=new SolidBrush(ArtTheme.Mix(t.Muted,t.Orange,.7f)))
            {
                g.DrawString("0",tick,muted,x,y+height+5);
                float p50Label=Math.Max(x,p50x-8),p90Label=Math.Max(x+2,p90x-2);
                bool stagger=Valid(p50)&&Valid(p90)&&p90Label-p50Label<24;
                if(stagger)p50Label=Math.Max(x,p50x-20);
                if(p90Label>x+track-15)p90Label=x+track-15;
                if(Valid(p50))g.DrawString("P50",tick,muted,p50Label,y+height+5);
                if(Valid(p90))g.DrawString("P90",tick,warm,stagger?p90Label:Math.Min(x+track-15,p90Label),stagger?y+height+15:y+height+5);
            }
        }
        private static string FormatMetric(double value,int index){return index==3?ReportDesign.Number(value*100)+"":index==5?ReportDesign.Number(value)+"%":ReportDesign.Number(value);}
        private void PaintHabits(Graphics g,float width)
        {
            ArtTheme t=ArtTheme.Current;int columns=width>=780?3:2;float gap=12,pad=2,summary=(width-2*pad-2*gap)/3;
            using(Font label=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel))
            using(Font small=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel))
            using(Font tiny=new Font("Microsoft YaHei UI",9,FontStyle.Regular,GraphicsUnit.Pixel))
            using(Font title=new Font("Microsoft YaHei UI",15,FontStyle.Bold,GraphicsUnit.Pixel))
            {
                Color[] accents={t.Accent,t.Cyan,t.Green};
                string[] topLabels={"键盘总击键","鼠标总点击","活跃时长"};
                string[] topValues={hasDay?Analysis.FmtCount((long)keys):"—",hasDay?Analysis.FmtCount((long)clicks):"—",hasDay?ActivityMonitor.FormatDuration(active):"—"};
                string[] topUnits={"次击键","次点击","有效使用"};
                for(int i=0;i<3;i++)
                {
                    float x=pad+i*(summary+gap);RectangleF card=new RectangleF(x,2,summary,86);CardSurface(g,card,t,false,false);
                    using(SolidBrush dot=new SolidBrush(accents[i]))g.FillEllipse(dot,x+16,15,5,5);
                    TextAt(g,topLabels[i],label,t.Muted,x+27,10,summary-42);
                    DrawValue(g,topValues[i],topUnits[i],x+16,30,summary-32,t,false);
                }
                bool available=false;foreach(double value in medians)available|=Valid(value);
                ReportDesign.Surface(g,new RectangleF(pad,102,width-2*pad,94),ArtTheme.Mix(t.Background,t.Accent,.055),Color.FromArgb(28,t.Text));
                TextAt(g,available?"个人习惯基线":"个人习惯基线正在建立",title,t.Text,18,112,width-36);
                string subtitle=available?"基于过去 30 天历史中位数 (P50) 对比":"近 30 天已有 "+observed+" 个记录日；每项满 7 个有效日后显示 P50/P90";
                TextAt(g,subtitle,small,t.Muted,18,140,width-90);
                float subtitleWidth=TextRenderer.MeasureText(subtitle,small).Width;
                infoBounds=new RectangleF(18+Math.Min(width-104,subtitleWidth+4),137,22,22);
                using(SolidBrush info=new SolidBrush(infoHover?t.Accent:t.Muted))g.DrawString("ⓘ",small,info,infoBounds.X,infoBounds.Y-1);
                string legend=available?"色条 = 当前值 · 灰线 = P50 · 金线 = P90 · 各指标独立尺度":"缺失日期不计入基准；没有点击或活跃时间的指标不参与比较。";
                TextAt(g,legend,tiny,t.Muted,18,166,width-36);
                float cell=(width-2*pad-(columns-1)*gap)/columns;
                string[] hints={"键盘与鼠标的使用比例","活跃时间内的输入强度","活跃时间内的点击强度","操作次数，不含滚轮与移动","滚轮事件次数，不等于滚动距离","组合动作与击键有重叠"};
                for(int i=0;i<6;i++)
                {
                    float x=pad+(i%columns)*(cell+gap),y=208+(i/columns)*144;RectangleF r=new RectangleF(x,y,cell,136);metricBounds[i]=r;
                    CardSurface(g,r,t,metricHover==i,metricSelection==i);
                    TextAt(g,ReportDesign.Metrics[i],label,t.Muted,x+16,y+10,cell-32);
                    DrawValue(g,FormatMetric(values[i],i),i==3?"厘米 / 操作":i==5?"":ReportDesign.Units[i],x+16,y+28,cell-32,t,i==5);
                    if(Valid(medians[i])&&Valid(values[i]))
                    {
                        double max=Math.Max(.0001,Math.Max(values[i],highs[i]))*1.12;
                        ScaleBar(g,x+16,y+88,cell-32,values[i],medians[i],highs[i],max,t,metricHover==i);
                        TextAt(g,"通常 "+FormatMetric(medians[i],i)+"  ·  较高 "+FormatMetric(highs[i],i),tiny,t.Muted,x+16,y+115,cell-32);
                    }
                    else
                    {
                        TextAt(g,Valid(values[i])?hints[i]:"缺少可计算的输入或活跃时间",tiny,t.Muted,x+16,y+106,cell-32);
                    }
                }
            }
        }
        private void DailyMouseMove(MouseEventArgs e)
        {
            float scale=UiScale>0?UiScale:DeviceDpiScale();
            dailyMouse=new PointF(e.X/scale,e.Y/scale);
            int next=DailyIndexAt(dailyMouse.X);
            bool overInfo=dailyInfo.Contains(dailyMouse),overToggle=dailyToggle.Contains(dailyMouse);
            if(next!=dailyHover||overInfo||overToggle)
            {
                dailyHover=next;Cursor=overInfo||overToggle||next>=0?Cursors.Hand:Cursors.Default;
            }
            metricTip.SetToolTip(this,overInfo?"缺失日期不补零；今日仍在累计；柱=每日击键，橙线=完整 7 日均值。点击上方切换仅看有记录日期。":"");
            Invalidate();
        }
        private int DailyIndexAt(float x)
        {
            if(dailyStep<=0||dailyVisible.Count==0)return -1;
            int slot=(int)((x-62)/dailyStep);
            if(slot<0||slot>=dailyVisible.Count)return -1;
            return dailyVisible[slot];
        }
        private void PaintDaily(Graphics g,float width,float height)
        {
            ArtTheme t=ArtTheme.Current;
            using(Font small=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel))
            using(Font tiny=new Font("Microsoft YaHei UI",9,FontStyle.Regular,GraphicsUnit.Pixel))
            using(Font body=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel))
            {
                float left=62,right=width-24,top=138,bottom=height-44;dailyTop=top;dailyBottom=bottom;
                dailyVisible.Clear();
                if(dailyCompact){for(int i=0;i<30;i++)if(Valid(daily[i]))dailyVisible.Add(i);}
                else for(int i=0;i<30;i++)dailyVisible.Add(i);
                dailyChart=new RectangleF(left,top,Math.Max(1,right-left),Math.Max(1,bottom-top));
                dailyStep=dailyVisible.Count==0?0:(right-left)/dailyVisible.Count;
                double max=1;foreach(double v in daily)if(Valid(v))max=Math.Max(max,v);foreach(double v in averages)if(Valid(v))max=Math.Max(max,v);
                max=Math.Max(1,max*1.1);
                using(Pen grid=new Pen(Color.FromArgb(24,t.Text),1f){DashStyle=DashStyle.Dash})
                {
                    for(int i=0;i<=4;i++)
                    {
                        float y=bottom-(bottom-top)*i/4f;g.DrawLine(grid,left,y,right,y);
                    }
                }
                using(SolidBrush muted=new SolidBrush(t.Muted))
                using(StringFormat far=new StringFormat{Alignment=StringAlignment.Far,LineAlignment=StringAlignment.Near})
                {
                    g.DrawString(Analysis.FmtCount((long)max),tiny,muted,new RectangleF(4,top-7,52,14),far);
                    g.DrawString(Analysis.FmtCount((long)(max/2)),tiny,muted,new RectangleF(4,(top+bottom)/2-7,52,14),far);
                    g.DrawString("0",tiny,muted,new RectangleF(4,bottom-7,52,14),far);
                }
                if(dailyVisible.Count==0)
                {
                    TextAt(g,"暂无每日击键记录",body,t.Muted,left,(top+bottom)/2-10,right-left);
                }
                else
                {
                    for(int slot=0;slot<dailyVisible.Count;slot++)
                    {
                        int i=dailyVisible[slot];float x=left+slot*dailyStep,barW=Math.Max(2,dailyStep-3);
                        if(Valid(daily[i]))
                        {
                            float h=(float)(daily[i]/max*(bottom-top));
                            Color fill=i==29&&date.Date==DateTime.Today?t.Cyan:ArtTheme.Mix(t.Card,t.Accent,.62);
                            if(i==dailyHover)fill=ArtTheme.Mix(fill,t.Text,.22f);
                            Fill(g,fill,x,bottom-h,barW,h);
                        }
                    }
                    using(Pen line=new Pen(t.Orange,2f))
                    {
                        PointF last=PointF.Empty;bool previous=false;
                        for(int slot=0;slot<dailyVisible.Count;slot++)
                        {
                            int i=dailyVisible[slot];float x=left+slot*dailyStep+(Math.Max(2,dailyStep-3))/2;
                            if(Valid(averages[i])){PointF point=new PointF(x,bottom-(float)(averages[i]/max*(bottom-top)));if(previous)g.DrawLine(line,last,point);last=point;previous=true;}else previous=false;
                        }
                    }
                    int labelEvery=dailyCompact?Math.Max(1,dailyVisible.Count/6):7;
                    for(int slot=0;slot<dailyVisible.Count;slot+=labelEvery)
                    {
                        int i=dailyVisible[slot];float x=left+slot*dailyStep;
                        string text=date.AddDays(i-29).ToString("MM.dd");
                        TextAt(g,text,tiny,t.Muted,x-4,bottom+8,Math.Max(38,dailyStep*1.6f));
                    }
                }
                // 图例 + 信息图标
                float legendX=width-330,legendY=116;
                using(SolidBrush square=new SolidBrush(t.Accent))g.FillRectangle(square,legendX,legendY+3,9,9);
                TextAt(g,"每日击键",tiny,t.Muted,legendX+14,legendY,64);
                using(SolidBrush today=new SolidBrush(t.Cyan))g.FillRectangle(today,legendX+82,legendY+3,9,9);
                TextAt(g,date.Date==DateTime.Today?"今日实时":"选中日",tiny,t.Muted,legendX+96,legendY,64);
                using(Pen line=new Pen(t.Orange,2))g.DrawLine(line,legendX+164,legendY+8,legendX+182,legendY+8);
                TextAt(g,"7 日均线",tiny,t.Muted,legendX+188,legendY,64);
                dailyInfo=new RectangleF(legendX+250,legendY-2,20,18);
                using(SolidBrush info=new SolidBrush(t.Muted))g.DrawString("ⓘ",small,info,dailyInfo.X,dailyInfo.Y-1);
                // 双态切换:30 天完整时间轴 / 仅看有记录日期
                dailyToggle=new RectangleF(width-184,92,160,20);
                using(GraphicsPath path=ReportDesign.Round(dailyToggle,6))
                using(SolidBrush bg=new SolidBrush(Color.FromArgb(22,t.Text)))g.FillPath(bg,path);
                float half=dailyToggle.Width/2;
                using(SolidBrush active=new SolidBrush(ArtTheme.Mix(t.Card,t.Accent,.32f)))
                    g.FillRectangle(active,dailyCompact?dailyToggle.X+half:dailyToggle.X,dailyToggle.Y,half,dailyToggle.Height);
                TextAt(g,"30 天",tiny,dailyCompact?t.Muted:t.Text,dailyToggle.X+12,dailyToggle.Y+4,half-14);
                TextAt(g,"仅记录日",tiny,dailyCompact?t.Text:t.Muted,dailyToggle.X+half+8,dailyToggle.Y+4,half-12);
                // 十字准星与单日浮窗
                if(dailyHover>=0&&dailyVisible.Contains(dailyHover)&&dailyStep>0)
                {
                    int slot=dailyVisible.IndexOf(dailyHover);float cx=left+slot*dailyStep+dailyStep/2;
                    using(Pen cross=new Pen(Color.FromArgb(110,t.Text),1f){DashStyle=DashStyle.Dash})
                        g.DrawLine(cross,cx,top,cx,bottom);
                    DateTime day=date.AddDays(dailyHover-29);
                    string duration=Valid(dailyActive[dailyHover])?ActivityMonitor.FormatDuration(dailyActive[dailyHover]):"无记录";
                    string avg=Valid(averages[dailyHover])?Analysis.FmtCount((long)averages[dailyHover]):"样本不足";
                    string keysText=Valid(daily[dailyHover])?Analysis.FmtCount((long)daily[dailyHover])+" 次":"无记录";
                    float bw=228,bh=78,bx=cx+bw+12>width?cx-bw-12:cx+12,by=top+6;
                    using(GraphicsPath path=ReportDesign.Round(new RectangleF(bx,by,bw,bh),7))
                    using(SolidBrush fill=new SolidBrush(Color.FromArgb(238,ArtTheme.Mix(t.Card,t.Background,.28f))))
                    using(Pen edge=new Pen(Color.FromArgb(90,t.Accent))){g.FillPath(fill,path);g.DrawPath(edge,path);}
                    TextAt(g,day.ToString("yyyy.MM.dd ddd"),small,t.Text,bx+12,by+7,bw-24);
                    TextAt(g,"键盘击键 "+keysText,tiny,t.Muted,bx+12,by+27,bw-24);
                    TextAt(g,"7 日均值 "+avg+" 次",tiny,t.Muted,bx+12,by+43,bw-24);
                    TextAt(g,"有效使用 "+duration,tiny,t.Muted,bx+12,by+59,bw-24);
                }
            }
        }
        private static string RhythmRateLabel(double rate)
        {
            if(rate>=180)return "极高";if(rate>=100)return "高";if(rate>=50)return "中";if(rate>=20)return "低";return "极低";
        }
        private static Color RhythmColor(ArtTheme t,double rate)
        {
            return rate>=180?t.Cyan:rate>=100?ArtTheme.Mix(t.Accent,t.Cyan,.45f):rate>=50?t.Accent:ArtTheme.Mix(t.Accent,t.Card,.42f);
        }
        private static string RhythmThresholdText()
        {
            int seconds=Store.IdleThresholdSeconds;
            return seconds%60==0?(seconds/60)+" 分钟":seconds+" 秒";
        }
        private void RhythmMouseMove(MouseEventArgs e)
        {
            float scale=UiScale>0?UiScale:DeviceDpiScale();rhythmMouse=new PointF(e.X/scale,e.Y/scale);
            int next=-1;for(int i=0;i<rhythmRects.Count;i++)if(rhythmRects[i].Contains(rhythmMouse)){next=i;break;}
            bool overInfo=rhythmInfo.Contains(rhythmMouse);
            if(next!=rhythmHover){rhythmHover=next;Cursor=next>=0?Cursors.Hand:Cursors.Default;Invalidate();}
            else if(overInfo)Cursor=Cursors.Hand;
            metricTip.SetToolTip(this,overInfo?"空闲判定阈值："+RhythmThresholdText()+" 无操作；色块为连续活跃区间；空白可能是空闲、锁屏或未采集。":"");
            if(overInfo||next>=0)Invalidate();
        }
        private void PaintRhythm(Graphics g,float width,float height)
        {
            ArtTheme t=ArtTheme.Current;
            using(Font small=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel))
            using(Font tiny=new Font("Microsoft YaHei UI",9.5f,FontStyle.Regular,GraphicsUnit.Pixel))
            {
                rhythmInfo=new RectangleF(width-34,96,20,20);
                using(SolidBrush info=new SolidBrush(t.Muted))g.DrawString("ⓘ",small,info,rhythmInfo.X,rhythmInfo.Y-1);
                RectangleF surface=new RectangleF(18,116,width-36,132);
                ReportDesign.Surface(g,surface,ArtTheme.Mix(t.Background,t.Card,.55f),Color.FromArgb(24,t.Text));
                float left=30,right=width-30,track=right-left,top=126,bottom=226;
                using(Pen grid=new Pen(Color.FromArgb(22,t.Text),1f))
                {
                    for(int h=0;h<=24;h++)
                    {
                        float x=left+track*h/24f;g.DrawLine(grid,x,top,x,bottom);
                        if(h%4==0)g.DrawLine(grid,x,bottom,x,bottom+4);
                    }
                }
                rhythmRects.Clear();
                for(int i=0;i<sessions.Count;i++)
                {
                    ActiveSession session=sessions[i];
                    double start=Math.Max(0,(session.Start-date).TotalSeconds),end=Math.Min(86400,(session.End-date).TotalSeconds);
                    if(end<=start)continue;
                    float x=left+(float)(start/86400*track),w=Math.Max(2,(float)((end-start)/86400*track));
                    float rate=i<sessionRates.Count?(float)sessionRates[i]:0;
                    float barH=10+(float)Math.Min(1.0,rate/180.0)*76;
                    RectangleF rect=new RectangleF(x,bottom-barH,w,barH);
                    Color color=RhythmColor(t,rate);if(i==rhythmHover)color=ArtTheme.Mix(color,t.Text,.28f);
                    using(LinearGradientBrush fill=new LinearGradientBrush(new RectangleF(rect.X,rect.Y,rect.Width,Math.Max(1,rect.Height)),color,ArtTheme.Mix(color,t.Background,.38f),90f))
                    using(GraphicsPath path=ReportDesign.Round(rect,3))g.FillPath(fill,path);
                    if(i==rhythmHover)
                    {
                        using(Pen edge=new Pen(Color.FromArgb(180,t.Text),1.2f))
                        using(GraphicsPath edgePath=ReportDesign.Round(rect,3))g.DrawPath(edge,edgePath);
                    }
                    rhythmRects.Add(rect);
                }
                if(date.Date==DateTime.Today)
                {
                    double seconds=(DateTime.Now-date.Date).TotalSeconds;float x=left+(float)(Math.Max(0,Math.Min(86400,seconds))/86400*track);
                    if(x<right)Fill(g,Color.FromArgb(20,t.Background),x,top,right-x,bottom-top);
                    using(Pen nowPen=new Pen(t.Red,1.3f){DashStyle=DashStyle.Dash})g.DrawLine(nowPen,x,top-6,x,bottom);
                    using(SolidBrush glow=new SolidBrush(Color.FromArgb(65,t.Red)))g.FillEllipse(glow,x-5,top-11,10,10);
                    using(SolidBrush dot=new SolidBrush(t.Red))g.FillEllipse(dot,x-2,top-8,4,4);
                    TextAt(g,DateTime.Now.ToString("HH:mm"),tiny,t.Red,x-16,112,34);
                }
                using(StringFormat center=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Near})
                using(SolidBrush muted=new SolidBrush(t.Muted))
                {
                    for(int h=0;h<=24;h+=4)
                    {
                        float x=left+track*h/24f;g.DrawString(h.ToString("00")+":00",tiny,muted,new RectangleF(x-22,bottom+6,44,14),center);
                    }
                }
                // 上半部分:每小时活跃分钟
                float gap=18,moduleY=260,moduleH=228,leftW=(width-54)*.55f;
                PaintHourlyModule(g,t,new RectangleF(18,moduleY,leftW,moduleH),small,tiny);
                PaintBucketModule(g,t,new RectangleF(18+leftW+gap,moduleY,width-36-leftW-gap,moduleH),small,tiny);
                if(rhythmHover>=0&&rhythmHover<sessions.Count)
                {
                    ActiveSession session=sessions[rhythmHover];double seconds=session.Seconds;
                    double maxSeconds=0;foreach(ActiveSession item in sessions)maxSeconds=Math.Max(maxSeconds,item.Seconds);
                    string range=session.Start.ToString("HH:mm")+" - "+session.End.ToString("HH:mm")+" · "+ActivityMonitor.FormatDuration(seconds);
                    string longest=seconds>=maxSeconds-.5?"最长专注段 · ":"";
                    string counts="击键 "+Analysis.FmtCount((long)Math.Max(0,rhythmHover<sessionKeys.Count?sessionKeys[rhythmHover]:0))+" 次 · 点击 "+Analysis.FmtCount((long)Math.Max(0,rhythmHover<sessionClicks.Count?sessionClicks[rhythmHover]:0))+" 次";
                    double rate=rhythmHover<sessionRates.Count?sessionRates[rhythmHover]:0;
                    float bw=330,bh=78,bx=rhythmMouse.X+16,by=rhythmMouse.Y+16;
                    if(bx+bw>width-10)bx=rhythmMouse.X-bw-16;if(bx<8)bx=8;if(by+bh>height-8)by=rhythmMouse.Y-bh-16;if(by<8)by=8;
                    using(GraphicsPath path=ReportDesign.Round(new RectangleF(bx,by,bw,bh),7))
                    using(SolidBrush fill=new SolidBrush(Color.FromArgb(240,ArtTheme.Mix(t.Card,t.Background,.25f))))
                    using(Pen edge=new Pen(Color.FromArgb(95,t.Accent))){g.FillPath(fill,path);g.DrawPath(edge,path);}
                    TextAt(g,longest+range,small,t.Text,bx+12,by+8,bw-24);
                    TextAt(g,counts,tiny,t.Muted,bx+12,by+32,bw-24);
                    TextAt(g,"强度 "+RhythmRateLabel(rate)+" · "+PersonalStats.Number(rate,"0")+" 次/分钟",tiny,t.Muted,bx+12,by+52,bw-24);
                }
            }
        }
        private void PaintHourlyModule(Graphics g,ArtTheme t,RectangleF r,Font small,Font tiny)
        {
            ReportDesign.Surface(g,r,ArtTheme.Mix(t.Background,t.Card,.35f),Color.FromArgb(20,t.Text));
            TextAt(g,"每小时活跃分钟",small,t.Text,r.X+14,r.Y+12,r.Width-28);
            float left=r.X+34,right=r.Right-14,track=right-left,top=r.Y+42,bottom=r.Bottom-30;
            using(Pen grid=new Pen(Color.FromArgb(20,t.Text),1f){DashStyle=DashStyle.Dash})
            {
                g.DrawLine(grid,left,bottom,right,bottom);g.DrawLine(grid,left,top+(bottom-top)/2,right,top+(bottom-top)/2);
            }
            float step=track/24;
            for(int h=0;h<24;h++)
            {
                double minutes=hourActive[h]/60.0;if(minutes<0)minutes=0;
                float barH=(float)Math.Min(1,minutes/60)*(bottom-top);
                Color color=h==DateTime.Now.Hour&&date.Date==DateTime.Today?t.Cyan:t.Accent;
                Fill(g,ArtTheme.Mix(color,t.Background,.18f),left+h*step+1,bottom-barH,Math.Max(2,step-2),barH);
            }
            using(SolidBrush muted=new SolidBrush(t.Muted))
            {
                TextAt(g,"60",tiny,t.Muted,left-26,top-5,26);TextAt(g,"0",tiny,t.Muted,left-20,bottom-7,20);
            }
            for(int h=0;h<24;h+=6)TextAt(g,h.ToString("00"),tiny,t.Muted,left+h*step-4,bottom+7,26);
        }
        private void PaintBucketModule(Graphics g,ArtTheme t,RectangleF r,Font small,Font tiny)
        {
            ReportDesign.Surface(g,r,ArtTheme.Mix(t.Background,t.Card,.35f),Color.FromArgb(20,t.Text));
            TextAt(g,"专注段分布",small,t.Text,r.X+14,r.Y+12,r.Width-28);
            // 阈值与"短段占比"公式与主面板共用 ReportDesign,避免两页各算一套(以前 47/48 在这里是 97%、在面板是 98%)。
            int[] bins=ReportDesign.SegmentBins(sessions);
            string[] names=ReportDesign.SegmentBinNames;
            int max=1;foreach(int value in bins)max=Math.Max(max,value);
            for(int i=0;i<4;i++)
            {
                float y=r.Y+44+i*32;TextAt(g,names[i],tiny,t.Muted,r.X+14,y,r.Width-80);
                float barW=(r.Width-130)*(float)bins[i]/max;
                Fill(g,ArtTheme.Mix(t.Raised,t.Text,.08f),r.X+112,y+3,r.Width-130,9);
                if(bins[i]>0)Fill(g,i==3?t.Orange:t.Accent,r.X+112,y+3,Math.Max(3,barW),9);
                TextAt(g,bins[i]+" 次",tiny,t.Text,r.Right-60,y,r.Width-64);
            }
            int total=sessions.Count;
            string summary=total==0?"暂无样本":"短段占比 (<5m) "+ReportDesign.ShortShare(bins[0],total)+" · 长段占比 "+ReportDesign.ShortShare(bins[3],total);
            TextAt(g,summary,tiny,t.Muted,r.X+14,r.Bottom-26,r.Width-28);
        }
        private void LiveMouseMove(MouseEventArgs e)
        {
            float scale=UiScale>0?UiScale:DeviceDpiScale();PointF point=new PointF(e.X/scale,e.Y/scale);
            bool overInfo=liveInfo.Contains(point);
            Cursor=overInfo?Cursors.Hand:Cursors.Default;
            metricTip.SetToolTip(this,overInfo?"APM = 最近 60 秒击键 + 点击；每秒一个采样点，近 5 分钟共 300 个点。断线表示未观测，空白不当作 0。":"");
            if(overInfo)Invalidate();
        }
        private void PaintLive(Graphics g,float width,float height,Font small,Font tiny,Font body)
        {
            ArtTheme t=ArtTheme.Current;
            float left=64,right=width-20,top=136,bottom=418;
            long[] values=LiveRate.Trend();
            double[] smooth=new double[values.Length];
            for(int i=0;i<values.Length;i++)
            {
                if(values[i]<0){smooth[i]=-1;continue;}
                double sum=0;int count=0;
                for(int k=Math.Max(0,i-2);k<=Math.Min(values.Length-1,i+2);k++)if(values[k]>=0){sum+=values[k];count++;}
                smooth[i]=count>0?sum/count:values[i];
            }
            long peak=0;foreach(long value in values)if(value>=0)peak=Math.Max(peak,value);
            long yMax=peak<=20?20:peak<=50?50:((peak+99)/100)*100;
            long tickStep=yMax<=50?10:100;
            RectangleF plot=new RectangleF(left,top,right-left,bottom-top);
            using(Pen grid=new Pen(Color.FromArgb(24,t.Text),1f){DashStyle=DashStyle.Dash})
            {
                for(long tick=0;tick<=yMax;tick+=tickStep)
                {
                    float y=bottom-(float)((double)tick/yMax*(bottom-top));
                    g.DrawLine(grid,left,y,right,y);
                    TextAt(g,tick.ToString(),tiny,t.Muted,left-44,y-7,36);
                }
            }
            using(Pen baseline=new Pen(Color.FromArgb(70,t.Text),1f))g.DrawLine(baseline,left,bottom,right,bottom);
            int runStart=-1;for(int i=0;i<=values.Length;i++)
            {
                bool valid=i<values.Length&&values[i]>=0;
                if(valid&&runStart<0)runStart=i;
                if((!valid||i==values.Length)&&runStart>=0)
                {
                    int start=runStart,end=i-1;runStart=-1;
                    if(end-start<1)continue;
                    PointF[] points=new PointF[end-start+1];
                    for(int j=start;j<=end;j++)points[j-start]=new PointF(left+plot.Width*j/(values.Length-1f),bottom-(float)(smooth[j]/yMax*(bottom-top)));
                    using(GraphicsPath linePath=new GraphicsPath())using(GraphicsPath areaPath=new GraphicsPath())
                    {
                        if(points.Length>=3){linePath.AddCurve(points,.28f);areaPath.AddCurve(points,.28f);}
                        else {linePath.AddLines(points);areaPath.AddLines(points);}
                        PointF last=points[points.Length-1],first=points[0];
                        areaPath.AddLine(last,new PointF(last.X,bottom));areaPath.AddLine(new PointF(last.X,bottom),new PointF(first.X,bottom));areaPath.AddLine(new PointF(first.X,bottom),first);areaPath.CloseFigure();
                        using(LinearGradientBrush area=new LinearGradientBrush(plot,Color.FromArgb(74,t.Cyan),Color.FromArgb(0,t.Cyan),90f))g.FillPath(area,areaPath);
                        using(Pen glow=new Pen(Color.FromArgb(45,t.Cyan),5f))g.DrawPath(glow,linePath);
                        using(Pen line=new Pen(t.Cyan,1.8f))g.DrawPath(line,linePath);
                    }
                }
            }
            // 折线最右端的呼吸点:传达“正在实时采样”
            int latest=-1;for(int i=values.Length-1;i>=0;i--)if(values[i]>=0){latest=i;break;}
            if(latest>=0)
            {
                float x=left+plot.Width*latest/(values.Length-1f),y=bottom-(float)(smooth[latest]/yMax*(bottom-top));
                float pulse=(float)((Math.Sin(DateTime.Now.TimeOfDay.TotalSeconds*Math.PI*2/1.8)+1)/2);
                using(SolidBrush halo=new SolidBrush(Color.FromArgb((int)(35+55*pulse),t.Cyan)))g.FillEllipse(halo,x-7-pulse*4,y-7-pulse*4,14+pulse*8,14+pulse*8);
                using(SolidBrush dot=new SolidBrush(t.Cyan))g.FillEllipse(dot,x-3,y-3,6,6);
            }
            TextAt(g,"5 分钟前",tiny,t.Muted,left,bottom+8,80);TextAt(g,"现在",tiny,t.Muted,right-38,bottom+8,38);
            // 下方两个轻量实时维度:键盘/鼠标负载 + 高频组合键
            float gap=16,moduleY=434,moduleH=102,leftW=(width-54)*.55f;
            PaintLiveLoad(g,t,new RectangleF(18,moduleY,leftW,moduleH),small,tiny);
            PaintLiveCombos(g,t,new RectangleF(18+leftW+gap,moduleY,width-36-leftW-gap,moduleH),small,tiny);
        }
        private void PaintLiveLoad(Graphics g,ArtTheme t,RectangleF r,Font small,Font tiny)
        {
            ReportDesign.Surface(g,r,ArtTheme.Mix(t.Background,t.Card,.35f),Color.FromArgb(20,t.Text));
            TextAt(g,"实时键盘 / 鼠标负载",small,t.Text,r.X+14,r.Y+12,r.Width-28);
            long keys=LiveRate.KeysPerMin,clicks=LiveRate.ClicksPerMin,total=keys+clicks;
            if(total<=0){TextAt(g,"暂无实时输入",small,t.Muted,r.X+14,r.Y+50,r.Width-28);return;}
            double keyShare=(double)keys/total,clickShare=(double)clicks/total;
            float left=r.X+14,width=r.Width-28,y=r.Y+46;
            Fill(g,ArtTheme.Mix(t.Raised,t.Text,.08f),left,y,width,10);
            Fill(g,t.Cyan,left,y,(float)(width*keyShare),10);
            Fill(g,t.Orange,left+(float)(width*keyShare),y,(float)(width*clickShare),10);
            TextAt(g,"键盘 "+PersonalStats.Number(keyShare*100,"0")+"%  ·  "+keys+" / 分",tiny,t.Text,left,y+18,width);
            TextAt(g,"鼠标 "+PersonalStats.Number(clickShare*100,"0")+"%  ·  "+clicks+" / 分",tiny,t.Muted,left,y+36,width);
        }
        private void PaintLiveCombos(Graphics g,ArtTheme t,RectangleF r,Font small,Font tiny)
        {
            ReportDesign.Surface(g,r,ArtTheme.Mix(t.Background,t.Card,.35f),Color.FromArgb(20,t.Text));
            TextAt(g,"高频组合键",small,t.Text,r.X+14,r.Y+12,r.Width-28);
            List<KeyValuePair<string,long>> combos=ShortcutStats.Ranking(new DayRecord[]{Store.Today});
            if(combos.Count==0){TextAt(g,"暂无组合键记录",tiny,t.Muted,r.X+14,r.Y+50,r.Width-28);return;}
            for(int i=0;i<Math.Min(3,combos.Count);i++)
            {
                string name=ShortcutStats.Display(combos[i].Key);float y=r.Y+36+i*22;
                using(Font font=new Font("Microsoft YaHei UI",10.5f,FontStyle.Regular,GraphicsUnit.Pixel))
                {
                    float w=Math.Min(r.Width-80,TextRenderer.MeasureText(name,font).Width+20);
                    using(GraphicsPath path=ReportDesign.Round(new RectangleF(r.X+14,y,w,20),6))
                    using(SolidBrush fill=new SolidBrush(Color.FromArgb(30,t.Accent)))g.FillPath(fill,path);
                    TextAt(g,name,font,t.Text,r.X+24,y+3,w-20);
                }
                TextAt(g,Analysis.FmtCount(combos[i].Value)+" 次",tiny,t.Muted,r.Right-64,y+3,54);
            }
        }
    }
}
