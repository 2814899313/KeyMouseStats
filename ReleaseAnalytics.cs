using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KeyMouseStats
{
    internal sealed class CrossDay
    {
        public readonly double[] ObservedHours=new double[24];
        public readonly Dictionary<string,double[]> AppHours=new Dictionary<string,double[]>(StringComparer.OrdinalIgnoreCase);
        public readonly double[] Direction=new double[4]; // left, right, up, down: absolute axis counts
        public double PathCounts,DragCounts;
        public long Packets,CalibratedPackets,Flicks,Reversals,Clicks,MoveClicks;

        /// <summary>合并另一份同日交叉观测(多机合并的「相加」策略)。</summary>
        public void AddFrom(CrossDay other)
        {
            if (other == null) return;
            for (int i = 0; i < 24; i++) ObservedHours[i] += other.ObservedHours[i];
            foreach (KeyValuePair<string, double[]> app in other.AppHours)
            {
                double[] hours;
                if (!AppHours.TryGetValue(app.Key, out hours)) { hours = new double[24]; AppHours[app.Key] = hours; }
                for (int i = 0; i < 24; i++) hours[i] += app.Value[i];
            }
            for (int i = 0; i < 4; i++) Direction[i] += other.Direction[i];
            PathCounts += other.PathCounts; DragCounts += other.DragCounts;
            Packets += other.Packets; CalibratedPackets += other.CalibratedPackets;
            Flicks += other.Flicks; Reversals += other.Reversals;
            Clicks += other.Clicks; MoveClicks += other.MoveClicks;
        }
    }
    internal static class CrossTelemetry
    {
        public const string Other="[其他应用汇总]";
        public static void Interval(DayRecord day,ActiveSession session,string path,DateTime start,DateTime end)
        {
            double seconds=(end-start).TotalSeconds;if(seconds<=0)return;
            session.CrossObservedSeconds+=seconds;
            for(DateTime cursor=start;cursor<end;)
            {
                DateTime next=cursor.Date.AddHours(cursor.Hour+1);if(next>end)next=end;
                double part=(next-cursor).TotalSeconds;day.Cross.ObservedHours[cursor.Hour]+=part;
                if(!string.IsNullOrEmpty(path))
                {
                    string key=day.Cross.AppHours.ContainsKey(path)||day.Cross.AppHours.Count<128?path:Other;
                    double[] hours;if(!day.Cross.AppHours.TryGetValue(key,out hours)){hours=new double[24];day.Cross.AppHours[key]=hours;}hours[cursor.Hour]+=part;
                }
                cursor=next;
            }
            if(!string.IsNullOrEmpty(path))
            {
                string key=session.AppSeconds.ContainsKey(path)||session.AppSeconds.Count<32?path:Other;
                double value;session.AppSeconds.TryGetValue(key,out value);session.AppSeconds[key]=value+seconds;
            }
        }
        public static void Save(StringBuilder sb,DayRecord day)
        {
            sb.AppendLine("cross_hours_v1="+ActivityMonitor.EncodeHours(day.Cross.ObservedHours));
            foreach(KeyValuePair<string,double[]> app in day.Cross.AppHours)sb.AppendLine("app_hours_v1="+AppActivity.Encode(app.Key)+"|"+ActivityMonitor.EncodeHours(app.Value));
            foreach(ActiveSession session in day.Sessions)
            {
                sb.AppendLine("session_coverage_v1="+session.Start.Ticks+"|"+session.CrossObservedSeconds.ToString("R",CultureInfo.InvariantCulture));
                foreach(KeyValuePair<string,double> app in session.AppSeconds)sb.AppendLine("session_app_v1="+session.Start.Ticks+"|"+AppActivity.Encode(app.Key)+"|"+app.Value.ToString("R",CultureInfo.InvariantCulture));
            }
            CrossDay c=day.Cross;
            double[] values={c.Direction[0],c.Direction[1],c.Direction[2],c.Direction[3],c.PathCounts,c.DragCounts,c.Packets,c.CalibratedPackets,c.Flicks,c.Reversals,c.Clicks,c.MoveClicks};
            string[] fields=new string[values.Length];for(int i=0;i<values.Length;i++)fields[i]=values[i].ToString("R",CultureInfo.InvariantCulture);
            sb.AppendLine("mouse_vector_v1="+string.Join("|",fields));
        }
        public static void Load(DayRecord day,string key,string value)
        {
            try
            {
                string[] fields=value.Split('|');
                if(key=="cross_hours_v1")ActivityMonitor.LoadHours(day.Cross.ObservedHours,value);
                else if(key=="app_hours_v1"&&fields.Length==2)
                {
                    string path=AppActivity.Decode(fields[0]);if(day.Cross.AppHours.Count>=129&&!day.Cross.AppHours.ContainsKey(path))return;
                    double[] hours=new double[24];ActivityMonitor.LoadHours(hours,fields[1]);day.Cross.AppHours[path]=hours;
                }
                else if((key=="session_app_v1"&&fields.Length==3)||(key=="session_coverage_v1"&&fields.Length==2))
                {
                    long ticks;if(!long.TryParse(fields[0],out ticks))return;
                    foreach(ActiveSession session in day.Sessions)if(session.Start.Ticks==ticks)
                    {
                        if(fields.Length==2)session.CrossObservedSeconds=ActivityMonitor.ParseSeconds(fields[1]);
                        else {string path=AppActivity.Decode(fields[1]);if(session.AppSeconds.Count<33||session.AppSeconds.ContainsKey(path))session.AppSeconds[path]=ActivityMonitor.ParseSeconds(fields[2]);}
                        break;
                    }
                }
                else if(key=="mouse_vector_v1"&&fields.Length==12)
                {
                    double[] v=new double[12];for(int i=0;i<12;i++)v[i]=ActivityMonitor.ParseSeconds(fields[i]);
                    CrossDay c=day.Cross;Array.Copy(v,c.Direction,4);c.PathCounts=v[4];c.DragCounts=Math.Min(v[5],v[4]);
                    c.Packets=Count(v[6]);c.CalibratedPackets=Math.Min(c.Packets,Count(v[7]));c.Flicks=Count(v[8]);c.Reversals=Count(v[9]);c.Clicks=Count(v[10]);c.MoveClicks=Math.Min(c.Clicks,Count(v[11]));
                }
            }
            catch(FormatException) { }
        }
        private static long Count(double n){return n>=long.MaxValue?long.MaxValue:(long)n;}
    }
    // One bounded state per physical device, 50 ms vectors; no event history retained.
    internal sealed class MouseVectorTracker
    {
        private sealed class Motion {public long Start,Last,PreviousEnd,FlickAt=-1000,TurnAt=-1000;public double X,Y,Distance,PreviousX,PreviousY,Dpi;}
        private readonly Dictionary<long,Motion> devices=new Dictionary<long,Motion>();
        private readonly HashSet<int> held=new HashSet<int>();
        private DateTime date;private long lastMove=-10000;
        public void Reset(){devices.Clear();held.Clear();lastMove=-10000;date=DateTime.MinValue;}
        private void Day(DayRecord day){if(date!=day.Date){Reset();date=day.Date;}}
        public void Button(DayRecord day,long now,int button,bool down)
        {
            Day(day);if(!down){held.Remove(button);return;}held.Add(button);
            day.Cross.Clicks++;if(now>=lastMove&&now-lastMove<=200)day.Cross.MoveClicks++;
        }
        public void Move(DayRecord day,long now,long device,int dx,int dy,double dpi)
        {
            Day(day);if(dx==0&&dy==0)return;
            CrossDay c=day.Cross;double x=dx,y=dy,distance=Math.Sqrt(x*x+y*y);
            c.Direction[x<0?0:1]+=Math.Abs(x);c.Direction[y<0?2:3]+=Math.Abs(y);c.PathCounts+=distance;c.Packets++;
            if(held.Count>0)c.DragCounts+=distance;lastMove=now;
            if(!MouseDistance.ValidDpi(dpi)){devices.Remove(device);return;}c.CalibratedPackets++;
            Motion motion;
            if(!devices.TryGetValue(device,out motion))
            {if(devices.Count>=16)devices.Clear();motion=new Motion{Start=now,Last=now,Dpi=dpi};devices[device]=motion;}
            if(now<motion.Last||now-motion.Last>250||motion.Dpi!=dpi){motion=new Motion{Start=now,Last=now,Dpi=dpi};devices[device]=motion;}
            motion.Last=now;motion.X+=x;motion.Y+=y;motion.Distance+=distance;
            long elapsed=now-motion.Start;if(elapsed<50)return;
            double mm=MouseDistance.ToMeters(motion.Distance,dpi)*1000;
            if(elapsed<=80&&mm>=8&&now-motion.FlickAt>=200){c.Flicks++;motion.FlickAt=now;}
            double length=Math.Sqrt(motion.X*motion.X+motion.Y*motion.Y),previous=Math.Sqrt(motion.PreviousX*motion.PreviousX+motion.PreviousY*motion.PreviousY);
            if(elapsed<=80&&now-motion.PreviousEnd<=120&&MouseDistance.ToMeters(length,dpi)*1000>=2&&MouseDistance.ToMeters(previous,dpi)*1000>=2
                &&(motion.X*motion.PreviousX+motion.Y*motion.PreviousY)<=-.5*length*previous&&now-motion.TurnAt>=150)
            {c.Reversals++;motion.TurnAt=now;}
            motion.PreviousX=motion.X;motion.PreviousY=motion.Y;motion.PreviousEnd=now;
            motion.Start=now;motion.X=motion.Y=motion.Distance=0;
        }
    }
    internal static class KeySemantics
    {
        public static readonly string[] Names={"移动类键位","数字 / 技能栏","交互类键位","功能 / 修饰键","文字及其他","未归位记录"};
        public static int Group(int vk)
        {
            if(vk==65||vk==68||vk==83||vk==87||vk>=37&&vk<=40)return 0;
            if(vk>=48&&vk<=57||vk>=96&&vk<=105)return 1;
            if(vk==32||vk==16||vk==160||vk==161)return 2;
            if(vk==17||vk==18||vk>=162&&vk<=165||vk==91||vk==92||vk>=112&&vk<=135||vk==9||vk==27)return 3;
            return 4;
        }
        public static long[] Count(DayRecord day)
        {
            long[] counts=new long[6];if(day==null)return counts;long sum=0;
            foreach(KeyValuePair<int,long> key in day.KeyCounts){counts[Group(key.Key)]+=key.Value;sum+=key.Value;}
            counts[5]=Math.Max(0,day.Keys-sum);return counts;
        }
    }
    internal sealed class DailySignal
    {
        public bool High;public int Samples;public double Z=double.NaN,Delta7=double.NaN;
        private static readonly Dictionary<DateTime,DailySignal> Cache=new Dictionary<DateTime,DailySignal>();
        private static DateTime expires;
        public static void Clear(){Cache.Clear();expires=DateTime.UtcNow.AddSeconds(60);}
        public static DailySignal Get(DateTime date)
        {
            if(DateTime.UtcNow>=expires)Clear();DailySignal signal;if(Cache.TryGetValue(date.Date,out signal))return signal;
            signal=Calculate(date);if(Cache.Count<400)Cache[date.Date]=signal;return signal;
        }
        internal static DailySignal Calculate(DateTime date)
        {
            DailySignal signal=new DailySignal();DayRecord day=Analysis.GetDay(date);if(day==null||day.IsEmpty)return signal;
            List<double> baseline=PersonalStats.History(date.AddDays(-1),30,delegate(DayRecord d){return d.Keys;});signal.Samples=baseline.Count;
            signal.Z=PersonalStats.Ratio(day.Keys-PersonalStats.Mean(baseline),PersonalStats.Sd(baseline));
            signal.High=date.Date<DateTime.Today&&baseline.Count>=14&&signal.Z>=2;
            List<double> seven=PersonalStats.History(date.AddDays(-1),7,delegate(DayRecord d){return d.Keys;});
            if(seven.Count==7)signal.Delta7=PersonalStats.Ratio(day.Keys-PersonalStats.Mean(seven),PersonalStats.Mean(seven))*100;
            return signal;
        }
        public string Note {get{return (High?"异常高值 · ":"")+(double.IsNaN(Delta7)?"7日基准不足或为0":"较前7日均值 "+PersonalStats.Number(Delta7,"+0.0;-0.0;0")+"%");}}
    }
}


