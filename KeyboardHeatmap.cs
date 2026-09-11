using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal sealed class HeatKey
    {
        public string Label;
        public int Scan, Vk;
        public RectangleF Bounds;
        public HeatKey(string label, int scan, int vk, float x, float y, float w, float h)
        { Label = label; Scan = scan; Vk = vk; Bounds = new RectangleF(x, y, w, h); }
    }

    internal static class KeyboardHeat
    {
        public static readonly string[] Names = { "自动（设备建议）", "全尺寸 · ANSI 104", "无小键盘 · 87 键", "75% · 紧凑", "65% · 紧凑", "60% · 紧凑" };
        public static int Valid(int value) { return value >= 0 && value < Names.Length ? value : 0; }
        public static int ScanId(int vk, uint scan, uint flags)
        { return scan == 0 ? 0 : vk == 19 ? 0x245 : (int)(scan & 255) | ((flags & 1) != 0 ? 256 : 0); }
        public static void Record(DayRecord day, int vk, uint scan, uint flags)
        {
            int id = ScanId(vk, scan, flags);
            if (id == 0) return;
            Add(day.PhysicalKeys, id, 1); Add(day.PhysicalVks, vk, 1);
        }
        private static void Add(Dictionary<int, long> counts, int id, long count)
        { long old; counts.TryGetValue(id, out old); counts[id] = old + count; }
        public static long Count(Dictionary<int, long> counts, int id)
        { long value; return counts.TryGetValue(id, out value) ? value : 0; }
        /// <summary>一个虚拟键只归属一个规范键位;旧记录按按键名近似归位,不能重复计 Enter。</summary>
        private static Dictionary<int, int> Canonical()
        {
            Dictionary<int, int> canonical = new Dictionary<int, int>();
            foreach (HeatKey key in Layout(1)) if (key.Vk > 0 && !canonical.ContainsKey(key.Vk)) canonical[key.Vk] = key.Scan;
            return canonical;
        }
        /// <summary>配列里没有这个虚拟键时的近似归位(通用 Ctrl / Alt / Shift)。</summary>
        private static int FallbackScan(int vk)
        {
            return vk == 16 ? 0x2a : vk == 17 ? 0x1d : vk == 18 ? 0x38 : 0;
        }
        public static Dictionary<int, long> Aggregate(IEnumerable<DayRecord> days, out long legacy, out long total)
        {
            Dictionary<int, long> result = new Dictionary<int, long>();
            Dictionary<int, int> canonical = Canonical();
            legacy = total = 0;
            foreach (DayRecord day in days)
            {
                foreach (KeyValuePair<int, long> pair in day.PhysicalKeys) { Add(result, pair.Key, pair.Value); total += pair.Value; }
                foreach (KeyValuePair<int, long> pair in day.KeyCounts)
                {
                    long remainder = Math.Max(0, pair.Value - Count(day.PhysicalVks, pair.Key));
                    legacy += remainder; total += remainder;
                    int id;
                    if (canonical.TryGetValue(pair.Key, out id)) Add(result, id, remainder);
                    else if (pair.Key == 16 || pair.Key == 17 || pair.Key == 18) Add(result, FallbackScan(pair.Key), remainder);
                }
            }
            return result;
        }

        /// <summary>
        /// 1.7.6:逐键累计按住时长(毫秒),键为扫描码,归属规则与次数热力完全一致。
        /// 逐键明细只按虚拟键保存,所以主区 Enter 与小键盘 Enter 会合并到同一个键位;
        /// Top N 截断的「其他按键」与配列里没有的键无法定位,计入 unmapped。
        /// total 含 unmapped,因此「配列覆盖」对两种口径都成立。
        /// </summary>
        public static Dictionary<int, KeyHold> AggregateHolds(IEnumerable<DayRecord> days, out long unmapped, out long total)
        {
            Dictionary<int, KeyHold> result = new Dictionary<int, KeyHold>();
            Dictionary<int, int> canonical = Canonical();
            unmapped = total = 0;
            foreach (DayRecord day in days)
            {
                foreach (KeyValuePair<int, KeyHold> pair in day.Holds)
                {
                    double milliseconds = pair.Value.TotalMs;
                    if (milliseconds <= 0) continue;
                    total += (long)Math.Round(milliseconds);
                    long rounded = (long)Math.Round(milliseconds);
                    if (rounded <= 0) continue;
                    if (pair.Key == HoldTracker.OtherKey) { unmapped += rounded; continue; }
                    int id;
                    if (!canonical.TryGetValue(pair.Key, out id)) id = FallbackScan(pair.Key);
                    if (id == 0) { unmapped += rounded; continue; }
                    KeyHold hold;
                    if (!result.TryGetValue(id, out hold)) { hold = new KeyHold(); result[id] = hold; }
                    hold.Count += pair.Value.Count;
                    hold.TotalMs += milliseconds;
                    if (pair.Value.MaxMs > hold.MaxMs) hold.MaxMs = pair.Value.MaxMs;
                }
            }
            return result;
        }
        /// <summary>某个键位的累计按住时长(毫秒);没有样本时为 0。</summary>
        public static double HoldMs(Dictionary<int, KeyHold> holds, int id)
        {
            KeyHold hold;
            return holds != null && holds.TryGetValue(id, out hold) ? hold.TotalMs : 0;
        }
        public static KeyHold HoldDetail(Dictionary<int, KeyHold> holds, int id)
        {
            KeyHold hold;
            return holds != null && holds.TryGetValue(id, out hold) ? hold : null;
        }
        private static void Row(List<HeatKey> keys, string labels, int[] scans, int[] vks, float y, float first, float last)
        {
            string[] words = labels.Split('|'); float x = 0;
            for (int i = 0; i < words.Length; i++)
            {
                float w = i == 0 ? first : i == words.Length - 1 ? last : 1;
                keys.Add(new HeatKey(words[i], scans[i], vks[i], x, y, w, 1)); x += w;
            }
        }
        public static List<HeatKey> Layout(int layout)
        {
            List<HeatKey> keys = new List<HeatKey>();
            bool compact = layout >= 3, fnRow = layout <= 3;
            float top = fnRow ? 1.35f : 0;
            Row(keys, "`|1|2|3|4|5|6|7|8|9|0|-|=|Back", new int[] {41,2,3,4,5,6,7,8,9,10,11,12,13,14}, new int[] {192,49,50,51,52,53,54,55,56,57,48,189,187,8}, top,1,2);
            Row(keys, "Tab|Q|W|E|R|T|Y|U|I|O|P|[|]|\\", new int[] {15,16,17,18,19,20,21,22,23,24,25,26,27,43}, new int[] {9,81,87,69,82,84,89,85,73,79,80,219,221,220},top+1,1.5f,1.5f);
            Row(keys, "Caps|A|S|D|F|G|H|J|K|L|;|'|Enter",new int[] {58,30,31,32,33,34,35,36,37,38,39,40,28},new int[] {20,65,83,68,70,71,72,74,75,76,186,222,13},top+2,1.75f,2.25f);
            Row(keys, "Shift|Z|X|C|V|B|N|M|,|.|/|Shift",new int[] {42,44,45,46,47,48,49,50,51,52,53,54},new int[] {160,90,88,67,86,66,78,77,188,190,191,161},top+3,2.25f,compact && layout !=5 ?1.75f:2.75f);
            keys.Add(new HeatKey("Ctrl",29,162,0,top+4,1.25f,1));
            keys.Add(new HeatKey("Win",347,91,1.25f,top+4,1.25f,1));
            keys.Add(new HeatKey("Alt",56,164,2.5f,top+4,1.25f,1));
            keys.Add(new HeatKey("Space",57,32,3.75f,top+4,6.25f,1));
            if (compact && layout !=5)
            {
                string[] labels = {"Alt","Fn","←","↓","→"}; int[] scans={312,0,331,336,333}; int[] vks={165,0,37,40,39};
                for(int i=0;i<5;i++) keys.Add(new HeatKey(labels[i],scans[i],vks[i],10+i,top+4,1,1));
                keys.Add(new HeatKey("↑",328,38,14,top+3,1,1));
                string[] nav={"Del","Home","PgUp","PgDn"}; int[] ns={339,327,329,337};int[] nv={46,36,33,34};
                for(int i=0;i<4;i++) keys.Add(new HeatKey(nav[i],ns[i],nv[i],15.25f,top+i,1,1));
            }
            else
            {
                string[] labels={"Alt","Win","Menu","Ctrl"};int[] scans={312,348,349,285};int[] vks={165,92,93,163};
                for(int i=0;i<4;i++) keys.Add(new HeatKey(labels[i],scans[i],vks[i],10+i*1.25f,top+4,1.25f,1));
            }
            if(fnRow)
            {
                keys.Add(new HeatKey("Esc",1,27,0,0,1,1));
                for(int i=0;i<12;i++) keys.Add(new HeatKey("F"+(i+1), i<10?59+i:87+i-10,112+i,compact?1.25f+i*1.1f:2+i+(i/4)*0.5f,0,1,1));
            }
            else
            {
                // Compact boards commonly put Esc on the grave key; firmware layers cannot be inferred.
                keys[0].Label="` / Esc";
            }
            if(!compact)
            {
                string[] labels={"PrtSc","ScrLk","Pause","Ins","Home","PgUp","Del","End","PgDn"};
                int[] scans={311,70,581,338,327,329,339,335,337};int[] vks={44,145,19,45,36,33,46,35,34};
                for(int i=0;i<9;i++) keys.Add(new HeatKey(labels[i],scans[i],vks[i],15.5f+i%3,i<3?0:top+(i-3)/3,1,1));
                keys.Add(new HeatKey("↑",328,38,16.5f,top+3,1,1));
                for(int i=0;i<3;i++) keys.Add(new HeatKey(new string[]{"←","↓","→"}[i],new int[]{331,336,333}[i],new int[]{37,40,39}[i],15.5f+i,top+4,1,1));
                if(layout==1)
                {
                    string[] labels2={"Num","/","*","−","7","8","9","+","4","5","6","1","2","3","Enter","0","."};
                    int[] s={325,309,55,74,71,72,73,78,75,76,77,79,80,81,284,82,83};
                    int[] v={144,111,106,109,103,104,105,107,100,101,102,97,98,99,13,96,110};
                    float[] x={0,1,2,3,0,1,2,3,0,1,2,0,1,2,3,0,2};float[] y={0,0,0,0,1,1,1,1,2,2,2,3,3,3,3,4,4};
                    for(int i=0;i<s.Length;i++) keys.Add(new HeatKey(labels2[i],s[i],v[i],19+x[i],top+y[i],i==15?2:1,i==7||i==14?2:1));
                }
            }
            return keys;
        }
    }

    internal static class KeyboardDevices
    {
        [StructLayout(LayoutKind.Sequential)] private struct Device { public IntPtr Handle; public uint Type; }
        [DllImport("user32.dll")] private static extern uint GetRawInputDeviceList([Out] Device[] list, ref uint count, uint size);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr info, ref uint size);
        public static int Suggested = 1;
        public static string Description = "尚未读取设备";
        public static int Suggest(int keys)
        { return keys==104 ? 1 : keys==87 ? 2 : keys==84 || keys==82 || keys==80 ? 3 : keys==68 || keys==67 || keys==66 ? 4 : keys==61 ? 5 : 0; }
        public static void Refresh()
        {
            Suggested=1;
            try
            {
                uint count=0, size=(uint)Marshal.SizeOf(typeof(Device));
                if(GetRawInputDeviceList(null,ref count,size)==uint.MaxValue || count>256) throw new InvalidOperationException();
                Device[] list=new Device[count];
                uint read=GetRawInputDeviceList(list,ref count,size);
                if(read==uint.MaxValue) throw new InvalidOperationException();
                HashSet<int> candidates=new HashSet<int>();List<int> reported=new List<int>();
                IntPtr info=Marshal.AllocHGlobal(32);
                try
                {
                    for(int i=0;i<read;i++) if(list[i].Type==1)
                    {
                        for(int j=0;j<32;j+=4) Marshal.WriteInt32(info,j,0);
                        Marshal.WriteInt32(info,32);uint bytes=32;
                        if(GetRawInputDeviceInfo(list[i].Handle,0x2000000b,info,ref bytes)==uint.MaxValue) continue;
                        int keys=Marshal.ReadInt32(info,28);
                        if(keys<30) continue;
                        reported.Add(keys);candidates.Add(Suggest(keys));
                    }
                }
                finally { Marshal.FreeHGlobal(info); }
                if(candidates.Count==1 && !candidates.Contains(0))
                {
                    foreach(int candidate in candidates) Suggested=candidate;
                    Description="设备报告 "+reported[0]+" 键 · 建议配列，可手动修正";
                }
                else Description=reported.Count==0?"未获得有效键数 · 暂用全尺寸，请手动选择": "设备键数不明确或存在多个配列 · 暂用全尺寸，请手动选择";
            }
            catch { Description="设备读取不可用 · 暂用全尺寸，请手动选择"; }
        }
    }

    internal sealed partial class Dashboard
    {
        private void ExportKeyboardHeatmap()
        {
            try
            {
                long legacy,total;
                Dictionary<int,long> counts=KeyboardHeat.Aggregate(KeyRangeDays(),out legacy,out total);
                long unmappedHold;long holdTotal;
                Dictionary<int,KeyHold> holds=KeyboardHeat.AggregateHolds(KeyRangeDays(),out unmappedHold,out holdTotal);
                Dictionary<int,string> names=new Dictionary<int,string>();
                foreach(HeatKey key in KeyboardHeat.Layout(1)) names[key.Scan]=key.Label;
                System.Text.StringBuilder csv=new System.Text.StringBuilder("扫描码,键位,次数,占全部击键比例,累计按住毫秒,平均按住毫秒,范围内旧记录次数\r\n");
                List<int> ids=new List<int>(counts.Keys);ids.Sort();
                foreach(int id in ids)
                {
                    string name;names.TryGetValue(id,out name);
                    KeyHold hold=KeyboardHeat.HoldDetail(holds,id);
                    double holdMs=hold==null?0:hold.TotalMs;
                    csv.Append(id).Append(',').Append('"').Append((name??"配列外按键").Replace("\"","\"\"")).Append("\",").Append(counts[id]).Append(',')
                        .Append((total>0?(double)counts[id]/total:0).ToString("0.######",CultureInfo.InvariantCulture)).Append(',')
                        .Append(holdMs.ToString("0",CultureInfo.InvariantCulture)).Append(',')
                        .Append((hold!=null&&hold.Count>0?(holdMs/hold.Count).ToString("0.#",CultureInfo.InvariantCulture):"0")).Append(',')
                        .Append(legacy).AppendLine();
                }
                csv.AppendLine();
                csv.AppendLine("# 按住时长合计(毫秒),"+holdTotal+",其中无法定位到键位,"+unmappedHold);
                csv.AppendLine("# 逐键明细只按虚拟键保存:主区 Enter 与小键盘 Enter 合并;Top 48 以外的按键无法定位。");
                string path=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),"键盘热力_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+".csv");
                System.IO.File.WriteAllText(path,csv.ToString(),new System.Text.UTF8Encoding(true));
                ThemeMessage.Show(this,"已导出：\n"+path,"导出成功",MessageBoxButtons.OK,MessageBoxIcon.Information);
            }
            catch(Exception ex) { ThemeMessage.Show(this,ex.Message,"导出失败",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
        }
        private readonly List<RectangleF> _keyboardHoverRects=new List<RectangleF>();
        private Bitmap _keyboardPlate;
        /// <summary>1.8.0:上一张键位板,用于换口径/换区间时的交叉淡入。</summary>
        private Bitmap _keyboardPlatePrev;
        private string _keyboardPlateStamp;
        private bool _showKeyboardHeatmap;
        private bool _keyboardHoldMode;   // 1.7.6:热力图口径——false 击键次数 / true 累计按住时长
        private bool _keyboardDevicesRead;
        /// <summary>1.8.0:与 _keyboardHoverRects 同序的扫描码,供点击下钻使用。</summary>
        private readonly List<int> _keyboardScans = new List<int>();
        /// <summary>1.8.0:下钻选中的键位(扫描码;-1 表示看全部按键)。</summary>
        private int _selectedKeyScan = -1;

        /// <summary>选中键位的名字;找不到时返回空串。</summary>
        private string SelectedKeyLabel()
        {
            if (_selectedKeyScan < 0) return "";
            int layout = Store.KeyboardLayout == 0 ? KeyboardDevices.Suggested : Store.KeyboardLayout;
            foreach (HeatKey key in KeyboardHeat.Layout(layout))
                if (key.Scan == _selectedKeyScan) return key.Label;
            return "已选键位";
        }
        /// <summary>1.8.0:点击下钻——命中某个键帽返回 true。</summary>
        internal bool HitKeyboardKey(PointF contentPoint, out int scan)
        {
            scan = -1;
            for (int i = 0; i < _keyboardHoverRects.Count && i < _keyboardScans.Count; i++)
            {
                if (!_keyboardHoverRects[i].Contains(contentPoint)) continue;
                scan = _keyboardScans[i];
                return scan != 0;   // Fn 由硬件处理,没有可统计的键位
            }
            return false;
        }
        internal void SelectKey(int scan)
        {
            _selectedKeyScan = scan;
            Motion.Start("keydetail", 180, Ease.OutCubic);
            Invalidate();
        }
        internal bool ClearSelectedKey()
        {
            if (_selectedKeyScan < 0) return false;
            _selectedKeyScan = -1;
            Motion.Start("keydetail", 180, Ease.OutCubic);
            Invalidate();
            return true;
        }
        private void KeyboardMenu()
        {
            ContextMenuStrip menu=PreparePopup(ref _keyboardMenu);

            for(int i=0;i<KeyboardHeat.Names.Length;i++)
            {
                int choice=i; ToolStripMenuItem item=new ToolStripMenuItem(KeyboardHeat.Names[i]);
                item.Checked=Store.KeyboardLayout==i;
                item.Click+=delegate { Store.KeyboardLayout=choice; Store.Save(); Invalidate(); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("重新检测键盘",null,delegate { KeyboardDevices.Refresh(); Invalidate(); });
            menu.Show(this,PointToClient(Cursor.Position));
        }
        private static Color HeatColor(double fraction)
        {
            return fraction<=0?HeatScale.Zero:HeatScale.At(fraction);
        }
        private static Color HeatInk(Color fill)
        {
            double[] channels={fill.R/255.0,fill.G/255.0,fill.B/255.0};
            for(int i=0;i<3;i++) channels[i]=channels[i]<=0.04045?channels[i]/12.92:Math.Pow((channels[i]+0.055)/1.055,2.4);
            double luminance=channels[0]*0.2126+channels[1]*0.7152+channels[2]*0.0722;
            return luminance>0.20?Color.FromArgb(16,25,32):Color.FromArgb(250,252,255);
        }
        /// <summary>1.8.0:某个键位在配列内的次数排位(从 1 开始;只数配列里的键)。</summary>
        private static int KeyRankFor(Dictionary<int, long> counts, List<HeatKey> keys, int scan)
        {
            long mine = KeyboardHeat.Count(counts, scan);
            int rank = 1;
            foreach (HeatKey key in keys)
                if (key.Scan != scan && KeyboardHeat.Count(counts, key.Scan) > mine) rank++;
            return rank;
        }

        private void PaintKeyboardHeatmap(Graphics g, IEnumerable<DayRecord> days)
        {
            if(!_keyboardDevicesRead) { KeyboardDevices.Refresh(); _keyboardDevicesRead=true; }
            int layout=Store.KeyboardLayout==0?KeyboardDevices.Suggested:Store.KeyboardLayout;
            bool holdMode=_keyboardHoldMode;
            PaintCard(g,Cx,138,Cw,438,null);
            using(Font title=new Font("Microsoft YaHei UI",17,FontStyle.Bold,GraphicsUnit.Pixel))
                AppText(g,holdMode?"你的键盘，按住的痕迹":"你的键盘，使用的痕迹",title,Ctext,new RectangleF(Cx+22,153,400,30),false);
            RectangleF metric=new RectangleF(Cx+Cw-352,152,104,30);
            _chips.Add(metric);_chipIds.Add(79);
            PaintChip(g,metric,holdMode?"按住时长":"击键次数",holdMode,null);
            RectangleF selector=new RectangleF(Cx+Cw-236,152,214,30);
            _chips.Add(selector);_chipIds.Add(75);
            PaintChip(g,selector,KeyboardHeat.Names[layout]+" ▾",false,null);
            AppText(g,Store.KeyboardLayout==0?KeyboardDevices.Description:"手动配列 · 多把键盘合并统计 · 悬停查看键位详情",_fSmall,Csub,new RectangleF(Cx+22,187,Cw-44,22),false);
            List<HeatKey> keys=KeyboardHeat.Layout(layout);
            long legacy=0,total=0,unmappedHold=0;
            Dictionary<int,long> counts=null;Dictionary<int,KeyHold> holds=null;
            if(holdMode) holds=KeyboardHeat.AggregateHolds(days,out unmappedHold,out total);
            else
            {
                counts=KeyboardHeat.Aggregate(days,out legacy,out total);
                if(layout>=4) { counts[41]=KeyboardHeat.Count(counts,41)+KeyboardHeat.Count(counts,1); counts.Remove(1); }
            }
            float width=0,height=0;long max=0,visible=0;int used=0,championScan=-1;string favorite="—";
            foreach(HeatKey key in keys)
            {
                width=Math.Max(width,key.Bounds.Right);height=Math.Max(height,key.Bounds.Bottom);
                long value=holdMode?(long)KeyboardHeat.HoldMs(holds,key.Scan):KeyboardHeat.Count(counts,key.Scan);
                if(value>max) {max=value;favorite=key.Label;championScan=key.Scan;} visible+=value;if(value>0)used++;
            }
            string[] labels=holdMode?new[]{"累计按住","最久按键","点亮键位"}:new[]{"记录击键","最常用键","点亮键位"};
            string[] values=holdMode?new[]{HoldReportData.Duration(total),favorite,used+" / "+keys.Count}:new[]{Analysis.FmtCount(total),favorite,used+" / "+keys.Count};
            // 1.8.0:下钻后这三格改成「这个键」的数字,不再是全局。
            if(_selectedKeyScan>=0)
            {
                string keyName=SelectedKeyLabel();
                if(!string.IsNullOrEmpty(keyName)) favorite=keyName;
                if(holdMode)
                {
                    KeyHold keyHold=KeyboardHeat.HoldDetail(holds,_selectedKeyScan);
                    labels=new[]{"该键累计","该键平均","该键最长"};
                    values=keyHold==null?new[]{"--","--","--"}
                        :new[]{HoldReportData.Duration(keyHold.TotalMs),HoldReportData.Duration(keyHold.Mean),HoldReportData.Duration(keyHold.MaxMs)};
                }
                else
                {
                    long keyValue=KeyboardHeat.Count(counts,_selectedKeyScan);
                    labels=new[]{"该键击键","占全部","本键排位"};
                    values=new[]{Analysis.FmtCount(keyValue),
                        total>0?(100.0*keyValue/total).ToString("0.0",CultureInfo.InvariantCulture)+"%":"--",
                        keyValue>0?"第 "+KeyRankFor(counts,keys,_selectedKeyScan)+" / "+keys.Count:"--"};
                }
            }
            for(int i=0;i<3;i++)
            {
                float x=Cx+22+i*185;
                AppText(g,ThemeArt.Active&&i==1?(ThemeArt.Dark?"核心按键":"大喵最爱"):labels[i],_fSmall,Csub,new RectangleF(x,219,70,22),false);
                AppText(g,values[i],_fNum2,Ctext,new RectangleF(x+73,213,106,30),false);
            }
            HeatScale.Bar(g,new RectangleF(Cx+Cw-198,221,176,9));
            AppText(g,"0+",_fSmall,Csub,new RectangleF(Cx+Cw-198,232,40,20),false);
            AppText(g,max>0?(holdMode?HoldReportData.Duration(max):Analysis.FmtCount(max)+" 次"):"暂无样本",_fSmall,Csub,new RectangleF(Cx+Cw-133,232,111,20),true);
            Color tray=ArtTheme.Mix(Ccard,ArtTheme.Current.Background,0.64);
            using(GraphicsPath shell=RoundedRect(Cx+12,260,Cw-24,242,14))
            using(SolidBrush b=new SolidBrush(tray))
            using(Pen edge=new Pen(ArtTheme.Mix(Ccard,Ctext,0.07))) { g.FillPath(b,shell);g.DrawPath(edge,shell); }
            float unit=(Cw-48)/width, row=Math.Min(42,220/height), originY=272+(220-height*row)/2;
            _keyboardHoverRects.Clear();
            _keyboardScans.Clear();
            PointF mouse=ToBase(_mouse);mouse=new PointF(mouse.X-ContentX,mouse.Y-ContentY);
            HeatKey hover=null;long hoverCount=0;KeyHold hoverHold=null;
            RectangleF hoverRect=RectangleF.Empty;
            RectangleF plateBounds=new RectangleF(Cx+22,270,Cw-44,226);
            Graphics keyGraphics=g;
            if(ThemeArt.Active)
            {
                System.Text.StringBuilder stamp=new System.Text.StringBuilder(ThemeImages.Revision+":"+Store.ThemeId+":"+layout+":"+_s.ToString(CultureInfo.InvariantCulture)+":"+(holdMode?"hold":"count"));
                foreach(HeatKey key in keys)stamp.Append(':').Append(key.Scan).Append('=').Append(holdMode?(long)KeyboardHeat.HoldMs(holds,key.Scan):KeyboardHeat.Count(counts,key.Scan));
                string signature=stamp.ToString();
                keyGraphics=null;
                if(_keyboardPlate==null || _keyboardPlateStamp!=signature)
                {
                    // 1.8.0:重建时把旧板留下来做交叉淡入,让换口径/换区间的颜色变化是渐变而不是跳变。
                    // 注意:只保留一张旧板(不做逐帧重建),所以动画期间不会重复解码或反复分配位图。
                    if(_keyboardPlate!=null)
                    {
                        if(_keyboardPlatePrev!=null)_keyboardPlatePrev.Dispose();
                        _keyboardPlatePrev=_keyboardPlate;
                        if(Visible)Motion.Start("platefade",200,Ease.OutCubic);
                    }
                    _keyboardPlate=new Bitmap((int)Math.Ceiling(plateBounds.Width*_s),(int)Math.Ceiling(plateBounds.Height*_s),System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    _keyboardPlateStamp=signature;
                    keyGraphics=Graphics.FromImage(_keyboardPlate);keyGraphics.Clear(tray);
                    keyGraphics.SmoothingMode=SmoothingMode.AntiAlias;keyGraphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    keyGraphics.ScaleTransform(_s,_s);keyGraphics.TranslateTransform(-plateBounds.X,-plateBounds.Y);
                }
            }
            using(Font font=new Font("Segoe UI",layout==1?9.5f:11,FontStyle.Regular,GraphicsUnit.Pixel))
            using(StringFormat center=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
            foreach(HeatKey key in keys)
            {
                RectangleF rect=new RectangleF(Cx+24+key.Bounds.X*unit,originY+key.Bounds.Y*row,key.Bounds.Width*unit-4,key.Bounds.Height*row-5);
                _keyboardHoverRects.Add(rect);
                _keyboardScans.Add(key.Scan);
                long value=holdMode?(long)KeyboardHeat.HoldMs(holds,key.Scan):KeyboardHeat.Count(counts,key.Scan);bool hovered=rect.Contains(mouse);
                if(hovered) {hover=key;hoverCount=value;hoverHold=holdMode?KeyboardHeat.HoldDetail(holds,key.Scan):null;hoverRect=rect;}
                if(keyGraphics==null)continue;
                if(ThemeArt.Active)hovered=false;
                Color fill=HeatColor(max>0?(double)value/max:0);
                bool champion=ThemeArt.Active && key.Scan==championScan;
                using(GraphicsPath shadow=RoundedRect(rect.X,rect.Y+2,rect.Width,rect.Height,5))
                using(SolidBrush b=new SolidBrush(Color.FromArgb(35,0,0,0))) keyGraphics.FillPath(b,shadow);
                using(GraphicsPath path=RoundedRect(rect.X,rect.Y,rect.Width,rect.Height,MinecraftArt.Active?1:ThemeArt.Dark?3:ThemeArt.Active?8:5))
                using(SolidBrush brush=new SolidBrush(fill))
                using(Pen border=new Pen(champion?Corange:hovered?Cblue:ArtTheme.Mix(fill,Ctext,0.12),champion?2f:hovered?1.6f:0.65f))
                { keyGraphics.FillPath(brush,path);keyGraphics.DrawPath(border,path); }
                if(EuroTruckArt.Active)EuroTruckArt.KeyCap(keyGraphics,key,rect,champion);
                else if(WuxiaArt.Active)WuxiaArt.KeyCap(keyGraphics,key,rect,champion);
                RectangleF labelRect=rect;
                if(champion)
                {
                    float iconSize=rect.Width>75?Math.Min(34,rect.Height):Math.Min(20,rect.Height-10);
                    RectangleF sticker=rect.Width>75?new RectangleF(rect.Right-iconSize-4,rect.Y,iconSize,iconSize):new RectangleF(rect.X+(rect.Width-iconSize)/2,rect.Y,iconSize,iconSize);
                    ThemeArt.Sticker(keyGraphics,0,sticker);
                    if(rect.Width>75)labelRect.Width-=iconSize+4;
                    else labelRect=new RectangleF(rect.X,rect.Bottom-12,rect.Width,12);
                }
                using(SolidBrush brush=new SolidBrush(key.Scan==0?Csub:HeatInk(fill))) keyGraphics.DrawString(key.Label,font,brush,labelRect,center);
            }
            if(ThemeArt.Active)
            {
                if(keyGraphics!=null)keyGraphics.Dispose();
                PointF[] corner={plateBounds.Location};using(Matrix matrix=g.Transform)matrix.TransformPoints(corner);
                GraphicsState state=g.Save();g.ResetTransform();g.DrawImageUnscaled(_keyboardPlate,(int)Math.Round(corner[0].X),(int)Math.Round(corner[0].Y));g.Restore(state);
                // 1.8.0:旧板按 alpha 渐隐,和上新板叠成一次交叉淡入;动画跑完就释放,不留额外常驻内存。
                if(_keyboardPlatePrev!=null)
                {
                    double fade=Motion.Progress("platefade");
                    if(fade>=1){_keyboardPlatePrev.Dispose();_keyboardPlatePrev=null;}
                    else
                    {
                        state=g.Save();g.ResetTransform();
                        using(System.Drawing.Imaging.ImageAttributes attributes=new System.Drawing.Imaging.ImageAttributes())
                        {
                            float alpha=(float)(1-fade);
                            System.Drawing.Imaging.ColorMatrix matrix=new System.Drawing.Imaging.ColorMatrix();
                            matrix.Matrix33=alpha;
                            attributes.SetColorMatrix(matrix);
                            g.DrawImage(_keyboardPlatePrev,new Rectangle((int)Math.Round(corner[0].X),(int)Math.Round(corner[0].Y),_keyboardPlatePrev.Width,_keyboardPlatePrev.Height),
                                0,0,_keyboardPlatePrev.Width,_keyboardPlatePrev.Height,GraphicsUnit.Pixel,attributes);
                        }
                        g.Restore(state);
                    }
                }
                if(hover!=null)using(GraphicsPath outline=RoundedRect(hoverRect.X,hoverRect.Y,hoverRect.Width,hoverRect.Height,8))using(Pen pen=new Pen(Cblue,1.6f))g.DrawPath(pen,outline);
            }
            // 1.8.0:下钻选中的键位加一圈描边,淡入由 keydetail 通道驱动。
            if(_selectedKeyScan>=0)
            {
                for(int i=0;i<_keyboardHoverRects.Count && i<_keyboardScans.Count;i++)
                {
                    if(_keyboardScans[i]!=_selectedKeyScan)continue;
                    int alpha=(int)(255*Motion.Progress("keydetail"));
                    if(alpha<40)alpha=40;
                    using(GraphicsPath ring=RoundedRect(_keyboardHoverRects[i].X-1,_keyboardHoverRects[i].Y-1,_keyboardHoverRects[i].Width+2,_keyboardHoverRects[i].Height+2,8))
                    using(Pen pen=new Pen(Color.FromArgb(alpha,Ccyan),2f)) g.DrawPath(pen,ring);
                    break;
                }
            }
            string keyLabel=hover==null?"探索键位":hover.Label;
            RectangleF badge=new RectangleF(Cx+22,516,80,29);
            using(GraphicsPath path=RoundedRect(badge.X,badge.Y,badge.Width,badge.Height,7))
            using(SolidBrush b=new SolidBrush(ArtTheme.Mix(Ccard,Cblue,0.12))) g.FillPath(b,path);
            using(StringFormat center=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
            using(SolidBrush b=new SolidBrush(Ctext)) g.DrawString(ThemeArt.Active && hover==null?(EuroTruckArt.Active?"黄金方向盘":WuxiaArt.Active?"武林盟主":BalatroArt.Active?"王牌加冕":MinecraftArt.Active?"钻石成就":ResidentArt.Active?"幸存者徽章":HaloArt.Active?"斯巴达勋章":"大喵加冕"):keyLabel,_fBody,b,badge,center);
            string detail;
            if(hover==null) detail=holdMode?"蓝色低频 → 红色高频(按累计按住时长) · 灰色没有样本 · 悬停查看数值":"蓝色低频 → 红色高频 · 灰色为零次 · 悬停查看数值";
            else if(hover.Scan==0) detail="Fn 由硬件处理，无法统计";
            else if(holdMode) detail=hoverHold==null?"这个键还没有按住的样本":HoldReportData.Duration(hoverHold.TotalMs)+" 累计按住    /    占全部 "+(total>0?(100.0*hoverHold.TotalMs/total).ToString("0.0",CultureInfo.InvariantCulture):"0.0")+"%    ·    "+Analysis.FmtCount(hoverHold.Count)+" 次 · 平均 "+HoldReportData.Duration(hoverHold.Mean)+" · 最长 "+HoldReportData.Duration(hoverHold.MaxMs);
            else detail=Analysis.FmtCount(hoverCount)+" 次击键    /    占全部 "+(total>0?(100.0*hoverCount/total).ToString("0.0",CultureInfo.InvariantCulture):"0.0")+"%";
            if(ThemeArt.Active && hover==null && !holdMode)detail=max>0?favorite+" 是本场最爱 · "+Analysis.FmtCount(max)+" 次 · 金边键帽获得大喵徽章":"还没有击键记录，大喵等你点亮第一颗键。";
            if(ThemeArt.Dark && hover==null && !holdMode)detail=max>0?favorite+" 为核心按键 · "+Analysis.FmtCount(max)+" 次 · 金边标识当前最高频键":"终端待命 · 等待第一条击键记录。";
            if(EuroTruckArt.Active && hover==null && !holdMode)detail=max>0?favorite+" 驶上热键榜首 · "+Analysis.FmtCount(max)+" 次 · 获得黄金方向盘":"车队待命 · 等待第一条击键记录。";
            if(WuxiaArt.Active && hover==null && !holdMode)detail=max>0?favorite+" 登临键谱榜首 · "+Analysis.FmtCount(max)+" 次 · 朱砂金边封为盟主键":"江湖谱尚空 · 等待第一式落键。";
            AppText(g,detail,_fBody,Ctext,new RectangleF(Cx+114,519,445,24),false);
            // 1.8.0:把这条明细留给 Ctrl+C 复制(有悬停就复制悬停项,否则复制本页提示)。
            _hoverDetail = hover==null ? "" : keyLabel + " · " + detail;
            AppText(g,"配列覆盖 "+(total>0?(100.0*visible/total).ToString("0.0",CultureInfo.InvariantCulture):"0.0")+"%",_fSmall,Csub,new RectangleF(Cx+Cw-183,519,160,24),true);
            string note;
            if(_selectedKeyScan>=0) note="已下钻到单个键位 · 按 Esc 或点空白处回到全部按键 · 三格统计已切换为该键的数据。";
            else if(holdMode) note=unmappedHold>0?"含 "+HoldReportData.Duration(unmappedHold)+" 无法定位到键位(每天 Top 48 以外的按键) · 逐键明细按虚拟键归位，主区 Enter 与小键盘 Enter 合并。":"逐键明细按虚拟键归位：主区 Enter 与小键盘 Enter 合并，其余键位一一对应。";
            else note=legacy>0?"含 "+Analysis.FmtCount(legacy)+" 次旧记录，按键名近似归位；旧 Enter 无法区分主区与小键盘。":"按物理键位统计 · 标准 ANSI 示意，厂商自定义键位与 Fn 层可能不同。";
            AppText(g,note,_fSmall,Csub,new RectangleF(Cx+22,551,Cw-44,20),false);
        }
    }
}


