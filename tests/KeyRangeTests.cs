using System;using System.Collections.Generic;using System.Drawing;using System.Reflection;using KeyMouseStats;
internal static class KeyRangeTests{
[STAThread]static void Main(){try{Run();}catch(Exception e){Console.WriteLine(e.Message);Environment.ExitCode=1;}}static void Run(){Store.History.Clear();Store.RollDay(DateTime.Today);for(int i=0;i<91;i++){DayRecord day=i==0?Store.Today:new DayRecord{Date=DateTime.Today.AddDays(-i)};day.Keys=i+1;day.Clicks=1;day.KeyCounts[65]=i+1;day.ComboCounts["ctrl_c"]=i+1;Store.History[day.Date]=day;}
BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;using(Dashboard form=new Dashboard()){
FieldInfo tab=typeof(Dashboard).GetField("_tab",f);tab.SetValue(form,Enum.Parse(tab.FieldType,"Keys"));int[] ids={70,76,77,71},counts={1,7,30,90};
for(int i=0;i<4;i++){typeof(Dashboard).GetMethod("ApplyChip",f).Invoke(form,new object[]{ids[i]});IEnumerable<DayRecord> days=(IEnumerable<DayRecord>)typeof(Dashboard).GetMethod("KeyRangeDays",f).Invoke(form,null);int count=0;long keys=0;foreach(DayRecord day in days){count++;keys+=day.Keys;}if(count!=counts[i]||keys!=counts[i]*(counts[i]+1)/2)throw new Exception("Rolling date boundary");if(Analysis.KeyRanking(days)[0].Value!=keys||ShortcutStats.Ranking(days)[0].Value!=keys)throw new Exception("Ranking range mismatch");string ratio=(string)typeof(Dashboard).GetMethod("MouseKeyRatio",f).Invoke(form,new object[]{days});if(ratio!=(keys/(double)count).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+" : 1")throw new Exception("Ratio range mismatch");}
}

Console.WriteLine("PASS: 1/7/30/90-day boundaries, key/combo rankings and selected-range ratio");}}
