using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using KeyMouseStats;
internal static class Release100Tests
{
    private static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
    static void Near(double a,double b,string name){Check(Math.Abs(a-b)<.000001,name);}
    static DayRecord NewDay(){return new DayRecord{Date=DateTime.Today};}
    static void Main(){try{Run();}catch(Exception ex){Console.WriteLine(ex.GetType().Name+": "+ex.Message);Environment.ExitCode=1;}}
    static void Run()
    {
        DayRecord day=NewDay();ActiveSession session=new ActiveSession{Start=day.Date.AddHours(9).AddSeconds(3599),End=day.Date.AddHours(10).AddSeconds(1),Seconds=2};day.Sessions.Add(session);
        CrossTelemetry.Interval(day,session,"C:\\编辑器.exe",session.Start,session.End);
        Near(day.Cross.AppHours["C:\\编辑器.exe"][9],1,"hour split left");Near(day.Cross.AppHours["C:\\编辑器.exe"][10],1,"hour split right");Near(session.AppSeconds["C:\\编辑器.exe"],2,"session attribution");
        CrossTelemetry.Interval(day,session,null,session.End,session.End.AddSeconds(1));Near(session.CrossObservedSeconds,3,"unattributed coverage retained");Near(session.AppSeconds["C:\\编辑器.exe"],2,"unknown not assigned");
        for(int i=0;i<150;i++)CrossTelemetry.Interval(day,session,"app"+i,session.Start,session.Start.AddSeconds(.1));
        Check(day.Cross.AppHours.Count==129&&session.AppSeconds.Count==33,"bounded application state");Check(day.Cross.AppHours.ContainsKey(CrossTelemetry.Other)&&session.AppSeconds.ContainsKey(CrossTelemetry.Other),"overflow explicitly aggregated");
        // Actual integration pipeline splits midnight before writing a per-day cross table.
        Dictionary<DateTime,DayRecord> split=new Dictionary<DateTime,DayRecord>();
        ActivityTracker tracker=new ActivityTracker(delegate(DateTime date){DayRecord d;if(!split.TryGetValue(date,out d)){d=new DayRecord{Date=date};split[date]=d;}return d;});
        tracker.OnActiveInterval=delegate(DayRecord d,ActiveSession s,DateTime a,DateTime b){CrossTelemetry.Interval(d,s,"app",a,b);};
        DateTime midnight=DateTime.Today.AddDays(1);tracker.Sample(midnight.AddSeconds(-1),1000,0,60);tracker.Sample(midnight.AddSeconds(1),3000,0,60);
        Near(split[midnight.AddDays(-1)].Cross.AppHours["app"][23],1,"midnight previous date");Near(split[midnight].Cross.AppHours["app"][0],1,"midnight new date");
        MouseVectorTracker mouse=new MouseVectorTracker();DayRecord vector=NewDay();
        mouse.Move(vector,0,1,-3,4,0);Near(vector.Cross.Direction[0],3,"left component");Near(vector.Cross.Direction[3],4,"down component");Near(vector.Cross.PathCounts,5,"euclidean path distinct from axial total");Check(vector.Cross.Flicks==0&&vector.Cross.CalibratedPackets==0,"unset DPI excludes flick detection");
        mouse.Button(vector,100,1,true);mouse.Move(vector,110,1,0,-5,0);mouse.Button(vector,120,1,false);mouse.Button(vector,1000,2,true);mouse.Button(vector,1010,2,false);
        Near(vector.Cross.DragCounts,5,"held-button movement");Check(vector.Cross.Clicks==2&&vector.Cross.MoveClicks==1,"200ms click coupling");
        mouse.Reset();vector=NewDay();mouse.Move(vector,0,1,500,0,1000);mouse.Move(vector,50,1,500,0,1000);Check(vector.Cross.Flicks==1,"50ms fast movement threshold");
        mouse.Move(vector,100,1,-500,0,1000);Check(vector.Cross.Reversals==1&&vector.Cross.Flicks==1,"reversal and flick cooldown");mouse.Move(vector,150,1,500,0,1000);Check(vector.Cross.Reversals==1,"turn cooldown");
        mouse.Move(vector,450,1,-500,0,1000);Check(vector.Cross.Reversals==1,"long pause no reversal");
        mouse.Reset();vector=NewDay();mouse.Move(vector,0,1,500,0,1000);mouse.Move(vector,50,1,500,0,1000);mouse.Move(vector,100,2,-500,0,1000);mouse.Move(vector,150,2,-500,0,1000);Check(vector.Cross.Reversals==0,"device headings isolated");
        mouse.Move(vector,200,1,-500,0,2000);Check(vector.Cross.Reversals==0,"DPI change resets heading");
        mouse.Reset();mouse.Button(vector,2000,1,true);mouse.Reset();mouse.Move(vector,2010,1,20,0,0);Near(vector.Cross.DragCounts,0,"reset clears held buttons");
        day.KeyCounts[87]=40;day.KeyCounts[162]=10;day.KeyCounts[49]=20;day.KeyCounts[32]=10;day.KeyCounts[67]=5;day.Keys=100;day.ComboCounts["ctrl_c"]=5;
        long[] semantic=KeySemantics.Count(day);long sum=0;foreach(long count in semantic)sum+=count;Check(sum==100&&semantic[0]==40&&semantic[1]==20&&semantic[2]==10&&semantic[3]==10&&semantic[4]==5&&semantic[5]==15,"semantic partition preserves denominator and unknowns");
        Store.History.Clear();Store.RollDay(DateTime.Today);for(int i=1;i<=40;i++)Store.History[DateTime.Today.AddDays(-i)]=new DayRecord{Date=DateTime.Today.AddDays(-i),Keys=100+i%5};
        Store.History[DateTime.Today.AddDays(-1)].Keys=5000;DailySignal signal=DailySignal.Calculate(DateTime.Today.AddDays(-1));Check(signal.High&&signal.Samples==30&&signal.Z>2&&signal.Delta7>0,"high day excludes itself from baseline");
        Store.Today.Keys=10000;Check(!DailySignal.Calculate(DateTime.Today).High,"partial today never anomalous");Store.History.Clear();Store.History[day.Date]=day;Check(!DailySignal.Calculate(day.Date.AddDays(-1)).High,"missing day not anomalous");
        // Full application persistence, isolated from real user data.
        string folder=Path.Combine(Path.GetTempPath(),"Release100-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string file=Path.Combine(folder,"stats.txt");
        typeof(Store).GetField("Dir",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,folder);typeof(Store).GetField("FilePath",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,file);
        try
        {
            Store.History.Clear();Store.RollDay(DateTime.Today);DayRecord saved=Store.Today;saved.Keys=120;saved.ActiveSeconds=2;
            ActiveSession stored=new ActiveSession{Start=day.Date.AddHours(9),End=day.Date.AddHours(9).AddSeconds(2),Seconds=2};saved.Sessions.Add(stored);CrossTelemetry.Interval(saved,stored,"C:\\应用|文档.exe",stored.Start,stored.End);
            mouse.Reset();mouse.Move(saved,0,3,-3,4,1600);mouse.Button(saved,100,1,true);mouse.Move(saved,110,3,0,5,1600);mouse.Button(saved,120,1,false);
            ShortcutSavings.MenuSeconds=2.25;ShortcutSavings.ShortcutSeconds=.75;ShortcutSavings.MenuClicks=3;Store.Save();ShortcutSavings.MenuSeconds=2;ShortcutSavings.ShortcutSeconds=.5;ShortcutSavings.MenuClicks=2;Store.History.Clear();Store.Load();saved=Store.Today;Check(ShortcutSavings.EncodeModel()=="2.25|0.75|3","savings model persistence");
            Check(saved.Keys==120,"old counters preserved");Near(saved.Cross.Direction[0],3,"vector persistence");Near(saved.Cross.DragCounts,5,"drag persistence");Check(saved.Cross.Clicks==1&&saved.Cross.MoveClicks==1,"click coupling persistence");
            Near(saved.Cross.AppHours["C:\\应用|文档.exe"][9],2,"hour app persistence with encoded delimiter");Near(saved.Sessions[0].AppSeconds["C:\\应用|文档.exe"],2,"session app full persistence");Near(saved.Sessions[0].CrossObservedSeconds,2,"session coverage persistence");
            File.WriteAllText(file,"date="+day.Date.ToString("yyyy-MM-dd")+"\n[day="+day.Date.ToString("yyyy-MM-dd")+"]\nkeys=321\nactive_seconds=60\n");Store.History.Clear();Store.Load();
            Check(Store.Today.Keys==321&&Store.Today.Cross.Packets==0&&Store.Today.Cross.AppHours.Count==0,"legacy data does not fabricate observations");
            CrossTelemetry.Load(Store.Today,"mouse_vector_v1","-1|NaN|Infinity|2|3|9|4|5|6|7|8|9");Near(Store.Today.Cross.Direction[0],0,"negative rejected");Near(Store.Today.Cross.Direction[1],0,"NaN rejected");Near(Store.Today.Cross.DragCounts,3,"drag bounded by total");Check(Store.Today.Cross.MoveClicks==8,"coupled clicks bounded");
            CrossTelemetry.Load(Store.Today,"app_hours_v1","not base64|abc");Check(Store.Today.Cross.AppHours.Count==0,"malformed line ignored");
        }
        finally{foreach(string name in new[]{"stats.txt","stats.txt.tmp","stats.txt.bak"})if(File.Exists(Path.Combine(folder,name)))File.Delete(Path.Combine(folder,name));Directory.Delete(folder);}
        MouseVectorTracker fast=new MouseVectorTracker();DayRecord performance=NewDay();System.Diagnostics.Stopwatch watch=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<100000;i++)fast.Move(performance,i/8,1,1,i%3-1,1600);
        watch.Stop();Check(performance.Cross.Packets==100000,"high polling preserves packet count");
        Console.WriteLine("100,000 vector packets: "+watch.Elapsed.TotalMilliseconds.ToString("0.0")+" ms (synthetic 8 kHz timestamps)");
        Check(typeof(CrossDay).Assembly.GetName().Version.ToString()=="1.3.3.0","release assembly version");
        Console.WriteLine("PASS: "+checks+" release 1.0.0 attribution / vector / semantics / anomaly / persistence checks");
    }
}


