using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KeyMouseStats
{
    internal sealed class ShortcutTracker
    {
        private readonly HashSet<int> _held = new HashSet<int>();
        private readonly Dictionary<int, int> _physicalHeld = new Dictionary<int, int>();
        private readonly Dictionary<int, long> _physicalDownTicks = new Dictionary<int, long>();
        private readonly HashSet<int> _seeded = new HashSet<int>();

        /// <summary>
        /// 同一个物理键在「已经按住」的状态下再次收到按下时的间隔上限(毫秒)。
        /// Windows 的自动重复由系统输入栈产生:首次重复延迟最长 1000 ms(SPI_SETKEYBOARDDELAY 上界),
        /// 之后最慢约 400 ms 一次。间隔超过这个上界的「重复按下」只可能是上一次抬起被吞掉之后的
        /// 重新按下(Alt+Tab、Win 键、提权进程、游戏吞键)。阈值 1200 ms:高于任何自动重复间隔,
        /// 又足够低,能拦住绝大多数丢抬起后重新按下的场景。
        /// </summary>
        public const double RePressGapMs = 1200;

        public void Reset() { _held.Clear(); _physicalHeld.Clear(); _physicalDownTicks.Clear(); _seeded.Clear(); }
        // Called outside a hook callback: async state has not yet been updated inside the callback.
        public void Seed(Func<int, bool> isDown)
        {
            Reset();
            for (int key = 8; key < 255; key++)
                if (key != 16 && key != 17 && key != 18 && isDown(key)) { _held.Add(key); _seeded.Add(key); }
        }
        public static int Normalize(int vk, uint scan, uint flags)
        {
            if (vk == 16) return scan == 0x36 ? 0xA1 : 0xA0;
            if (vk == 17) return (flags & 1) != 0 ? 0xA3 : 0xA2;
            if (vk == 18) return (flags & 1) != 0 ? 0xA5 : 0xA4;
            return vk;
        }
        public static bool IsModifier(int vk) { return vk >= 0xA0 && vk <= 0xA5 || vk == 0x5B || vk == 0x5C; }
        public string Process(int message, int vk, uint scan, uint flags)
        {
            bool firstPress;
            return Process(message, vk, scan, flags, System.Diagnostics.Stopwatch.GetTimestamp(), out firstPress);
        }
        public string Process(int message, int vk, uint scan, uint flags, out bool firstPress)
        {
            return Process(message, vk, scan, flags, System.Diagnostics.Stopwatch.GetTimestamp(), out firstPress);
        }
        /// <summary>ticks 为单调时钟(Stopwatch.GetTimestamp),用于区分自动重复与丢失抬起后的重新按下。</summary>
        public string Process(int message, int vk, uint scan, uint flags, long ticks, out bool firstPress)
        {
            firstPress = false;
            bool down = message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN;
            bool up = message == Native.WM_KEYUP || message == Native.WM_SYSKEYUP;
            if ((!down && !up) || vk < 8 || vk >= 255) return null;
            vk = Normalize(vk, scan, flags);
            int physical = KeyboardHeat.ScanId(vk, scan, flags);
            if (physical == 0) physical = 0x10000 | vk;
            if (up)
            {
                int original;
                if (_physicalHeld.TryGetValue(physical, out original)) { _physicalHeld.Remove(physical); vk = original; }
                _physicalDownTicks.Remove(physical);
                _seeded.Remove(vk);
                if (!_physicalHeld.ContainsValue(vk)) _held.Remove(vk);
                return null;
            }
            if (_physicalHeld.ContainsKey(physical))
            {
                long lastDown;
                if (!_physicalDownTicks.TryGetValue(physical, out lastDown)) lastDown = ticks;
                if ((ticks - lastDown) * 1000.0 / System.Diagnostics.Stopwatch.Frequency <= RePressGapMs)
                {
                    // 自动重复:只刷新这个物理键的活跃时间,不产生新的击键,也不重置时长起点。
                    if (ticks > lastDown) _physicalDownTicks[physical] = ticks;
                    return null;
                }
                // 间隔远超自动重复的上界:上一次抬起被吞掉,这个键在我们眼里一直「按着」。
                // 当成一次全新的按下重新起算,否则这次抬起会和很旧的按下配对,产出一次虚假的长按,
                // 同时让这次真实敲击完全不被计数。
                _physicalHeld.Remove(physical);
                _physicalDownTicks.Remove(physical);
            }
            _physicalHeld[physical] = vk;
            _physicalDownTicks[physical] = ticks;
            if (_seeded.Contains(vk)) return null;
            _held.Add(vk);
            firstPress = true;
            if (IsModifier(vk)) return null;
            StringBuilder combo = new StringBuilder();
            if (_held.Contains(0xA2) || _held.Contains(0xA3)) combo.Append("ctrl_");
            if (_held.Contains(0xA4) || _held.Contains(0xA5)) combo.Append("alt_");
            if (_held.Contains(0x5B) || _held.Contains(0x5C)) combo.Append("win_");
            if (_held.Contains(0xA0) || _held.Contains(0xA1)) combo.Append("shift_");
            if (combo.Length == 0) return null;
            if (vk >= 0x41 && vk <= 0x5A) combo.Append(char.ToLowerInvariant((char)vk));
            else if (vk >= 0x30 && vk <= 0x39) combo.Append((char)vk);
            else if (vk == 9) combo.Append("tab");
            else combo.Append("vk").Append(vk.ToString("x2", CultureInfo.InvariantCulture));
            return combo.ToString();
        }
    }

    internal static class ShortcutStats
    {
        public static bool IsValid(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 40) return false;
            string[] parts = key.Split('_');
            if (parts.Length < 2 || parts.Length > 5) return false;
            string[] modifiers = { "ctrl", "alt", "win", "shift" };
            int previous = -1;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                int index = Array.IndexOf(modifiers, parts[i]);
                if (index <= previous) return false;
                previous = index;
            }
            string last = parts[parts.Length - 1];
            if (last == "tab") return true;
            if (last.Length == 1 && (last[0] >= 'a' && last[0] <= 'z' || last[0] >= '0' && last[0] <= '9')) return true;
            int vk;
            return last.Length == 4 && last.StartsWith("vk") && int.TryParse(last.Substring(2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out vk) && vk >= 8 && vk < 255 && vk != 16 && vk != 17 && vk != 18 && !ShortcutTracker.IsModifier(vk);
        }
        public static string Display(string key)
        {
            string[] parts = key.Split('_');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "ctrl") parts[i] = "Ctrl";
                else if (parts[i] == "alt") parts[i] = "Alt";
                else if (parts[i] == "shift") parts[i] = "Shift";
                else if (parts[i] == "win") parts[i] = "Win";
                else if (parts[i] == "tab") parts[i] = "Tab";
                else if (parts[i].StartsWith("vk")) parts[i] = Analysis.KeyName(int.Parse(parts[i].Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                else parts[i] = parts[i].ToUpperInvariant();
            }
            return string.Join("+", parts);
        }
        public static void Add(DayRecord day, string key)
        {
            if (key == null) return;
            long count; day.ComboCounts.TryGetValue(key, out count); day.ComboCounts[key] = count + 1;
        }
        public static void Load(DayRecord day, string text)
        {
            int split = text.IndexOf(':');
            if (split <= 0) return;
            string key = text.Substring(0, split);
            long count;
            if (IsValid(key) && long.TryParse(text.Substring(split + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out count) && count > 0)
                day.ComboCounts[key] = count;
        }
        public static List<KeyValuePair<string, long>> Ranking(IEnumerable<DayRecord> days)
        {
            Dictionary<string, long> sums = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (DayRecord day in days)
                foreach (KeyValuePair<string, long> combo in day.ComboCounts)
                { long count; sums.TryGetValue(combo.Key, out count); sums[combo.Key] = count + combo.Value; }
            List<KeyValuePair<string, long>> rows = new List<KeyValuePair<string, long>>();
            foreach (KeyValuePair<string, long> combo in sums) rows.Add(new KeyValuePair<string, long>(Display(combo.Key), combo.Value));
            rows.Sort(delegate(KeyValuePair<string, long> a, KeyValuePair<string, long> b)
            { int order = b.Value.CompareTo(a.Value); return order == 0 ? string.CompareOrdinal(a.Key, b.Key) : order; });
            return rows;
        }
        public static string Export(IEnumerable<DayRecord> days)
        {
            StringBuilder text = new StringBuilder("日期,组合键,标识,动作次数\r\n");
            List<DayRecord> sorted = new List<DayRecord>(days);
            sorted.Sort(delegate(DayRecord a, DayRecord b) { return a.Date.CompareTo(b.Date); });
            foreach (DayRecord day in sorted)
            {
                List<string> keys = new List<string>(day.ComboCounts.Keys); keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys)
                    text.Append(day.Date.ToString("yyyy-MM-dd")).Append(',').Append(AppActivity.CsvCell(Display(key)))
                        .Append(',').Append(key).Append(',').Append(day.ComboCounts[key].ToString(CultureInfo.InvariantCulture)).AppendLine();
            }
            return text.ToString();
        }
    }
}

namespace KeyMouseStats
{
    internal static class ShortcutSavings
    {
        public static double MenuSeconds=2,ShortcutSeconds=.5;public static int MenuClicks=2;
        private static readonly string[] Keys={"ctrl_c","ctrl_v","ctrl_x","ctrl_z","ctrl_s","ctrl_a","ctrl_f","ctrl_p"};
        private static readonly string[] Actions={"复制","粘贴","剪切","撤销","保存","全选","查找","打印"};
        public static void LoadModel(string text)
        {
            string[] f=text.Split('|');double menu,key;int clicks;
            if(f.Length==3&&double.TryParse(f[0],NumberStyles.Float,CultureInfo.InvariantCulture,out menu)&&double.TryParse(f[1],NumberStyles.Float,CultureInfo.InvariantCulture,out key)&&int.TryParse(f[2],out clicks)&&menu>=0&&menu<=60&&key>=0&&key<=60&&clicks>=0&&clicks<=20){MenuSeconds=menu;ShortcutSeconds=key;MenuClicks=clicks;}
        }
        public static string EncodeModel(){return MenuSeconds.ToString("R",CultureInfo.InvariantCulture)+"|"+ShortcutSeconds.ToString("R",CultureInfo.InvariantCulture)+"|"+MenuClicks;}
        public static CrossReportData Build(DayRecord day)
        {
            CrossReportData data=new CrossReportData{Title="快捷键节省",Caption="预计节省时间贡献 · Top 6",Headings=new[]{"组合 / 假设动作","触发次数","预计少用点击","预计省时（秒）","估算口径"}};
            double all=0,matched=0,total=0,delta=Math.Max(0,MenuSeconds-ShortcutSeconds);
            if(day!=null)foreach(System.Collections.Generic.KeyValuePair<string,long> pair in day.ComboCounts)
            {
                if(pair.Value<=0)continue;all+=pair.Value;int i=Array.IndexOf(Keys,pair.Key);
                if(i<0){data.Rows.Add(new[]{ShortcutStats.Display(pair.Key),pair.Value.ToString(),"—","—","未建模，不计入节省量"});continue;}
                matched+=pair.Value;double saved=pair.Value*delta;total+=saved;
                string name=ShortcutStats.Display(pair.Key)+" · "+Actions[i];
                data.Rows.Add(new[]{name,pair.Value.ToString(),(pair.Value*(double)MenuClicks).ToString("R",CultureInfo.InvariantCulture),saved.ToString("R",CultureInfo.InvariantCulture),"假设该组合用于对应菜单命令，未验证执行成功"});
                data.Chart.Add(new CrossChartRow{Name=name,Values=new[]{saved},Detail=name+" · "+pair.Value+" 次 · 预计少用 "+(pair.Value*(double)MenuClicks).ToString("N0")+" 次鼠标点击 · 每次预计省 "+delta.ToString("0.##")+" 秒"});
            }
            foreach(CrossChartRow row in data.Chart)row.Total=total;
            data.Chart.Sort(delegate(CrossChartRow a,CrossChartRow b){return b.Values[0].CompareTo(a.Values[0]);});
            data.Cards=new[]{"匹配快捷键次数","预计少用鼠标点击","预计节省时间","模型覆盖组合次数"};data.CardValues=new[]{matched.ToString("N0"),matched>0?"≈ "+(matched*MenuClicks).ToString("N0"):"—",matched>0?"≈ "+ActivityMonitor.FormatDuration(total):"—",all>0?(matched*100/all).ToString("0.#")+"%":"—"};
            data.Note="模型只匹配 Ctrl+C/V/X/Z/S/A/F/P，假设其用于复制、粘贴、剪切、撤销、保存、全选、查找、打印。实际应用可能重映射或禁用这些组合，采集未验证命令是否成功。\\n每次省时 = max(0, 菜单耗时 − 快捷键耗时)，预计少用点击 = 次数 × 菜单点击数。参数是可编辑假设，不是人群基准或计时测量；未匹配组合不估算，不计入实际活跃时长。".Replace("\\n","\n");
            data.Footer=matched>0?"当前假设：菜单 "+MenuSeconds.ToString("0.##")+" 秒 / "+MenuClicks+" 次点击，快捷键 "+ShortcutSeconds.ToString("0.##")+" 秒；不会从有效使用时长扣除。":"暂无可估算的快捷键；未匹配组合在明细中保留。";
            if(data.Rows.Count==0)data.Rows.Add(new[]{"暂无组合键记录","—","—","—","使用中逐步累积"});
            return data;
        }
    }
    internal sealed class ShortcutSavingsSettings : ThemedDialog
    {
        public ShortcutSavingsSettings()
        {
            Text="快捷键估算模型";Font=new System.Drawing.Font("Microsoft YaHei UI",10);AutoScaleMode=System.Windows.Forms.AutoScaleMode.Dpi;ClientSize=new System.Drawing.Size(540,310);FormBorderStyle=System.Windows.Forms.FormBorderStyle.FixedDialog;StartPosition=System.Windows.Forms.FormStartPosition.CenterParent;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
            string[] labels={"鼠标菜单耗时（秒 / 次）","快捷键耗时（秒 / 次）","鼠标菜单点击数（次 / 操作）"};System.Windows.Forms.NumericUpDown[] inputs=new System.Windows.Forms.NumericUpDown[3];
            for(int i=0;i<3;i++){Controls.Add(new System.Windows.Forms.Label{Text=labels[i],Location=new System.Drawing.Point(20,26+i*48),Size=new System.Drawing.Size(330,30)});inputs[i]=new System.Windows.Forms.NumericUpDown{Location=new System.Drawing.Point(365,24+i*48),Size=new System.Drawing.Size(140,30),DecimalPlaces=i==2?0:2,Minimum=0,Maximum=i==2?20:60,Increment=i==2?1:.25m,Value=(decimal)(i==0?ShortcutSavings.MenuSeconds:i==1?ShortcutSavings.ShortcutSeconds:ShortcutSavings.MenuClicks)};Controls.Add(inputs[i]);}
            Controls.Add(new System.Windows.Forms.Label{Text="这些值是你的场景假设；会重新计算所选日的估算。\n不修改历史击键、点击或有效使用时间。",Location=new System.Drawing.Point(20,176),Size=new System.Drawing.Size(500,60)});
            System.Windows.Forms.Button save=new System.Windows.Forms.Button{Text="保存模型",Location=new System.Drawing.Point(300,260),Size=new System.Drawing.Size(100,32)},cancel=new System.Windows.Forms.Button{Text="取消",DialogResult=System.Windows.Forms.DialogResult.Cancel,Location=new System.Drawing.Point(410,260),Size=new System.Drawing.Size(100,32)};
            save.Click+=delegate{ShortcutSavings.MenuSeconds=(double)inputs[0].Value;ShortcutSavings.ShortcutSeconds=(double)inputs[1].Value;ShortcutSavings.MenuClicks=(int)inputs[2].Value;Store.Save();DialogResult=System.Windows.Forms.DialogResult.OK;Close();};Controls.Add(save);Controls.Add(cancel);AcceptButton=save;CancelButton=cancel;
        }
    }
}
