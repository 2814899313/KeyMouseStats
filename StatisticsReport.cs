using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal static class PersonalStats
    {
        public static double Ratio(double numerator, double denominator)
        { return denominator > 0 ? numerator / denominator : double.NaN; }
        // 均值 / 分位数 / 标准差统一委托给 RangeMath,口径只保留一份实现。
        public static double Mean(List<double> values)
        { return RangeMath.Mean(values); }
        public static double Quantile(List<double> values, double p)
        { return RangeMath.Quantile(values, p); }
        public static double Sd(List<double> values)
        { return RangeMath.Sd(values); }
        public static long Combos(DayRecord day)
        { long n = 0; foreach (long v in day.ComboCounts.Values) n += v; return n; }
        public static double Metric(DayRecord day, int metric)
        {
            switch (metric)
            {
                case 0: return Ratio(day.Keys, day.Clicks);
                case 1: return Ratio(day.Keys, day.ActiveSeconds);
                case 2: return Ratio(day.Clicks * 60.0, day.ActiveSeconds);
                case 3: return MouseDistance.ValidDpi(Store.MouseDpi) ? Ratio(day.MoveMeters, (double)day.Keys + day.Clicks) : double.NaN;
                case 4: return Ratio(day.Wheel * 60.0, day.ActiveSeconds);
                default: return Ratio(Combos(day) * 100.0, day.Keys);
            }
        }
        public static List<double> History(DateTime end, int days, Func<DayRecord, double> selector)
        {
            List<double> result = new List<double>();
            for (int i = 0; i < days; i++)
            {
                DayRecord day = Analysis.GetDay(end.Date.AddDays(-i));
                if (day == null || day.IsEmpty) continue;
                double value = selector(day);
                if (!double.IsNaN(value) && !double.IsInfinity(value)) result.Add(value);
            }
            return result;
        }
        public static double MovingAverage(DateTime date, int metric)
        {
            List<double> values = History(date, 7, delegate(DayRecord d) { return metric == 0 ? d.Keys : metric == 1 ? d.Clicks : metric == 2 ? d.Wheel : d.MoveMeters; });
            return values.Count == 7 ? Mean(values) : double.NaN;
        }
        public static string Number(double value, string format)
        { return double.IsNaN(value) || double.IsInfinity(value) ? "--" : value.ToString(format, CultureInfo.InvariantCulture); }
        public static string Change(double value, double baseline)
        { return double.IsNaN(value) || double.IsNaN(baseline) ? "--" : baseline == 0 ? (value == 0 ? "持平" : "基准为 0") : Number((value / baseline - 1) * 100, "+0.0;-0.0;0") + "%"; }
    }

    internal sealed class ReportList : ListView
    {
        internal int Hovered=-1;
        private readonly ToolTip rowTip=new ToolTip{InitialDelay=300,ReshowDelay=100,AutoPopDelay=20000};
        private readonly ImageList spacing=new ImageList { ImageSize=new Size(1,34) };
        internal void SetScale(float scale){int height=Math.Min(256,(int)(34*scale));if(spacing.ImageSize.Height!=height)spacing.ImageSize=new Size(1,height);}
        public ReportList()
        {
            DoubleBuffered=true;SmallImageList=spacing;
            MouseMove+=delegate(object sender,MouseEventArgs e){ListViewItem item=GetItemAt(e.X,e.Y);int next=item==null?-1:item.Index;if(next!=Hovered){int old=Hovered;Hovered=next;rowTip.SetToolTip(this,item==null?"":item.ToolTipText);if(old>=0&&old<Items.Count)Invalidate(Items[old].Bounds);if(next>=0)Invalidate(Items[next].Bounds);}Cursor=item==null?Cursors.Default:Cursors.Hand;};
            MouseLeave+=delegate{int old=Hovered;Hovered=-1;if(old>=0&&old<Items.Count)Invalidate(Items[old].Bounds);Cursor=Cursors.Default;};
            ItemActivate+=delegate{if(SelectedItems.Count>0){ListViewItem item=SelectedItems[0];List<string> lines=new List<string>();for(int i=0;i<item.SubItems.Count;i++)lines.Add(Columns[i].Text+"："+item.SubItems[i].Text);ThemeMessage.Show(FindForm(),string.Join(Environment.NewLine,lines.ToArray()),"指标详情 · "+item.Text);}};
        }
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing){spacing.Dispose();rowTip.Dispose();}}
    }

    internal sealed partial class ReportVisual : Control
    {
        private readonly DateTime date;
        private readonly int page;
        private readonly double keys,active,clicks,combos;
        private readonly double[] values=new double[6],medians=new double[6],highs=new double[6];
        private readonly double[] daily=new double[30],averages=new double[30];
        private readonly List<KeyValuePair<string,double>> apps=new List<KeyValuePair<string,double>>();
        private readonly List<ActiveSession> sessions=new List<ActiveSession>();
        private readonly int observed;
        private readonly bool hasDay,hasAppTime;
        private readonly double priorMean;
        public ReportVisual(DateTime selected,int index)
        {
            date=selected;page=index;DoubleBuffered=true;ResizeRedraw=true;
            DayRecord day=Analysis.GetDay(date);hasDay=day!=null&&!day.IsEmpty;
            if(day!=null)
            {
                keys=day.Keys;clicks=day.Clicks;active=day.ActiveSeconds;combos=PersonalStats.Combos(day);hasAppTime=day.AppObservedSeconds>0;
                Dictionary<string,double> sums=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
                foreach(AppUsage app in day.Apps.Values){double v;sums.TryGetValue(app.ProcessPath,out v);sums[app.ProcessPath]=v+app.ActiveSeconds;}
                foreach(KeyValuePair<string,double> pair in sums)apps.Add(new KeyValuePair<string,double>(string.IsNullOrEmpty(pair.Key)?"未识别应用":Path.GetFileName(pair.Key),pair.Value));
                apps.Sort(delegate(KeyValuePair<string,double> a,KeyValuePair<string,double> b){return b.Value.CompareTo(a.Value);});
                foreach(ActiveSession session in day.Sessions)if(session.Seconds>0)sessions.Add(new ActiveSession{Start=session.Start,End=session.End,Seconds=session.Seconds});
            }
            List<double> prior=PersonalStats.History(date.AddDays(-1),30,delegate(DayRecord d){return d.Keys;});observed=prior.Count;priorMean=PersonalStats.Mean(prior);
            for(int i=0;i<6;i++)
            {
                int metric=i;values[i]=day==null?double.NaN:PersonalStats.Metric(day,i);
                List<double> history=PersonalStats.History(date.AddDays(-1),30,delegate(DayRecord d){return PersonalStats.Metric(d,metric);});
                medians[i]=history.Count>=7?PersonalStats.Quantile(history,.5):double.NaN;highs[i]=history.Count>=7?PersonalStats.Quantile(history,.9):double.NaN;
            }
            for(int i=0;i<30;i++){DateTime d=date.AddDays(i-29);DayRecord record=Analysis.GetDay(d);daily[i]=record==null||record.IsEmpty?double.NaN:record.Keys;averages[i]=PersonalStats.MovingAverage(d,0);}
        }
        private static bool Valid(double n){return !double.IsNaN(n)&&!double.IsInfinity(n);}
        private static string MetricText(double n){return PersonalStats.Number(n,Math.Abs(n)<.1?"0.####":"0.##");}
        private static void TextAt(Graphics g,string text,Font font,Color color,float x,float y,float width)
        {
            using(SolidBrush brush=new SolidBrush(color))using(StringFormat format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})
                g.DrawString(text,font,brush,new RectangleF(x,y,Math.Max(1,width),font.Height+6),format);
        }
        private static void Fill(Graphics g,Color color,float x,float y,float w,float h)
        {if(w>0&&h>0)using(SolidBrush brush=new SolidBrush(color))g.FillRectangle(brush,x,y,w,h);}
        internal void RenderTo(Graphics g,float scale)
        {
            float previous=UiScale;UiScale=scale;
            try{using(PaintEventArgs args=new PaintEventArgs(g,new Rectangle(0,0,Math.Max(1,(int)(ClientSize.Width*scale)),Math.Max(1,(int)(ClientSize.Height*scale)))))OnPaint(args);}
            finally{UiScale=previous;}
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;float scale=UiScale>0?UiScale:g.DpiX/96f;g.ScaleTransform(scale,scale);float width=ClientSize.Width/scale;
            ArtTheme t=ArtTheme.Current;g.Clear(t.Background);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if(page==0){PaintHabits(g,width);return;}
            using(Font small=new Font("Microsoft YaHei UI",11,FontStyle.Regular,GraphicsUnit.Pixel))
            using(Font body=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel))
            using(Font number=new Font("Segoe UI",25,FontStyle.Bold,GraphicsUnit.Pixel))
            {
                string[] names={"键盘击键","有效使用","较前 30 日均值","历史覆盖"};
                string[] data={hasDay?Analysis.FmtCount((long)keys):"--",hasDay?ActivityMonitor.FormatDuration(active):"--",hasDay&&observed>=7?PersonalStats.Change(keys,priorMean):"基准不足",observed+" / 30 天"};
                if(page==2)
                {
                    double sum=0;foreach(KeyValuePair<string,double> app in apps)sum+=app.Value;
                    names=new[]{"有效使用","使用最多的应用","已归因占比","记录应用数"};
                    data=new[]{hasDay?ActivityMonitor.FormatDuration(active):"--",hasAppTime&&apps.Count>0&&apps[0].Value>0?apps[0].Key:"--",hasAppTime&&active>0?Math.Min(100,sum/active*100).ToString("0.#")+"%":"--",apps.Count.ToString()};
                }
                if(page==3)
                {
                    List<double> lengths=new List<double>();foreach(ActiveSession session in sessions)lengths.Add(session.Seconds);
                    names=new[]{"连续使用段","平均段长","最长一段","段长中位数"};
                    data=new[]{sessions.Count.ToString(),sessions.Count>0?ActivityMonitor.FormatDuration(PersonalStats.Mean(lengths)):"--",sessions.Count>0?ActivityMonitor.FormatDuration(PersonalStats.Quantile(lengths,1)):"--",sessions.Count>0?ActivityMonitor.FormatDuration(PersonalStats.Quantile(lengths,.5)):"--"};
                }
                if(page==4){names=new[]{"当前 APM","当前连续使用","今日峰值 APM","历史峰值 APM"};data=new[]{LiveRate.Apm.ToString(),ActivityMonitor.CurrentSession==null?"--":ActivityMonitor.FormatDuration(ActivityMonitor.CurrentSession.Seconds),Store.Today.PeakApm.ToString(),Store.AllTimePeakApm.ToString()};}
                float card=(width-44)/4;
                for(int i=0;i<4;i++)
                {
                    float x=10+i*(card+8);ReportDesign.Surface(g,new RectangleF(x,10,card,72),t.Card,t.Line);
                    TextAt(g,names[i],small,t.Muted,x+12,18,card-24);TextAt(g,data[i],data[i].Length>11?body:number,t.Text,x+12,39,card-24);
                }
                string[] titles={"操作习惯 · 当前值与个人基准","30 日击键走势 · 日记录 + 7 日移动均值","应用活跃时长 · Top 5","一天的连续使用 · 24 小时时间轴","最近 5 分钟 · APM 微趋势"};
                TextAt(g,titles[page],body,t.Text,14,94,width-28);
                if(page==1)
                {
                    double max=1;foreach(double v in daily)if(Valid(v))max=Math.Max(max,v);foreach(double v in averages)if(Valid(v))max=Math.Max(max,v);
                    float left=62,top=132,bottom=231,step=(width-left-20)/30;
                    TextAt(g,Analysis.FmtCount((long)max),small,t.Muted,12,124,48);TextAt(g,"0",small,t.Muted,30,220,28);
                    PointF last=PointF.Empty;bool previous=false;
                    using(Pen line=new Pen(t.Orange,2))for(int i=0;i<30;i++)
                    {
                        float x=left+i*step;
                        if(Valid(daily[i]))Fill(g,i==29?t.Accent:ArtTheme.Mix(t.Card,t.Accent,.52),x,bottom-(float)(daily[i]/max*(bottom-top)),step-3,(float)(daily[i]/max*(bottom-top)));
                        else TextAt(g,"·",small,t.Muted,x,bottom-14,step);
                        if(Valid(averages[i])){PointF point=new PointF(x+(step-3)/2,bottom-(float)(averages[i]/max*(bottom-top)));if(previous)g.DrawLine(line,last,point);last=point;previous=true;}else previous=false;
                    }
                    TextAt(g,date.AddDays(-29).ToString("MM.dd"),small,t.Muted,left,234,80);TextAt(g,date.ToString("MM.dd"),small,t.Muted,width-65,234,60);
                    TextAt(g,"柱：每日击键  /  橙线：完整 7 日均值  /  ·：缺失记录"+(date==DateTime.Today?"  /  今日仍在累计":""),small,t.Muted,14,256,width-28);
                }
                else if(page==2)
                {
                    if(!hasAppTime||active<=0)TextAt(g,"尚无可用的应用活跃时长，击键与点击明细见下方。",body,t.Muted,16,150,width-32);
                    else
                    {
                        double sum=0;foreach(KeyValuePair<string,double> app in apps)sum+=app.Value;
                        for(int i=0;i<Math.Min(5,apps.Count);i++)
                        {
                            float y=123+i*24;double share=apps[i].Value/active;TextAt(g,(i+1)+"  "+apps[i].Key,small,t.Text,16,y,180);
                            Fill(g,t.Raised,204,y+4,width-445,10);Fill(g,i==0?t.Accent:t.Cyan,204,y+4,(width-445)*(float)Math.Min(1,share),10);
                            TextAt(g,ActivityMonitor.FormatDuration(apps[i].Value)+"  ·  "+(share*100).ToString("0.#")+"%",small,t.Text,width-225,y,210);
                        }
                        TextAt(g,"占全日活跃时间  /  已归因 "+Math.Min(100,sum/active*100).ToString("0.#")+"%  /  未归因 "+ActivityMonitor.FormatDuration(Math.Max(0,active-sum)),small,t.Muted,16,254,width-32);
                    }
                }
                else if(page==3)
                {
                    float left=18,track=width-36;Fill(g,t.Raised,left,152,track,26);
                    double total=0,longest=0;
                    foreach(ActiveSession session in sessions)
                    {
                        total+=session.Seconds;longest=Math.Max(longest,session.Seconds);
                        double start=Math.Max(0,(session.Start-date).TotalSeconds),end=Math.Min(86400,(session.End-date).TotalSeconds);
                        if(end>start)Fill(g,t.Accent,left+(float)(start/86400*track),152,Math.Max(1,(float)((end-start)/86400*track)),26);
                    }
                    for(int h=0;h<=24;h+=4)TextAt(g,h.ToString("00")+":00",small,t.Muted,left+(track-38)*h/24,183,45);
                    TextAt(g,sessions.Count==0?"暂无连续使用段":sessions.Count+" 段  ·  平均 "+ActivityMonitor.FormatDuration(total/sessions.Count)+"  ·  最长 "+ActivityMonitor.FormatDuration(longest),body,t.Text,18,218,width-36);
                    TextAt(g,"色块表示连续活跃区间；空白可能是空闲、锁屏或未采集，不等同于休息。",small,t.Muted,18,254,width-36);
                }
                else
                {
                    long peak=0;bool sampled=false;foreach(long value in LiveRate.Trend())if(value>=0){sampled=true;peak=Math.Max(peak,value);}
                    TextAt(g,"窗口峰值 "+peak+" APM",small,t.Muted,16,120,width-32);
                    LiveRate.PaintTrend(g,new RectangleF(18,148,width-36,82),t.Cyan);
                    TextAt(g,"5 分钟前",small,t.Muted,18,234,110);TextAt(g,"现在",small,t.Muted,width-50,234,40);
                    TextAt(g,sampled?"每秒一个采样点 · 每点为最近 60 秒击键 + 点击；断线表示未观测。":"正在积累实时样本，曲线会随输入更新。",small,t.Muted,18,256,width-36);
                }
            }
        }
    }

    internal sealed partial class StatisticsReport : ThemedDialog
    {
        private static void Row(ListView list, params string[] fields)
        {
            string[] display=new string[fields.Length];for(int i=0;i<fields.Length;i++)display[i]=ReportDesign.Display(fields[i]);
            ListViewItem row=new ListViewItem(display);row.Tag=(string[])fields.Clone();row.ToolTipText=string.Join("  ·  ",fields);list.Items.Add(row);
        }
        private void PopulateRatios()
        {
            List<double>[] histories=new List<double>[6];bool baseline=false;
            for(int i=0;i<6;i++){int m=i;histories[i]=PersonalStats.History(_date.AddDays(-1),30,delegate(DayRecord d){return PersonalStats.Metric(d,m);});baseline|=histories[i].Count>=7;}
            ListView list=Page("输入习惯","个人基准仅使用所选日之前 30 天的记录，每项指标至少需要 7 个有效日。P50 是中位数；P90 表示 90% 的历史日不超过该值。\n分母为零时显示 —，不当作零值。鼠标距离受 DPI 设置影响；旧版长按计数会影响跨日比较。点击指标卡或明细行后可复制。",baseline?new[]{"指标","当前值","通常 · P50","较高 · P90","含义"}:new[]{"指标","当前值","含义"});
            DayRecord day=Analysis.GetDay(_date);
            for(int i=0;i<6;i++)
            {
                double factor=i==3?100:1,value=day==null?double.NaN:PersonalStats.Metric(day,i)*factor;
                string name=ReportDesign.Metrics[i]+"（"+ReportDesign.Units[i]+"）",raw=PersonalStats.Number(value,"R");
                if(baseline)Row(list,name,raw,histories[i].Count>=7?PersonalStats.Number(PersonalStats.Quantile(histories[i],.5)*factor,"R"):"—",histories[i].Count>=7?PersonalStats.Number(PersonalStats.Quantile(histories[i],.9)*factor,"R"):"—",ReportDesign.Definitions[i]);
                else Row(list,name,raw,ReportDesign.Definitions[i]);
            }
        }
        private void PopulateChanges()
        {
            ListView list = Page("变化与分布", "本页比较击键量。今天与完整历史日对比仅表示当前进度；周/月比较截止最近已结束的一天。\n工作日按周一至周五划分，不处理节假日；异常提示是描述性阈值，不是显著性检验。缺失日不补零。", "项目", "数值 / 变化", "基准 / 覆盖", "说明");
            DayRecord day = Analysis.GetDay(_date), lastWeek = Analysis.GetDay(_date.AddDays(-7));
            List<double> prior = PersonalStats.History(_date.AddDays(-1), 30, delegate(DayRecord d) { return d.Keys; });
            List<double> prior7 = PersonalStats.History(_date.AddDays(-1), 7, delegate(DayRecord d) { return d.Keys; });
            double keys = day == null || day.IsEmpty ? double.NaN : day.Keys;
            Row(list, "击键 vs 上周同日", PersonalStats.Change(keys, lastWeek == null || lastWeek.IsEmpty ? double.NaN : lastWeek.Keys), _date.AddDays(-7).ToString("MM-dd"), _date == DateTime.Today ? "今日累计，未结束" : "日记录对比");
            Row(list, "击键 vs 前 7 日均值", prior7.Count == 7 ? PersonalStats.Change(keys, PersonalStats.Mean(prior7)) : "样本不足", prior7.Count + "/7 天", "不把缺失天当作零");
            DateTime end = _date == DateTime.Today ? _date.AddDays(-1) : _date;
            foreach (int width in new int[] { 7, 30 })
            {
                List<double> current = PersonalStats.History(end, width, delegate(DayRecord d) { return d.Keys; });
                List<double> previous = PersonalStats.History(end.AddDays(-width), width, delegate(DayRecord d) { return d.Keys; });
                Row(list, width == 7 ? "周环比（滚动 7 日）" : "月环比（滚动 30 日）", current.Count == width && previous.Count == width ? PersonalStats.Change(PersonalStats.Mean(current), PersonalStats.Mean(previous)) : "覆盖不足",
                    current.Count + "/" + width + " 对 " + previous.Count + "/" + width, "截止 " + end.ToString("MM-dd"));
            }
            List<double> weekdays = new List<double>(), weekends = new List<double>();
            double sx = 0, sy = 0, sxx = 0, sxy = 0; int n = 0;
            for (int i = 0; i < 30; i++)
            {
                DayRecord d = Analysis.GetDay(end.AddDays(-i)); if (d == null || d.IsEmpty) continue;
                (d.Date.DayOfWeek == DayOfWeek.Saturday || d.Date.DayOfWeek == DayOfWeek.Sunday ? weekends : weekdays).Add(d.Keys);
                double x = 29 - i; sx += x; sy += d.Keys; sxx += x * x; sxy += x * d.Keys; n++;
            }
            Row(list, "工作日 / 周末日均击键", PersonalStats.Number(PersonalStats.Mean(weekdays), "0") + " / " + PersonalStats.Number(PersonalStats.Mean(weekends), "0"), weekdays.Count + " / " + weekends.Count + " 天", "近 30 个已结束日");
            Row(list, "30 日线性趋势斜率", n >= 7 ? PersonalStats.Number(PersonalStats.Ratio(n * sxy - sx * sy, n * sxx - sx * sx), "+0.0;-0.0;0") + " 次/天" : "样本不足", n + " 个观测日", "按实际日期间隔拟合");
            Row(list, "历史击键 P50 / P90", prior.Count >= 7 ? PersonalStats.Number(PersonalStats.Quantile(prior, .5), "0") + " / " + PersonalStats.Number(PersonalStats.Quantile(prior, .9), "0") : "样本不足", prior.Count + " 天", "不包含所选日期");
            double sd = PersonalStats.Sd(prior), z = PersonalStats.Ratio(keys - PersonalStats.Mean(prior), sd);
            Row(list, "所选日偏差提示", prior.Count < 14 || double.IsNaN(z) ? "基准不足或无波动" : z >= 2 ? "明显高于近期记录" : z <= -2 ? "明显低于近期记录" : "近期常见范围", PersonalStats.Number(z, "0.00") + " 个标准差", "至少 14 日；阈值 ±2，今日仍累计");
            // Scan each completed day against its own preceding baseline, never include the candidate in its baseline.
            for (int i = 0, shown = 0; i < 30 && shown < 5; i++)
            {
                DateTime date = end.AddDays(-i); DayRecord candidate = Analysis.GetDay(date); if (candidate == null || candidate.IsEmpty) continue;
                List<double> baseline = PersonalStats.History(date.AddDays(-1), 30, delegate(DayRecord d) { return d.Keys; });
                double score = PersonalStats.Ratio(candidate.Keys - PersonalStats.Mean(baseline), PersonalStats.Sd(baseline));
                if (baseline.Count >= 14 && score >= 2) { Row(list, "近期高值日 " + date.ToString("MM-dd"), candidate.Keys.ToString(), PersonalStats.Number(score, "0.00") + " 个标准差", "相对其前 30 天，最多列 5 天"); shown++; }
            }
        }
        private void PopulateApps()
        {
            ListView list = Page("应用交叉", "时长来自新版约每秒的前台采样，只归因连续两次观测到同一进程的活跃区间；快速切换可能漏记。\n时间占比以全日 ActiveSeconds 为分母，不能归因的部分单列；使用天数按此前 30 天有记录的日子计算。", "应用", "键鼠比", "活跃时长", "占全日活跃", "使用日/观测日", "击键 / 点击");
            Dictionary<string, AppUsage> apps = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase);
            DayRecord day = Analysis.GetDay(_date);
            if (day == null) return;
            foreach (AppUsage usage in day.Apps.Values)
            {
                AppUsage sum; if (!apps.TryGetValue(usage.ProcessPath, out sum)) { sum = new AppUsage { ProcessPath = usage.ProcessPath }; apps[usage.ProcessPath] = sum; }
                sum.Keys += usage.Keys; sum.Clicks += usage.Clicks; sum.ActiveSeconds += usage.ActiveSeconds;
            }
            List<AppUsage> sorted = new List<AppUsage>(apps.Values); sorted.Sort(delegate(AppUsage a, AppUsage b) { return b.ActiveSeconds.CompareTo(a.ActiveSeconds); });
            Dictionary<string, int> usedDays = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int observed = 0;
            for (int i = 0; i < 30; i++)
            {
                DayRecord d = Analysis.GetDay(_date.AddDays(-i)); if (d == null || d.Apps.Count == 0) continue;
                observed++; HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (AppUsage app in d.Apps.Values) paths.Add(app.ProcessPath);
                foreach (string path in paths) { int count; usedDays.TryGetValue(path, out count); usedDays[path] = count + 1; }
            }
            double attributed = 0;
            foreach (AppUsage app in sorted)
            {
                int used; usedDays.TryGetValue(app.ProcessPath, out used);
                attributed += app.ActiveSeconds;
                Row(list, app.AppName, PersonalStats.Number(PersonalStats.Ratio(app.Keys, app.Clicks), "0.##"), day.AppObservedSeconds > 0 ? ActivityMonitor.FormatDuration(app.ActiveSeconds) : "未采集",
                    day.AppObservedSeconds > 0 ? PersonalStats.Number(PersonalStats.Ratio(app.ActiveSeconds * 100, day.ActiveSeconds), "0.#") + "%" : "--", used + "/" + observed, app.Keys + " / " + app.Clicks);
            }
            Row(list, "活跃时间未归因", "--", ActivityMonitor.FormatDuration(Math.Max(0, day.ActiveSeconds - attributed)), PersonalStats.Number(PersonalStats.Ratio(Math.Max(0, day.ActiveSeconds - attributed) * 100, day.ActiveSeconds), "0.#") + "%", "旧记录 / 切换边界 / 识别失败", "");
            Row(list, "前台进程切换", day.AppObservedSeconds > 0 ? day.AppSwitches + " 次" : "未采集", "约每秒观测", "不计启动和空闲后的首次出现", "", "");
            Row(list, "切换频率", PersonalStats.Number(PersonalStats.Ratio(day.AppSwitches * 3600.0, day.AppObservedSeconds), "0.##") + " 次/活跃小时", "仅新版观测活跃时间", "", "", "");
        }
        private void PopulateSessions()
        {
            ListView list = Page("会话节奏", "会话是连续活跃区间，包含空闲阈值内停顿。变异系数 CV = 样本标准差 / 平均时长，越小仅表示长度越接近。\n平均段间空闲仅使用准确衔接前后会话的 IdlePeriods；锁屏和离线间隔不当作空闲。段内峰值从新版开始采集。", "指标", "数值", "覆盖 / 定义", "补充");
            DayRecord day = Analysis.GetDay(_date); if (day == null) return;
            List<double> lengths = new List<double>(), idle = new List<double>();
            foreach (ActiveSession s in day.Sessions) if (s.Seconds > 0) lengths.Add(s.Seconds);
            HashSet<DateTime> starts = new HashSet<DateTime>(), ends = new HashSet<DateTime>();
            foreach (ActiveSession s in day.Sessions) { starts.Add(s.Start); ends.Add(s.End); }
            foreach (ActiveSession s in day.IdlePeriods) if (starts.Contains(s.End) && ends.Contains(s.Start)) idle.Add(s.Seconds);
            Row(list, "会话数", lengths.Count.ToString(), "所选日", "");
            Row(list, "平均 / 最短 / 最长（分钟）", PersonalStats.Number(PersonalStats.Mean(lengths) / 60, "0.##") + " / " + PersonalStats.Number(PersonalStats.Quantile(lengths, 0) / 60, "0.##") + " / " + PersonalStats.Number(PersonalStats.Quantile(lengths, 1) / 60, "0.##"), lengths.Count + " 段", "");
            Row(list, "会话中位数 / P90（分钟）", PersonalStats.Number(PersonalStats.Quantile(lengths, .5) / 60, "0.##") + " / " + PersonalStats.Number(PersonalStats.Quantile(lengths, .9) / 60, "0.##"), "描述性分布", "");
            Row(list, "会话标准差（分钟）", PersonalStats.Number(PersonalStats.Sd(lengths) / 60, "0.##"), "至少 2 段", "");
            double sdMinutes = PersonalStats.Sd(lengths) / 60;
            Row(list, "会话方差（分钟²）", PersonalStats.Number(sdMinutes * sdMinutes, "0.##"), "样本方差，至少 2 段", "");
            Row(list, "会话长度 CV", lengths.Count >= 3 ? PersonalStats.Number(PersonalStats.Ratio(PersonalStats.Sd(lengths), PersonalStats.Mean(lengths)), "0.###") : "至少需要 3 段", "相对波动，不是评分", "");
            Row(list, "段间平均观测空闲", PersonalStats.Number(PersonalStats.Mean(idle) / 60, "0.##") + " 分钟", idle.Count + " 个完整衔接空档", "排除未定位的历史空闲");
            foreach (ActiveSession s in day.Sessions)
                Row(list, s.Start.ToString("HH:mm:ss") + "—" + s.End.ToString("HH:mm:ss"), s.RateMeasured ? "峰值 " + s.PeakApm + " APM" : "无段内速率记录",
                    s.RateMeasured ? "开始后 " + ActivityMonitor.FormatDuration(s.PeakOffsetSeconds) + " 达峰" : "旧数据不可还原",
                    s.RateMeasured ? "末次 " + s.LastApm + " APM；较峰值 " + PersonalStats.Change(s.LastApm, s.PeakApm) : "");
        }
        private void PopulateLive()
        {
            _live = Page("实时与峰值", "APM = 最近 60 秒去重击键 + 鼠标点击，不含滚轮；不足 60 秒不外推。\n当前会话速率仅包含该段内的事件。峰值从新版启用起保存，无法还原旧记录的历史峰值。此页每秒刷新。", "指标", "当前值", "口径");
            PopulateLiveRows();
        }
        private void PopulateLiveRows()
        {
            int selected=_live.SelectedIndices.Count>0?_live.SelectedIndices[0]:-1;
            _live.BeginUpdate(); _live.Items.Clear();
            Row(_live, "实时 APM", LiveRate.Apm.ToString(), "键 " + LiveRate.KeysPerMin + " + 点 " + LiveRate.ClicksPerMin);
            ActiveSession current = ActivityMonitor.CurrentSession;
            Row(_live, "当次连续活跃时长", current == null ? "当前无活跃段" : ActivityMonitor.FormatDuration(current.Seconds), "基于已积分活跃秒数");
            Row(_live, "今日峰值 APM", Store.Today.PeakApm.ToString(), "从新版开始记忆");
            Row(_live, "历史峰值 APM", Store.AllTimePeakApm.ToString(), "即使每日记录清理仍保留");
            if(selected>=0&&selected<_live.Items.Count)_live.Items[selected].Selected=true;
            _live.EndUpdate();
        }
        internal static string BuildCsv(ListView list)
        {
            StringBuilder csv=new StringBuilder();
            foreach(ColumnHeader column in list.Columns){if(csv.Length>0)csv.Append(',');csv.Append(AppActivity.CsvCell(column.Text));}csv.AppendLine();
            foreach(ListViewItem row in list.Items){string[] raw=row.Tag as string[];for(int i=0;i<row.SubItems.Count;i++){if(i>0)csv.Append(',');csv.Append(AppActivity.CsvCell(raw!=null?raw[i]:row.SubItems[i].Text));}csv.AppendLine();}
            return csv.ToString();
        }
        private void Export()
        {
            try
            {
                ListView list = null; foreach (Control control in _tabs.SelectedTab.Controls) if (control is ListView) list = (ListView)control;
                string csv=BuildCsv(list);
                DateTime reportDate = _tabs.SelectedIndex == 4 ? DateTime.Today : _date;
                using(SaveFileDialog dialog=new SaveFileDialog{Title="导出当前报告明细",Filter="CSV 文件 (*.csv)|*.csv",FileName="统计报告_"+reportDate.ToString("yyyyMMdd")+"_"+ReportDesign.Names[_tabs.SelectedIndex]+".csv",DefaultExt="csv",AddExtension=true})
                {if(dialog.ShowDialog(this)!=DialogResult.OK)return;File.WriteAllText(dialog.FileName,csv,new UTF8Encoding(true));_feedback.Text="当前页明细已导出";}

            }
            catch (Exception ex) { ThemeMessage.Show(this, ex.Message, "导出失败"); }
        }
        /// <summary>把当前页图表渲染成 scale 倍尺寸,供 PNG 导出使用。</summary>
        private void ExportImage()
        {
            try
            {
                ReportPage page = _tabs.SelectedTab;
                if (page == null || page.Chart == null || page.Chart.Width <= 0 || page.Chart.Height <= 0) return;
                DateTime reportDate = _tabs.SelectedIndex == 4 ? DateTime.Today : _date;
                using (Bitmap bitmap = RenderChart(page.Chart, 2f))
                using (SaveFileDialog dialog = new SaveFileDialog { Title = "导出当前页图表", Filter = "PNG 图片 (*.png)|*.png", FileName = "统计报告_" + reportDate.ToString("yyyyMMdd") + "_" + ReportDesign.Names[_tabs.SelectedIndex] + ".png", DefaultExt = "png", AddExtension = true })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    bitmap.Save(dialog.FileName, ImageFormat.Png);
                    _feedback.Text = "当前页图片已导出";
                }
            }
            catch (Exception ex) { ThemeMessage.Show(this, ex.Message, "导出失败"); }
        }

        private Bitmap RenderChart(Control chart, float scale)
        {
            Bitmap bitmap = new Bitmap(Math.Max(1, (int)(chart.Width * scale)), Math.Max(1, (int)(chart.Height * scale)));
            bitmap.SetResolution(96f * scale, 96f * scale);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                ReportVisual habit = chart as ReportVisual;
                CrossReportVisual cross = chart as CrossReportVisual;
                RangeReportVisual range = chart as RangeReportVisual;
                RhythmReportVisual rhythm = chart as RhythmReportVisual;
                DistributionReportVisual distribution = chart as DistributionReportVisual;
                HoldReportVisual hold = chart as HoldReportVisual;
                AppKeyReportVisual appKey = chart as AppKeyReportVisual;
                if (habit != null) habit.RenderTo(graphics, scale);
                else if (cross != null) cross.RenderTo(graphics, scale);
                else if (range != null) range.RenderTo(graphics, scale);
                else if (rhythm != null) rhythm.RenderTo(graphics, scale);
                else if (distribution != null) distribution.RenderTo(graphics, scale);
                else if (hold != null) hold.RenderTo(graphics, scale);
                else if (appKey != null) appKey.RenderTo(graphics, scale);
                else chart.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            }
            return bitmap;
        }

        /// <summary>把当前页导出为自包含 HTML:图表以 base64 内嵌,明细以表格呈现,离线可开、不含脚本。</summary>
        private void ExportHtml()
        {
            try
            {
                ReportPage page = _tabs.SelectedTab;
                if (page == null || page.Chart == null || page.Chart.Width <= 0 || page.Chart.Height <= 0) return;
                DateTime reportDate = _tabs.SelectedIndex == 4 ? DateTime.Today : _date;
                string title = ReportDesign.Names[Math.Max(0, _tabs.SelectedIndex)];
                string subtitle = _header.Date + " · " + _header.Status;

                string base64;
                using (Bitmap bitmap = RenderChart(page.Chart, 2f))
                using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    base64 = Convert.ToBase64String(stream.ToArray());
                }

                StringBuilder html = new StringBuilder();
                html.AppendLine("<!DOCTYPE html>");
                html.AppendLine("<html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
                html.AppendLine("<title>" + Escape("键鼠统计 · " + title) + "</title>");
                html.AppendLine("<style>body{background:#12161d;color:#e9edf6;font-family:\"Microsoft YaHei UI\",\"Segoe UI\",sans-serif;margin:24px auto;max-width:1100px;padding:0 16px}");
                html.AppendLine("h1{font-size:22px;margin:0 0 4px}p.sub{color:#a0abbc;margin:0 0 18px;font-size:13px}");
                html.AppendLine("img{max-width:100%;border-radius:8px;display:block;margin:0 0 20px}");
                html.AppendLine("table{border-collapse:collapse;width:100%;font-size:13px}th,td{border-bottom:1px solid #303f54;padding:7px 10px;text-align:left}");
                html.AppendLine("th{color:#a0abbc;font-weight:600}tr:nth-child(even) td{background:#18202e}");
                html.AppendLine("p.note{color:#a0abbc;font-size:12px;margin-top:20px}</style></head><body>");
                html.AppendLine("<h1>" + Escape(title) + "</h1>");
                html.AppendLine("<p class=\"sub\">" + Escape(subtitle) + "</p>");
                html.AppendLine("<img alt=\"" + Escape(title) + "\" src=\"data:image/png;base64," + base64 + "\">");
                if (page.List != null && page.List.Columns.Count > 0)
                {
                    html.AppendLine("<table><thead><tr>");
                    foreach (ColumnHeader column in page.List.Columns) html.Append("<th>" + Escape(column.Text) + "</th>");
                    html.AppendLine("</tr></thead><tbody>");
                    foreach (ListViewItem row in page.List.Items)
                    {
                        html.Append("<tr>");
                        for (int i = 0; i < page.List.Columns.Count; i++)
                            html.Append("<td>" + Escape(i < row.SubItems.Count ? row.SubItems[i].Text : "") + "</td>");
                        html.AppendLine("</tr>");
                    }
                    html.AppendLine("</tbody></table>");
                }
                html.AppendLine("<p class=\"note\">本文件由键鼠统计导出,图片与数据均已内嵌,不联网、不含脚本。数据只来自本机记录,缺失日不补零。</p>");
                html.AppendLine("</body></html>");

                using (SaveFileDialog dialog = new SaveFileDialog { Title = "导出为网页", Filter = "网页 (*.html)|*.html", FileName = "键鼠统计_" + reportDate.ToString("yyyyMMdd") + "_" + title + ".html", DefaultExt = "html", AddExtension = true })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    File.WriteAllText(dialog.FileName, html.ToString(), new UTF8Encoding(true));
                    _feedback.Text = "当前页已导出为网页";
                }
            }
            catch (Exception ex) { ThemeMessage.Show(this, ex.Message, "导出失败"); }
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}


