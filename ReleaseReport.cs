using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal sealed class CrossChartRow
    {
        public string Name,Detail;public double[] Values;public double Total;
    }
    internal sealed class CrossReportData
    {
        public string Title,Note,Caption,Footer;
        public string[] Headings,Cards,CardValues,SeriesNames,SeriesPaths;
        public readonly List<string[]> Rows=new List<string[]>();
        public readonly List<CrossChartRow> Chart=new List<CrossChartRow>();
        public double[] Coverage=new double[24];
        public static string AppName(string path){return path==CrossTelemetry.Other?"其他应用汇总":string.IsNullOrEmpty(path)?"未识别应用":Path.GetFileName(path);}
        private static string Percent(double n,double d){return d>0?(n*100/d).ToString("0.#")+"%":"--";}
        private static double Sum(double[] values){double n=0;foreach(double v in values)n+=v;return n;}
        private static string Duration(double seconds){return ActivityMonitor.FormatDuration(seconds);}
        public static CrossReportData Build(DateTime date,int page)
        {
            if(page==10)return ShortcutSavings.Build(Analysis.GetDay(date));
            DayRecord day=Analysis.GetDay(date);bool missing=day==null;day=day??new DayRecord{Date=date};CrossDay cross=day.Cross;
            CrossReportData data=new CrossReportData();double coverage=Sum(cross.ObservedHours);
            if(page==5)
            {
                data.Title="应用时段";data.Caption="应用 × 小时 · 活跃时间热图（Top 8）";
                data.Note="仅 1.0.0 起真实归因的活跃秒数，旧数据不反推。斜线表示该小时没有新版活跃观测；有观测时灰格表示未归因给该应用。\n色阶固定 0—60 分钟；快速切换、识别失败及旧记录单列。超过 128 个进程归入其他应用汇总。";
                data.Headings=new[]{"应用 / 进程","时段","归因时长","占该小时新版活跃"};
                double attributed=0;Array.Copy(cross.ObservedHours,data.Coverage,24);
                foreach(KeyValuePair<string,double[]> app in cross.AppHours)
                {
                    double total=Sum(app.Value);attributed+=total;data.Chart.Add(new CrossChartRow{Name=AppName(app.Key),Detail=app.Key,Values=(double[])app.Value.Clone(),Total=total});
                    for(int h=0;h<24;h++)if(app.Value[h]>0)data.Rows.Add(new[]{AppName(app.Key)+" · "+app.Key,h.ToString("00")+":00—"+(h+1).ToString("00")+":00",Duration(app.Value[h]),Percent(app.Value[h],cross.ObservedHours[h])});
                }
                data.Chart.Sort(delegate(CrossChartRow a,CrossChartRow b){return b.Total.CompareTo(a.Total);});
                for(int h=0;h<24;h++)
                {
                    double known=0;foreach(double[] hours in cross.AppHours.Values)known+=hours[h];double unknown=Math.Max(0,cross.ObservedHours[h]-known);
                    if(unknown>0)data.Rows.Add(new[]{"新版活跃未归因",h.ToString("00")+":00",Duration(unknown),Percent(unknown,cross.ObservedHours[h])});
                }
                data.Rows.Add(new[]{"旧数据 / 无交叉观测","全日",Duration(Math.Max(0,day.ActiveSeconds-coverage)),"不分配到应用"});
                data.Cards=new[]{"已记录活跃时间","已识别应用占比","已记录应用","无交叉记录的时间"};data.CardValues=new[]{Duration(coverage),Percent(attributed,coverage),cross.AppHours.Count.ToString(),Duration(Math.Max(0,day.ActiveSeconds-coverage))};
                data.Footer=coverage>0?"悬停格子查看应用、时间和分钟数；明细 / CSV 包含全部已归因时段。":"尚无交叉时间数据；安装 1.0.0 后，在活跃使用中逐步积累。";
            }
            else if(page==6)
            {
                data.Title="应用操作";data.Caption="应用 × 操作类型 · 各应用内部构成（Top 6）";
                data.Note="击键、点击、滚轮按已采集事件数展示，三者相加只用于构成比例，不评价效率。滚轮为事件次数，不是滚动距离。\n按进程合并窗口；旧记录与无法归因的输入单列。颜色依次为击键、点击、滚轮。";
                data.Headings=new[]{"应用 / 进程","击键","点击","滚轮","应用内键 / 点 / 滚比例"};
                Dictionary<string,double[]> apps=new Dictionary<string,double[]>(StringComparer.OrdinalIgnoreCase);double[] attributed=new double[3];
                foreach(AppUsage app in day.Apps.Values){double[] v;if(!apps.TryGetValue(app.ProcessPath,out v)){v=new double[3];apps[app.ProcessPath]=v;}v[0]+=app.Keys;v[1]+=app.Clicks;v[2]+=app.Wheel;}
                foreach(KeyValuePair<string,double[]> app in apps)
                {for(int i=0;i<3;i++)attributed[i]+=app.Value[i];data.Chart.Add(new CrossChartRow{Name=AppName(app.Key),Detail=app.Key,Values=app.Value,Total=Sum(app.Value)});}
                double[] unknown={Math.Max(0,day.Keys-attributed[0]),Math.Max(0,day.Clicks-attributed[1]),Math.Max(0,day.Wheel-attributed[2])};
                if(Sum(unknown)>0)data.Chart.Add(new CrossChartRow{Name="旧记录 / 未归因",Detail="不能还原对应应用",Values=unknown,Total=Sum(unknown)});
                data.Chart.Sort(delegate(CrossChartRow a,CrossChartRow b){return b.Total.CompareTo(a.Total);});
                foreach(CrossChartRow row in data.Chart)data.Rows.Add(new[]{row.Name+" · "+row.Detail,row.Values[0].ToString("N0"),row.Values[1].ToString("N0"),row.Values[2].ToString("N0"),Percent(row.Values[0],row.Total)+" / "+Percent(row.Values[1],row.Total)+" / "+Percent(row.Values[2],row.Total)});
                data.Cards=new[]{"记录击键","记录点击","滚轮事件","已归因事件占比"};data.CardValues=new[]{day.Keys.ToString("N0"),day.Clicks.ToString("N0"),day.Wheel.ToString("N0"),Percent(Sum(attributed),(double)day.Keys+day.Clicks+day.Wheel)};
                data.Footer="每行独立归一化为 100%；悬停看绝对次数，不能用条长比较应用总操作量。";
            }
            else if(page==7)
            {
                data.Title="会话应用";data.Caption="会话 × 应用 · 最近 6 段（同色表示同一应用）";
                data.Note="按会话内实际采样的进程路径关联应用，不按全天占比拆分。全日停留最多的四个应用使用固定颜色；其余已识别应用单列。\n未识别 = 已观测但不能归因；未采集 = 整段有效时间减去交叉观测。旧记录不会反推。明细包含全部会话及应用，悬停色块查看对应时长。";
                data.Headings=new[]{"连续使用段","应用 / 进程","活跃秒数","占整段","开始原因"};
                Dictionary<string,double> totals=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
                foreach(ActiveSession session in day.Sessions)foreach(KeyValuePair<string,double> app in session.AppSeconds){double v;totals.TryGetValue(app.Key,out v);totals[app.Key]=v+app.Value;}
                List<KeyValuePair<string,double>> top=new List<KeyValuePair<string,double>>(totals);top.RemoveAll(delegate(KeyValuePair<string,double> p){return p.Key==CrossTelemetry.Other;});top.Sort(delegate(KeyValuePair<string,double> a,KeyValuePair<string,double> b){int result=b.Value.CompareTo(a.Value);return result!=0?result:string.Compare(a.Key,b.Key,StringComparison.OrdinalIgnoreCase);});
                int n=Math.Min(4,top.Count);data.SeriesNames=new string[n+3];for(int i=0;i<n;i++)data.SeriesNames[i]=AppName(top[i].Key);data.SeriesNames[n]="其他应用";data.SeriesNames[n+1]="未识别";data.SeriesNames[n+2]="未采集";data.SeriesPaths=(string[])data.SeriesNames.Clone();for(int i=0;i<n;i++)data.SeriesPaths[i]=top[i].Key;
                double observed=0,attributed=0,sessionTotal=0;
                foreach(ActiveSession session in day.Sessions)
                {
                    sessionTotal+=session.Seconds;string label=session.Start.ToString("HH:mm:ss")+"—"+session.End.ToString("HH:mm:ss");double[] parts=new double[n+3];double sum=0;
                    foreach(KeyValuePair<string,double> app in session.AppSeconds){int index=n;for(int i=0;i<n;i++)if(string.Equals(top[i].Key,app.Key,StringComparison.OrdinalIgnoreCase)){index=i;break;}parts[index]+=app.Value;sum+=app.Value;
                        data.Rows.Add(new[]{label,AppName(app.Key)+" · "+app.Key,app.Value.ToString("R",System.Globalization.CultureInfo.InvariantCulture),Percent(app.Value,session.Seconds),ActivityMonitor.SessionReason(session)});}
                    parts[n+1]=Math.Max(0,session.CrossObservedSeconds-sum);parts[n+2]=Math.Max(0,session.Seconds-session.CrossObservedSeconds);observed+=session.CrossObservedSeconds;attributed+=sum;
                    for(int i=n+1;i<n+3;i++)if(parts[i]>0)data.Rows.Add(new[]{label,data.SeriesNames[i],parts[i].ToString("R",System.Globalization.CultureInfo.InvariantCulture),Percent(parts[i],session.Seconds),ActivityMonitor.SessionReason(session)});
                    data.Chart.Add(new CrossChartRow{Name=session.Start.ToString("HH:mm")+"—"+session.End.ToString("HH:mm"),Values=parts,Total=session.Seconds,Detail=label+" · "+ActivityMonitor.SessionReason(session)});
                }
                data.Chart.Reverse();data.Cards=new[]{"连续使用段","已观测段内时间","已识别应用时间","应用归因占整段"};data.CardValues=new[]{day.Sessions.Count.ToString(),Duration(observed),Duration(attributed),Percent(attributed,sessionTotal)};
                data.Footer="每行占该段有效时长的 100%；颜色在全日会话间保持一致。";
            }
            else if(page==8)
            {
                data.Title="鼠标矢量";data.Caption="上下左右 · 传感器方向分量占比";
                data.Note="方向分母为 |dx|+|dy|，单位为原始计数，不是欧氏路程；上/下仅表示传感器 Y 轴，不推断左右手或游戏走位。\n快速移动：50—80 ms 内路程≥8 mm，冷却200 ms；掉头：相邻向量均≥2 mm且转角≥120°，间隔≤120 ms，冷却150 ms。两项需正确 DPI。";
                data.Headings=new[]{"指标","数值","比例 / 覆盖","口径"};double total=Sum(cross.Direction);
                string[] names={"向左","向右","向上","向下"};
                for(int i=0;i<4;i++){data.Chart.Add(new CrossChartRow{Name=names[i],Values=new[]{cross.Direction[i]},Total=total,Detail=cross.Direction[i].ToString("N0")+" 原始轴计数 · "+Percent(cross.Direction[i],total)});data.Rows.Add(new[]{names[i],cross.Direction[i].ToString("N0"),Percent(cross.Direction[i],total),"按绝对轴分量统计"});}
                data.Rows.Add(new[]{"flick 候选 / 快速移动",cross.Flicks.ToString(),cross.CalibratedPackets+" / "+cross.Packets+" 有 DPI 移动包","阈值候选，不等同于瞄准动作"});
                data.Rows.Add(new[]{"急转 / 掉头",cross.Reversals.ToString(),"≥120°","按设备分离，间隔过长 / DPI 改变重置向量"});
                data.Rows.Add(new[]{"按住按钮时的移动",cross.DragCounts.ToString("N0")+" counts",Percent(cross.DragCounts,cross.PathCounts),"任一鼠标按钮按住；不判定拖动是否成功"});
                data.Rows.Add(new[]{"移动后点击",cross.MoveClicks.ToString(),Percent(cross.MoveClicks,cross.Clicks),"点击前 200 ms 内有相对移动观测"});
                data.Rows.Add(new[]{"未观测到紧邻移动的点击",Math.Max(0,cross.Clicks-cross.MoveClicks).ToString(),Percent(cross.Clicks-cross.MoveClicks,cross.Clicks),"不代表绝对静止；仅已启用相对输入后的点击"});
                data.Cards=new[]{"快速移动候选","急转 / 掉头","按住移动占比","移动后点击占比"};data.CardValues=new[]{cross.CalibratedPackets>0?cross.Flicks.ToString():"需 DPI 数据",cross.CalibratedPackets>0?cross.Reversals.ToString():"需 DPI 数据",Percent(cross.DragCounts,cross.PathCounts),Percent(cross.MoveClicks,cross.Clicks)};
                data.Footer=cross.Packets>0?"仅 1.0.0 相对鼠标数据 · "+cross.Packets.ToString("N0")+" 个非零移动包 · 排除校准期间，绝对坐标设备不参与。":"尚无矢量记录；开始移动相对鼠标后积累，旧距离无法还原方向。";
            }
            else
            {
                data.Title="键位语义";data.Caption="默认游戏键位映射 · 操作结构（非用途识别）";
                data.Note="互斥归类：WASD/方向键→移动；数字/小键盘数字→技能栏；空格/Shift→交互；Ctrl/Alt/Win/Tab/Esc/F键→功能；其余→文字及其他。\n同一击键只计一组，组合动作单独展示，不重复加入击键分母。键位用途因游戏而异，输入文本中的 WASD 也会进入移动类。";
                data.Headings=new[]{"键位分组","击键次数","占记录击键","解释"};long[] counts=KeySemantics.Count(day);double total=0;foreach(long count in counts)total+=count;
                for(int i=0;i<6;i++){data.Chart.Add(new CrossChartRow{Name=KeySemantics.Names[i],Values=new[]{(double)counts[i]},Total=total,Detail=counts[i].ToString("N0")+" 次 · "+Percent(counts[i],total)});data.Rows.Add(new[]{KeySemantics.Names[i],counts[i].ToString("N0"),Percent(counts[i],total),i==5?"旧数据无对应按键": "固定默认键位映射，不推断真实用途"});}
                long combos=PersonalStats.Combos(day);data.Rows.Add(new[]{"组合动作（另计）",combos.ToString("N0"),"不纳入上述分母","与击键可能关联，不作为额外击键"});
                data.Cards=new[]{"记录击键","移动类键位占比","组合动作（另计）","未归位击键"};data.CardValues=new[]{total.ToString("N0"),Percent(counts[0],total),combos.ToString("N0"),counts[5].ToString("N0")};
                data.Footer=total>0?"可分析已有 KeyCounts；组别表示按键分布，不意味着这些输入发生在游戏中。":"所选日期没有击键记录，暂不显示构成比例。";
            }
            if(missing)data.Footer="所选日期没有采集记录。";
            if(data.Rows.Count==0){string[] empty=new string[data.Headings.Length];for(int i=0;i<empty.Length;i++)empty[i]=i==0?"暂无记录":"";data.Rows.Add(empty);}
            return data;
        }
    }
    internal sealed class CrossReportVisual : Control
    {
        internal float UiScale;internal readonly CrossReportData Data;private readonly int page;private readonly DateTime date;private int hover=-1;
        private readonly List<KeyValuePair<RectangleF,string>> targets=new List<KeyValuePair<RectangleF,string>>();
        public CrossReportVisual(DateTime date,int page,CrossReportData snapshot=null)
        {
            this.date=date;this.page=page;Data=snapshot??CrossReportData.Build(date,page);DoubleBuffered=true;ResizeRedraw=true;
            MouseMove+=delegate(object sender,MouseEventArgs e){float scale=DeviceScale;PointF point=new PointF(e.X/scale,e.Y/scale);int next=-1;for(int i=0;i<targets.Count;i++)if(targets[i].Key.Contains(point)){next=i;break;}if(hover!=next){hover=next;detailTip.SetToolTip(this,next>=0?targets[next].Value:"");Invalidate();}};
            MouseLeave+=delegate{hover=-1;Invalidate();};
        }
        private readonly ToolTip detailTip=new ToolTip{InitialDelay=250,ReshowDelay=100,AutoPopDelay=20000};
        protected override void Dispose(bool disposing){if(disposing)detailTip.Dispose();base.Dispose(disposing);}
        private float DeviceScale=1;
        private static void TextAt(Graphics g,string text,Font font,Color color,RectangleF rect)
        {using(SolidBrush brush=new SolidBrush(color))using(StringFormat format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(text??"",font,brush,rect,format);}
        private static void Fill(Graphics g,Color color,RectangleF rect){if(rect.Width>0&&rect.Height>0)using(SolidBrush b=new SolidBrush(color))g.FillRectangle(b,rect);}
        internal void RenderTo(Graphics g,float scale)
        {
            float previous=UiScale;UiScale=scale;
            try{using(PaintEventArgs args=new PaintEventArgs(g,new Rectangle(0,0,Math.Max(1,(int)(ClientSize.Width*scale)),Math.Max(1,(int)(ClientSize.Height*scale)))))OnPaint(args);}
            finally{UiScale=previous;}
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;float scale=UiScale>0?UiScale:g.DpiX/96f;DeviceScale=scale;g.ScaleTransform(scale,scale);float w=ClientSize.Width/scale;
            ArtTheme t=ArtTheme.Current;g.Clear(t.Background);g.SmoothingMode=SmoothingMode.AntiAlias;targets.Clear();
            using(Font small=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel))
            using(Font body=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel))
            using(Font large=new Font("Segoe UI",23,FontStyle.Bold,GraphicsUnit.Pixel))
            {
                float card=(w-44)/4;
                for(int i=0;i<4;i++){float x=10+i*(card+8);ReportDesign.Surface(g,new RectangleF(x,10,card,67),t.Card,t.Line);TextAt(g,Data.Cards[i],small,t.Muted,new RectangleF(x+12,17,card-24,20));TextAt(g,Data.CardValues[i],Data.CardValues[i].Length>10?body:large,t.Text,new RectangleF(x+12,37,card-24,32));}
                TextAt(g,Data.Caption,body,t.Text,new RectangleF(14,86,w-160,23));
                Color[] colors={t.Accent,t.Cyan,t.Orange,t.Purple,t.Green,t.Muted,t.Line};
                if(page==5)
                {
                    float left=155,step=(w-left-18)/24;
                    for(int h=0;h<24;h+=3)TextAt(g,h.ToString("00"),small,t.Muted,new RectangleF(left+h*step,108,32,18));
                    int shown=Math.Min(8,Data.Chart.Count);float rowHeight=Math.Min(32,110f/Math.Max(1,shown));
                    for(int i=0;i<shown;i++)
                    {
                        CrossChartRow row=Data.Chart[i];float y=129+i*rowHeight;TextAt(g,row.Name,small,t.Text,new RectangleF(14,y+(rowHeight-17)/2,137,18));
                        for(int h=0;h<24;h++)
                        {
                            RectangleF rect=new RectangleF(left+h*step,y,step-3,rowHeight-3);Fill(g,Data.Coverage[h]<=0?t.Raised:row.Values[h]<=0?HeatScale.Zero:HeatScale.At(row.Values[h]/3600),rect);
                            if(Data.Coverage[h]<=0)using(Pen pen=new Pen(t.Muted))g.DrawLine(pen,rect.Left+2,rect.Bottom-2,rect.Right-2,rect.Top+2);
                            targets.Add(new KeyValuePair<RectangleF,string>(rect,row.Name+" · "+h.ToString("00")+":00 · "+(Data.Coverage[h]<=0?"无新版活跃观测":(row.Values[h]/60).ToString("0.##")+" 分钟（已观测部分）")));
                        }
                    }
                    if(Data.Chart.Count==0)TextAt(g,"暂无应用 × 小时观测",body,t.Muted,new RectangleF(18,158,w-36,28));
                }
                else
                {
                    int count=Math.Min(6,Data.Chart.Count);float left=page==7?125:180,barWidth=w-left-132;
                    for(int i=0;i<count;i++)
                    {
                        CrossChartRow row=Data.Chart[i];float y=119+i*20;TextAt(g,row.Name,small,t.Text,new RectangleF(14,y, left-22,20));RectangleF bar=new RectangleF(left,y+3,barWidth,12);Fill(g,t.Raised,bar);
                        double cursor=0;for(int part=0;part<row.Values.Length;part++){double fraction=row.Total>0?Math.Max(0,Math.Min(1-cursor,Math.Max(0,row.Values[part]/row.Total))):0;RectangleF segment=new RectangleF(bar.X+(float)cursor*bar.Width,bar.Y,(float)fraction*bar.Width,bar.Height);Fill(g,row.Values.Length==1?colors[i%colors.Length]:colors[part%colors.Length],segment);
                        if(page==7&&fraction>0)targets.Add(new KeyValuePair<RectangleF,string>(segment,row.Detail+" · "+Data.SeriesPaths[part]+" · "+ActivityMonitor.FormatDuration(row.Values[part])+" · "+(fraction*100).ToString("0.#")+"%"));cursor+=fraction;}
                        string label=page==10?"≈ "+ActivityMonitor.FormatDuration(row.Values[0]):page==7?ActivityMonitor.FormatDuration(row.Total):row.Values.Length==1?(row.Total>0?(row.Values[0]*100/row.Total).ToString("0.#")+"%":"--"):row.Total.ToString("N0")+" 次";
                        TextAt(g,label,small,t.Text,new RectangleF(w-121,y,110,20));
                        string detail=row.Detail;if(page==6)detail=row.Name+" · 击键 "+row.Values[0].ToString("N0")+" / 点击 "+row.Values[1].ToString("N0")+" / 滚轮 "+row.Values[2].ToString("N0");
                        targets.Add(new KeyValuePair<RectangleF,string>(new RectangleF(14,y,w-28,18),detail));
                    }
                    if(count==0)TextAt(g,"暂无可用记录",body,t.Muted,new RectangleF(18,154,w-36,28));
                }
                if(hover>=targets.Count)hover=-1;
                if(hover>=0)using(Pen pen=new Pen(t.Accent,1.4f)){RectangleF r=targets[hover].Key;g.DrawRectangle(pen,r.X-1,r.Y-1,r.Width+2,r.Height+2);}
                if(page==7&&Data.SeriesNames!=null){for(int i=0;i<Data.SeriesNames.Length;i++){float x=14+(i%4)*(w-28)/4,y=240+(i/4)*20;Fill(g,colors[i%colors.Length],new RectangleF(x,y+4,8,8));TextAt(g,Data.SeriesNames[i],small,t.Muted,new RectangleF(x+14,y,(w-28)/4-18,19));}TextAt(g,hover>=0?targets[hover].Value:Data.Footer,small,t.Muted,new RectangleF(14,285,w-28,24));}
                else TextAt(g,hover>=0?targets[hover].Value:Data.Footer,small,t.Muted,new RectangleF(14,251,w-28,24));
            }
        }
    }
}

