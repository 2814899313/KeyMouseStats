using System;
using System.Reflection;
using System.Diagnostics;
using KeyMouseStats;
internal static class LiveRateTests
{
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);}
    static object Field(string name){return typeof(LiveRate).GetField(name,BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);}
    static void Main()
    {
        LiveRate.Reset();long start=(long)Field("_rateStart"),f=Stopwatch.Frequency;
        long[] keys=(long[])Field("_kb"),clicks=(long[])Field("_ms");keys[0]=100;clicks[0]=20;
        Check(LiveRate.Apm==120,"APM includes keys and clicks");
        Check(LiveRate.Trend()[0]==-1 && LiveRate.Trend()[299]==120,"startup unknown history");
        for(int i=1;i<=60;i++)LiveRate.TickAt(start+i*f);
        Check(LiveRate.Apm==0 && LiveRate.Trend()[298]==120,"events expire after 60 seconds, history retained");
        Check(LiveRate.Trend()[299]==0,"idle decays to zero");
        LiveRate.TickAt(start+80*f);
        Check(LiveRate.Trend()[298]==-1 && LiveRate.Trend()[299]==0,"sampling gap not fabricated");
        for(int i=81;i<=400;i++)LiveRate.TickAt(start+i*f);
        foreach(long n in LiveRate.Trend())Check(n==0,"five minute retention and idle samples");
        LiveRate.TickAt(start+1000*f);
        Check(LiveRate.Trend()[298]==-1 && LiveRate.Apm==0,"long pause resets unavailable history");
        LiveRate.Reset();Check(LiveRate.Trend()[298]==-1 && LiveRate.Apm==0,"reset clears rate and history");
        Console.WriteLine("PASS: APM rolling window, five-minute retention, gaps, reset");
    }
}
