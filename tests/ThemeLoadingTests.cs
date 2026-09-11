using System;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeyMouseStats;
internal static class ThemeLoadingTests
{
    [STAThread] static void Main()
    {
        Store.History.Clear();Store.RollDay(DateTime.Today);Store.KeyboardLayout=2;
        Store.Today.Keys=10000;for(int k=32;k<91;k++)Store.Today.KeyCounts[k]=100+k;
        BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        string[] pages={"Overview","Trend","Hours","Keys","Apps","Insights"};
        string[] nikki={"NikkiDream","NikkiPoseTrend","NikkiPoseHours","NikkiPoseKeys","NikkiPoseApps","NikkiPoseInsights"};
        using(Dashboard form=new Dashboard())
        using(Bitmap bmp=new Bitmap(1608,1086))
        using(Graphics graphics=Graphics.FromImage(bmp))
        {
            typeof(Dashboard).GetMethod("OnLoad",flags).Invoke(form,new object[]{EventArgs.Empty});
            typeof(Dashboard).GetField("_s",flags).SetValue(form,1f);
            FieldInfo tab=typeof(Dashboard).GetField("_tab",flags);
            MethodInfo paint=typeof(Dashboard).GetMethod("OnPaint",flags);
            object[] args={new PaintEventArgs(graphics,new Rectangle(Point.Empty,bmp.Size))};
            for(int pass=0;pass<2;pass++)for(int theme=5;theme<=11;theme++)
            {
                typeof(Dashboard).GetField("_s",flags).SetValue(form,pass==0?1f:1.5f);
                Store.ThemeId=theme;double slowest=0;
                for(int page=0;page<6;page++)
                {
                    string prefix=theme==11?"EuroTruck":theme==10?"Wuxia":theme==9?"Balatro":theme==6?"Halo":theme==7?"Resident":"Minecraft";
                    string resource=theme==11?prefix+pages[page]:theme==10?"WuxiaBackgrounds":theme==9?"BalatroBackgrounds":theme==5?nikki[page]:prefix+pages[page];
                    Stopwatch request=Stopwatch.StartNew();ThemeImages.Get(resource);request.Stop();
                    if(request.ElapsedMilliseconds>1000)throw new Exception("Image request blocked UI");
                    Wait(resource);Wait(theme==11?"EuroTruckIcons":theme==10?"WuxiaUi":theme==5?"NikkiEmotes":prefix+"Icons");if(theme==10)Wait("WuxiaWidget");if(theme==11)Wait("EuroTruckWidget");
                    tab.SetValue(form,Enum.ToObject(tab.FieldType,page));
                    typeof(Dashboard).GetField("_showKeyboardHeatmap",flags).SetValue(form,true);
                    graphics.ResetTransform();paint.Invoke(form,args);
                    Stopwatch watch=Stopwatch.StartNew();
                    for(int frame=0;frame<6;frame++){graphics.ResetTransform();paint.Invoke(form,args);}
                    watch.Stop();slowest=Math.Max(slowest,watch.Elapsed.TotalMilliseconds/6);
                    // 1.8.0:动效中间帧的帧预算。换页淡入只多一次半透明填充,所以必须留在
                    // 静态帧的 1.6 倍以内(上限 33 ms ≈ 30 FPS)。用假时钟把时间轴钉在 50%。
                    Motion.ResetForTests();
                    Motion.UseFakeClock(1000000);
                    Motion.Start("page",150,Ease.Linear);
                    Motion.AdvanceFakeClock(75);
                    graphics.ResetTransform();paint.Invoke(form,args);
                    Stopwatch animated=Stopwatch.StartNew();
                    for(int frame=0;frame<6;frame++){graphics.ResetTransform();paint.Invoke(form,args);}
                    animated.Stop();
                    double animationFrame=animated.Elapsed.TotalMilliseconds/6;
                    Motion.ResetForTests();
                    double budget=Math.Max(33,slowest*1.6);
                    if(animationFrame>budget)
                        throw new Exception("Animation frame budget exceeded on "+pages[page]+" (theme "+theme+"): "
                            +animationFrame.ToString("0.0")+" ms > "+budget.ToString("0.0")+" ms");
                    if(ThemeImages.AllocatedBytes>ThemeImages.Budget||NikkiArt.ScaledBytes>40L*1024*1024)throw new Exception("Unbounded original-image cache");
                    if(pass==1 && (page==3||theme>=9))
                    {
                        bmp.Save("tests/theme-performance-"+theme+"-"+page+".png");
                        graphics.ResetTransform();graphics.ScaleTransform(1.5f,1.5f);
                        typeof(Dashboard).GetMethod("PaintLoading",flags).Invoke(form,new object[]{graphics});
                        bmp.Save("tests/theme-loading-"+theme+".png");
                    }
                }
                Console.WriteLine("Pass "+pass+", theme "+theme+": slowest warm page "+slowest.ToString("0.0")+" ms; originals "+(ThemeImages.ResidentBytes/1048576.0).ToString("0.0")+" MiB");
            }
            // Missing optional art must not crash or enqueue endless retries.
            ThemeImages.Get("MissingTestResource");Thread.Sleep(100);
            if(ThemeImages.Get("MissingTestResource")!=null)throw new Exception("Missing asset fallback");
            typeof(Dashboard).GetMethod("OnFormClosed",flags).Invoke(form,new object[]{new FormClosedEventArgs(CloseReason.None)});
        }
        Console.WriteLine("PASS: 84 page transitions, bounded original cache, async loading, animation frame budget and missing-image fallback");
    }
    static void Wait(string resource)
    {
        Stopwatch watch=Stopwatch.StartNew();
        while(ThemeImages.Get(resource)==null)
        {if(watch.ElapsedMilliseconds>15000)throw new Exception("Image failed: "+resource);Thread.Sleep(10);}
    }
}
