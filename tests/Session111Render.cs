using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;
internal static class Session111Render
{
 [STAThread]static void Main(){Store.History.Clear();Store.RollDay(DateTime.Today);Store.ThemeId=8;Store.IdleThresholdSeconds=300;DateTime start=DateTime.Today.AddHours(8);
 foreach(SessionStartReason reason in new[]{SessionStartReason.Legacy,SessionStartReason.Startup,SessionStartReason.Idle,SessionStartReason.ObservationGap,SessionStartReason.Lock,SessionStartReason.Suspend}){Store.Today.Sessions.Add(new ActiveSession{Start=start,End=start.AddSeconds(29.751),Seconds=29.751,StartReason=reason});Store.Today.ActiveSeconds+=29.751;start=start.AddMinutes(10);}
 using(SessionDetails dialog=new SessionDetails()){Handles(dialog);using(Bitmap bitmap=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save("tests/session-111.png");}FieldInfo f=typeof(SessionDetails).GetField("_list",BindingFlags.NonPublic|BindingFlags.Instance);ListView list=(ListView)f.GetValue(dialog);if(list.Columns.Count!=4||list.Items.Count!=6||!list.Items[0].ToolTipText.Contains("29.751"))throw new Exception("Session details missing");}Console.WriteLine("PASS: session reasons and exact duration tooltip rendered");}
 static void Handles(Control c){IntPtr h=c.Handle;foreach(Control child in c.Controls)Handles(child);}
}
