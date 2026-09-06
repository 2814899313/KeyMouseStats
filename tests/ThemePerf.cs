using System;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;
internal static class ThemePerf {
 [STAThread] static void Main(string[] options) {
  Store.History.Clear();Store.RollDay(DateTime.Today);Store.ThemeId=options.Length>0?int.Parse(options[0]):5;Store.KeyboardLayout=1;
  Store.Today.Keys=10000;for(int k=32;k<91;k++)Store.Today.KeyCounts[k]=100+k;
  BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
  using(Dashboard form=new Dashboard()) {
   typeof(Dashboard).GetMethod("OnLoad",flags).Invoke(form,new object[]{EventArgs.Empty});
   FieldInfo tab=typeof(Dashboard).GetField("_tab",flags);tab.SetValue(form,Enum.ToObject(tab.FieldType,3));
   typeof(Dashboard).GetField("_showKeyboardHeatmap",flags).SetValue(form,true);
   foreach(float scale in new[]{1f,1.5f}) {
    typeof(Dashboard).GetField("_s",flags).SetValue(form,scale);
    using(Bitmap bmp=new Bitmap((int)(1072*scale),(int)(724*scale))) using(Graphics g=Graphics.FromImage(bmp)) {
     MethodInfo paint=typeof(Dashboard).GetMethod("OnPaint",flags);object[] args={new PaintEventArgs(g,new Rectangle(Point.Empty,bmp.Size))};
     paint.Invoke(form,args);Stopwatch sw=Stopwatch.StartNew();
     for(int i=0;i<30;i++){g.ResetTransform();paint.Invoke(form,args);}sw.Stop();
     Console.WriteLine("Keyboard "+scale+"x: "+(sw.Elapsed.TotalMilliseconds/30).ToString("0.00")+" ms/frame");
    }
   }
  }
 }
}
