using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KeyMouseStats;
internal static class KeyboardHeatTests
{
    static int checks;
    static void Check(bool ok,string name) { if(!ok) throw new Exception(name);checks++; }
    static void Main()
    {
        for(int layout=1;layout<=5;layout++)
        {
            List<HeatKey> keys=KeyboardHeat.Layout(layout);HashSet<int> ids=new HashSet<int>();
            Check(layout!=1||keys.Count==104,"104 keys");Check(layout!=2||keys.Count==87,"87 keys");Check(layout!=5||keys.Count==61,"61 keys");
            for(int i=0;i<keys.Count;i++)
            {
                Check(keys[i].Scan==0 || ids.Add(keys[i].Scan),"unique physical key");
                for(int j=i+1;j<keys.Count;j++) Check(!keys[i].Bounds.IntersectsWith(keys[j].Bounds),"key geometry does not overlap");
            }
        }
        Check(KeyboardDevices.Suggest(101)==0 && KeyboardDevices.Suggest(105)==0,"generic/ISO counts not guessed as ANSI");
        Check(KeyboardDevices.Suggest(87)==2 && KeyboardDevices.Suggest(61)==5,"known counts suggested");
        DayRecord day=new DayRecord();day.Keys=10;day.KeyCounts[13]=10;
        KeyboardHeat.Record(day,13,28,0);KeyboardHeat.Record(day,13,28,1);
        long legacy,total;Dictionary<int,long> aggregate=KeyboardHeat.Aggregate(new[]{day},out legacy,out total);
        Check(total==10 && legacy==8 && aggregate[28]==9 && aggregate[284]==1,"mixed history without double-counted Enter");
        Check(KeyboardHeat.ScanId(19,69,0)!=KeyboardHeat.ScanId(144,69,1),"Pause vs NumLock");
        ShortcutTracker tracker=new ShortcutTracker();bool first;
        tracker.Process(Native.WM_KEYDOWN,13,28,0,out first);Check(first,"main Enter");
        tracker.Process(Native.WM_KEYDOWN,13,28,1,out first);Check(first,"simultaneous numpad Enter");
        tracker.Process(Native.WM_KEYDOWN,13,28,1,out first);Check(!first,"numpad hold");
        tracker.Process(Native.WM_KEYUP,13,28,0);tracker.Process(Native.WM_KEYDOWN,13,28,1,out first);Check(!first,"other Enter release does not release held numpad");
        tracker.Reset();tracker.Process(Native.WM_KEYDOWN,97,79,0,out first);
        tracker.Process(Native.WM_KEYUP,35,79,0);tracker.Process(Native.WM_KEYDOWN,97,79,0,out first);Check(first,"NumLock changes VK during release");
        string dir=Path.Combine(Path.GetTempPath(),"KeyboardHeatTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        Store.DataDirectory = dir;
        try
        {
            Store.History.Clear();Store.Total=new Counters();Store.RollDay(DateTime.Today);
            Store.Today.Keys=10;Store.Today.KeyCounts[13]=10;KeyboardHeat.Record(Store.Today,13,28,1);Store.KeyboardLayout=4;
            Store.Save();Store.History.Clear();Store.Load();
            Check(Store.KeyboardLayout==4 && Store.Today.PhysicalKeys[284]==1 && Store.Today.PhysicalVks[13]==1,"physical and preference persistence");
            aggregate=KeyboardHeat.Aggregate(new[]{Store.Today},out legacy,out total);Check(total==10 && legacy==9,"round-trip aggregation");
        }
        finally { foreach(string file in Directory.GetFiles(dir)) File.Delete(file);Directory.Delete(dir); }
        Console.WriteLine("Keyboard heatmap: "+checks+" checks passed");
    }
}
