using System;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;
internal static class PopupLifetimeTests
{
    [STAThread] static void Main()
    {
        try { Run(); } catch(Exception e) { while(e.InnerException!=null)e=e.InnerException; Console.WriteLine(e.GetType().FullName+": "+e.Message); Environment.ExitCode=1; }
    }
    static void Run() {
        BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        using(Dashboard form=new Dashboard())
        {
            typeof(Dashboard).GetMethod("OnLoad",flags).Invoke(form,new object[]{EventArgs.Empty});
            MethodInfo prepare=typeof(Dashboard).GetMethod("PreparePopup",flags);
            foreach(string field in new[]{"_keyboardMenu","_categoryMenu","_distributionMenu"})
            {
                object[] args={null};ContextMenuStrip menu=(ContextMenuStrip)prepare.Invoke(form,args);
                typeof(Dashboard).GetField(field,flags).SetValue(form,menu);
                bool clicked=false;ToolStripMenuItem item=new ToolStripMenuItem("无小键盘");
                bool disposed=false;item.Disposed+=delegate{disposed=true;};item.Click+=delegate{clicked=true;};menu.Items.Add(item);
                typeof(ToolStripDropDown).GetMethod("OnClosed",flags).Invoke(menu,new object[]{new ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason.ItemClicked)});
                if(menu.IsDisposed)throw new Exception("Menu destroyed during close callback");
                item.PerformClick();if(!clicked)throw new Exception("Item click lost after close");
                args[0]=menu;
                if(!ReferenceEquals(menu,prepare.Invoke(form,args)) || menu.Items.Count!=0 || !disposed)throw new Exception("Menu reuse or item disposal failed");
            }
            typeof(Dashboard).GetMethod("OnFormClosed",flags).Invoke(form,new object[]{new FormClosedEventArgs(CloseReason.UserClosing)});
            foreach(string field in new[]{"_keyboardMenu","_categoryMenu","_distributionMenu"})
                if(!((ContextMenuStrip)typeof(Dashboard).GetField(field,flags).GetValue(form)).IsDisposed)throw new Exception("Menu leaked at owner shutdown");
        }
        Console.WriteLine("PASS: all three popup close/click/reuse/owner-disposal lifecycles");
    }
}



