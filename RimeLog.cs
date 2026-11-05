using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KeyMouseStats
{
    // ============================================================================
    //  Rime 输入法上屏日志(commit_log.csv)的只读后处理
    //
    //  这个文件回答的是与键鼠计数完全不同的问题:键鼠统计只记「按了多少次」,
    //  这里读的是「上屏了什么」。两者的唯一共同点都是「本地、只读、不联网」。
    //
    //  边界(刻意设计,不是偷懒):
    //  * 本类不引用 Store / DayRecord / AppActivity —— 字符内容不参与键鼠计数,
    //    也不与前台应用、窗口标题产生任何复合键;
    //  * 只读用户输入法自己写的文件,程序自身仍不记录任何输入文字(见 SECURITY.md);
    //  * 解析失败的行只计数,不抛异常、不中断,坏一行不影响整页。
    // ============================================================================

    /// <summary>一次上屏:Rime 每提交一段文字就往 CSV 追加一行。</summary>
    internal sealed class RimeCommit
    {
        public DateTime Time;
        public string Text;
        public int Chars;
    }

    /// <summary>字符编码工具。.NET Framework 4.x 没有 Rune,这里自己按码点走。</summary>
    internal static class RimeText
    {
        /// <summary>按 Unicode 码点计数(代理对算 1 个,孤立代理项各算 1 个)。</summary>
        public static int CountCodePoints(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
                count++;
            }
            return count;
        }

        /// <summary>把码点枚举出来(用于字符频次)。换行与回车不算字符。</summary>
        public static List<int> CodePoints(string text)
        {
            List<int> list = new List<int>();
            if (string.IsNullOrEmpty(text)) return list;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r' || c == '\n') continue;
                int cp;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(c, text[i + 1]);
                    i++;
                }
                else cp = c;
                list.Add(cp);
            }
            return list;
        }

        /// <summary>显示用:码点 → 字符串。非法码点降级成替换符,绝不让页面抛异常。</summary>
        public static string Display(int codePoint)
        {
            if (codePoint < 0) return "?";
            try { return char.ConvertFromUtf32(codePoint); }
            catch (ArgumentOutOfRangeException) { return "?"; }
        }

        /// <summary>「标点」这一类在 Classify / RimeSlice.Composition 里的下标。符号(IsSymbol)也算这一类。</summary>
        public const int PunctuationClass = 1;

        /// <summary>字符构成分类。顺序要紧:先认 CJK,再看标点,最后才是字母数字。</summary>
        public static int Classify(int codePoint)
        {
            if (codePoint == ' ' || codePoint == '\t' || codePoint == 0x3000) return 3;
            if (IsCjk(codePoint)) return 0;
            if (codePoint < 0 || codePoint > 0xFFFF) return 4;   // 扩展区以外的 emoji 等
            char c = (char)codePoint;
            if (char.IsPunctuation(c) || char.IsSymbol(c)) return 1;
            if (char.IsLetterOrDigit(c)) return 2;
            return 4;
        }

        /// <summary>是否汉字(含扩展 A/B 及之后的扩展区、兼容区)。CJK 标点不算。</summary>
        public static bool IsCjk(int codePoint)
        {
            return (codePoint >= 0x4E00 && codePoint <= 0x9FFF)     // 基本区
                || (codePoint >= 0x3400 && codePoint <= 0x4DBF)     // 扩展 A
                || (codePoint >= 0x20000 && codePoint <= 0x2FA1F)   // 扩展 B 及之后
                || (codePoint >= 0xF900 && codePoint <= 0xFAFF);    // 兼容汉字
        }

        /// <summary>字符是否值得单独一行展示(空白与不可见控制符换成别名)。</summary>
        public static string DisplayName(int codePoint)
        {
            if (codePoint == ' ') return "空格";
            if (codePoint == '\t') return "Tab";
            if (codePoint < 0x20) return "控制符";
            return Display(codePoint);
        }
    }

    /// <summary>一天的字数/上屏次数(用于每日趋势)。</summary>
    internal struct RimeDay
    {
        public DateTime Day;
        public long Chars;
        public long Commits;
    }

    /// <summary>某个日期区间的聚合视图。页面上画什么,这里就只算什么。</summary>
    internal sealed class RimeSlice
    {
        public DateTime From, To;
        /// <summary>区间内的上屏字数与上屏次数(原口径:含标点,换行不计)。</summary>
        public long Chars;
        public long Commits;
        /// <summary>不含标点的字数,与「含文字的上屏次数」配套,只服务于「平均每次字数」这张卡。</summary>
        public long TextChars;
        public long TextCommits;
        public int ActiveDays;
        public readonly Dictionary<int, long> CharCounts = new Dictionary<int, long>();
        /// <summary>连续汉字串里切出的 2–4 字词组,用于「高频词组」页签。</summary>
        public readonly Dictionary<string, long> PhraseCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        public readonly long[] HourChars = new long[24];
        public readonly long[] HourCommits = new long[24];
        /// <summary>词长分布:下标 1..7 是真实长度,下标 8 表示「8 个及以上」;下标 0 未用。</summary>
        public readonly long[] WordLen = new long[9];
        /// <summary>区间内最近的上屏(新→旧),最多保留 40 条。</summary>
        public readonly List<RimeCommit> Recent = new List<RimeCommit>();
        public double PeakPerMinute;
        /// <summary>字符构成:0 汉字 / 1 标点 / 2 字母数字 / 3 空白 / 4 其他。按码点统计。</summary>
        public readonly long[] Composition = new long[5];
        /// <summary>有上屏的分钟数。用它算「平均每分钟产出」,比按自然时长摊薄更诚实。</summary>
        public int ActiveMinutes;
        /// <summary>区间内出现过的汉字种类数(去重后)。「输入了多少种文字」的一半答案。</summary>
        public int DistinctHan;

        /// <summary>区间内一共出现过多少种不同的字符(去重后,按码点算)。</summary>
        public int DistinctChars
        {
            get { return CharCounts.Count; }
        }
        /// <summary>最长的一段连续输入:持续分钟数与该段字数(相邻两次相隔超过 120 秒就断开)。</summary>
        public int LongestBurstMinutes;
        public long LongestBurstChars;

        /// <summary>构成分类的名字,与 Composition 的下标一一对应。</summary>
        public static readonly string[] CompositionNames = { "汉字", "标点", "字母数字", "空白", "其他" };

        /// <summary>构成占比(0-1)。分母是码点总数,与卡片上的「字数」可能差几个换行符。</summary>
        public double CompositionShare(int index)
        {
            long total = 0;
            for (int i = 0; i < Composition.Length; i++) total += Composition[i];
            if (total <= 0 || index < 0 || index >= Composition.Length) return 0;
            return Composition[index] / (double)total;
        }

        public long CompositionTotal
        {
            get
            {
                long total = 0;
                for (int i = 0; i < Composition.Length; i++) total += Composition[i];
                return total;
            }
        }

        /// <summary>平均每分钟产出字数(只按有上屏的分钟算)。</summary>
        public double CharsPerActiveMinute
        {
            get { return ActiveMinutes > 0 ? Chars / (double)ActiveMinutes : 0; }
        }

        public bool HasData { get { return Commits > 0; } }

        /// <summary>平均每次上屏的字数。空区间返回 0,调用方自己决定显示 "--"。</summary>
        public double AverageChars
        {
            get { return Commits > 0 ? Chars / (double)Commits : 0; }
        }

        /// <summary>
        /// 平均每次上屏的字数,**不含标点**:标点不计入字数,一次只上屏标点的提交也不计入次数。
        /// 中文输入法里「，」「。」通常单独提交一次,把它们算进次数会把平均值系统性拉低。
        /// 空区间(或区间内只有标点)返回 0,调用方自己决定显示 "--"。
        /// </summary>
        public double AverageTextChars
        {
            get { return TextCommits > 0 ? TextChars / (double)TextCommits : 0; }
        }

        /// <summary>有数据的天数的日均字数(不是自然日均,没打字的日子不该拉低它)。</summary>
        public double AverageCharsPerActiveDay
        {
            get { return ActiveDays > 0 ? Chars / (double)ActiveDays : 0; }
        }

        /// <summary>字数最多的那个小时(0-23);没有数据返回 -1。并列时取更早的那个。</summary>
        public int BusiestHour()
        {
            int best = -1;
            long max = 0;
            for (int i = 0; i < 24; i++)
            {
                if (HourChars[i] <= max) continue;
                max = HourChars[i];
                best = i;
            }
            return best;
        }

        /// <summary>Top 字符,按次数降序。并列时按码点升序,保证每次绘制顺序稳定。</summary>
        public List<KeyValuePair<int, long>> TopChars(int limit)
        {
            List<KeyValuePair<int, long>> list = new List<KeyValuePair<int, long>>(CharCounts);
            list.Sort(delegate(KeyValuePair<int, long> a, KeyValuePair<int, long> b)
            {
                int byCount = b.Value.CompareTo(a.Value);
                return byCount != 0 ? byCount : a.Key.CompareTo(b.Key);
            });
            if (limit > 0 && list.Count > limit) list.RemoveRange(limit, list.Count - limit);
            return list;
        }

        /// <summary>高频词组:从每次上屏的连续汉字串里取 2–4 字滑窗,按出现次数排序。</summary>
        public List<KeyValuePair<string, long>> TopPhrases(int limit)
        {
            List<KeyValuePair<string, long>> list = new List<KeyValuePair<string, long>>(PhraseCounts);
            list.Sort(delegate(KeyValuePair<string, long> a, KeyValuePair<string, long> b)
            {
                int byCount = b.Value.CompareTo(a.Value);
                return byCount != 0 ? byCount : string.CompareOrdinal(a.Key, b.Key);
            });
            if (limit > 0 && list.Count > limit) list.RemoveRange(limit, list.Count - limit);
            return list;
        }

        /// <summary>把一次上屏文本切成 2–4 字词组;只取连续汉字段,标点/字母处断开。</summary>
        public static void AddPhrases(string text, RimeSlice slice)
        {
            if (string.IsNullOrEmpty(text) || slice == null) return;
            List<int> points = RimeText.CodePoints(text);
            List<int> run = new List<int>();
            for (int i = 0; i <= points.Count; i++)
            {
                bool cjk = i < points.Count && RimeText.IsCjk(points[i]);
                if (cjk) { run.Add(points[i]); continue; }
                if (run.Count >= 2)
                {
                    for (int length = 2; length <= 4 && length <= run.Count; length++)
                        for (int start = 0; start + length <= run.Count; start++)
                        {
                            StringBuilder phrase = new StringBuilder(length * 2);
                            for (int k = 0; k < length; k++) phrase.Append(char.ConvertFromUtf32(run[start + k]));
                            string key = phrase.ToString();
                            long value;
                            slice.PhraseCounts.TryGetValue(key, out value);
                            slice.PhraseCounts[key] = value + 1;
                        }
                }
                run.Clear();
            }
        }

    }

    /// <summary>
    /// commit_log.csv 的完整解析结果。一次读取、多处切片:
    /// 换区间不需要重新读盘,只重算聚合(见 <see cref="Slice"/>)。
    /// </summary>
    internal sealed class RimeTextStats
    {
        /// <summary>只解析文件尾部的这么多字节。日志长期运行会到几十 MB,整份读会卡住 UI 线程,
        /// 而 UI 线程同时还在跑低级键鼠钩子 —— 停顿超过 300 ms 会被系统摘钩子(见 P0-2)。</summary>
        public const long TailBytes = 8L * 1024 * 1024;

        /// <summary>内存里最多保留多少条上屏。超过就丢最旧的,并在页面上标注。</summary>
        public const int MaxCommits = 200000;

        /// <summary>最近上屏列表保留条数。</summary>
        public const int RecentKeep = 40;

        /// <summary>
        /// 字符内容页「自动重读」的可选项(秒)。0 表示手动:只有文件改动(mtime 变化)时才重读。
        /// 总览是 3 / 10 / 15 / 30 / 60 / 手动 —— 最快一档仍比绘制节拍(1 秒)慢,
        /// 这样连点鼠标也不会把 8 MiB 的尾读排成队列。
        /// </summary>
        public static readonly int[] RefreshOptions = { 3, 10, 15, 30, 60, 0 };

        /// <summary>把任意写入值收进合法范围:0 或负数 = 手动;其余夹在 2 秒 ~ 1 小时。</summary>
        public static int NormalizeRefresh(int seconds)
        {
            if (seconds <= 0) return 0;
            if (seconds < 2) return 2;
            if (seconds > 3600) return 3600;
            return seconds;
        }

        /// <summary>设置菜单与页脚上的显示名。</summary>
        public static string RefreshLabel(int seconds)
        {
            return seconds <= 0 ? "手动" : seconds.ToString(CultureInfo.InvariantCulture) + " 秒";
        }

        public readonly List<RimeCommit> Commits = new List<RimeCommit>();
        public readonly Dictionary<DateTime, long> DayChars = new Dictionary<DateTime, long>();
        public readonly Dictionary<DateTime, long> DayCommits = new Dictionary<DateTime, long>();

        public string Source = "";
        public DateTime ReadAt;
        /// <summary>文件字节数。刷新时与 mtime 一起比较:时间戳可能被缓存,体积不会。</summary>
        public long FileBytes;
        /// <summary>实际读到的记录条数(含表头与坏行),用来和页脚「已读 N 条」对上。</summary>
        public long RecordLines;
        public DateTime? FirstDay, LastDay;
        /// <summary>最后一条上屏的时间。页脚用它显示「数据到哪一刻」。</summary>
        public DateTime? LastCommit;
        public long TotalChars, TotalCommits;
        public long BadLines, HeaderLines;
        public bool Missing, Truncated;
        public string Error = "";

        public bool HasData { get { return TotalCommits > 0; } }

        /// <summary>测试/预览用:非空时覆盖默认日志路径,不读写用户真实日志。</summary>
        internal static string PathOverride { get; set; }

        /// <summary>Rime 的默认日志位置(%APPDATA%\Rime\commit_log.csv)。</summary>
        public static string DefaultPath
        {
            get
            {
                if (!string.IsNullOrEmpty(PathOverride)) return PathOverride;
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(roaming)) return "";
                return Path.Combine(Path.Combine(roaming, "Rime"), "commit_log.csv");
            }
        }

        /// <summary>读默认位置。找不到文件不算错误 —— 那是「没开这个功能」,页面上说清楚就行。</summary>
        public static RimeTextStats Load()
        {
            return Load(DefaultPath);
        }

        public static RimeTextStats Load(string path)
        {
            RimeTextStats stats = new RimeTextStats();
            stats.Source = path == null ? "" : path;
            stats.ReadAt = DateTime.Now;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                stats.Missing = true;
                return stats;
            }

            FileStream stream = null;
            try
            {
                // FileShare.ReadWrite:Rime 可能正在往这个文件里追加,不能锁住它。
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stats.FileBytes = stream.Length;
                long start = 0;
                if (stream.Length > TailBytes)
                {
                    start = stream.Length - TailBytes;
                    stats.Truncated = true;
                }
                stream.Seek(start, SeekOrigin.Begin);
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 8192))
                {
                    stream = null;   // 交给 reader 释放
                    bool firstLine = true;
                    string line;
                    // 一条记录可能跨多行:被引号包住的明文里如果有换行,ReadLine 会在半路断开。
                    // 见到未闭合的引号就继续拼下一行,拼完整了再解析 —— 否则那一条会被记成坏行。
                    StringBuilder record = null;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (firstLine)
                        {
                            firstLine = false;
                            // 从文件中段开始读:第一行几乎必然是半行,直接丢。
                            if (start > 0) continue;
                        }
                        if (record == null)
                        {
                            if (line.Length == 0) continue;
                            record = new StringBuilder(line);
                        }
                        else
                        {
                            record.Append('\n').Append(line);
                        }
                        if (HasOpenQuote(record.ToString())) continue;

                        string text = record.ToString();
                        record = null;
                        stats.RecordLines++;
                        RimeCommit commit = ParseLine(text);
                        if (commit != null) { stats.Add(commit); continue; }
                        // 表头只可能是第一条有效行之前的那一行。不能只看关键字:
                        // 一条时间戳写坏的记录里也可能出现「时间」两个字,那属于坏行。
                        if (stats.TotalCommits == 0 && stats.HeaderLines == 0 && LooksLikeHeader(text))
                            stats.HeaderLines++;
                        else stats.BadLines++;
                    }
                    // 文件在引号中间结束时,剩下这段仍然尽力解析一次,不整段丢掉。
                    if (record != null && record.Length > 0)
                    {
                        stats.RecordLines++;
                        RimeCommit commit = ParseLine(record.ToString());
                        if (commit != null) stats.Add(commit); else stats.BadLines++;
                    }
                }
            }
            catch (Exception ex)
            {
                stats.Error = ex.Message;
            }
            finally
            {
                if (stream != null)
                {
                    try { stream.Dispose(); } catch (Exception) { }
                }
            }
            stats.Finish();
            return stats;
        }

        private void Add(RimeCommit commit)
        {
            Commits.Add(commit);
            TotalChars += commit.Chars;
            TotalCommits++;

            DateTime day = commit.Time.Date;
            long value;
            DayChars.TryGetValue(day, out value);
            DayChars[day] = value + commit.Chars;
            DayCommits.TryGetValue(day, out value);
            DayCommits[day] = value + 1;

            // 超过上限就成批丢最旧的。逐条 RemoveAt(0) 是 O(n),成批才划算。
            if (Commits.Count > MaxCommits + 4096)
            {
                Commits.RemoveRange(0, 4096);
                Truncated = true;
            }
        }

        private void Finish()
        {
            FirstDay = null;
            LastDay = null;
            LastCommit = null;
            DayChars.Clear();
            DayCommits.Clear();
            TotalChars = 0;
            TotalCommits = 0;
            // 成批丢弃后按实际留下的重算一遍,避免首尾日期与总数对不上。
            for (int i = 0; i < Commits.Count; i++)
            {
                RimeCommit commit = Commits[i];
                TotalChars += commit.Chars;
                TotalCommits++;
                DateTime day = commit.Time.Date;
                long value;
                DayChars.TryGetValue(day, out value);
                DayChars[day] = value + commit.Chars;
                DayCommits.TryGetValue(day, out value);
                DayCommits[day] = value + 1;
                if (FirstDay == null || day < FirstDay.Value) FirstDay = day;
                if (LastDay == null || day > LastDay.Value) LastDay = day;
                if (LastCommit == null || commit.Time > LastCommit.Value) LastCommit = commit.Time;
            }
        }

        /// <summary>
        /// 这条记录里是否还有没闭合的引号(用于把被换行截断的 CSV 记录拼回去)。
        /// 成对的 "" 是转义,不算开关;只数真正的开合。
        /// </summary>
        internal static bool HasOpenQuote(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            bool open = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '"') continue;
                if (open && i + 1 < text.Length && text[i + 1] == '"') { i++; continue; }
                open = !open;
            }
            return open;
        }

        /// <summary>一行 CSV → 一次上屏。任何字段不可用都返回 null,由调用方记坏行。</summary>
        internal static RimeCommit ParseLine(string line)
        {
            List<string> fields = SplitCsv(line);
            if (fields.Count < 3) return null;

            DateTime time;
            string stamp = fields[0].Trim();
            if (!DateTime.TryParseExact(stamp, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out time)
                && !DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                return null;

            // 明文里带英文逗号、而写入方没有加引号时,后面的字段都属于明文 —— 直接拼回去。
            // 只用 fields[2] 会把「a,b」截成「a」:次数没错,但字符与词频会少一截。
            string text;
            if (fields.Count > 3)
            {
                StringBuilder joined = new StringBuilder(fields[2]);
                for (int i = 3; i < fields.Count; i++) joined.Append(',').Append(fields[i]);
                text = joined.ToString();
            }
            else text = fields[2];
            if (text == null) text = "";
            // 单次上屏不该有几百个字符;真出现这种行,大概率是文件坏了,截断防御而不是崩掉。
            if (text.Length > 512) text = text.Substring(0, 512);

            int chars;
            if (!int.TryParse(fields[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out chars)
                || chars <= 0 || chars > 100000)
                chars = RimeText.CountCodePoints(text);

            RimeCommit commit = new RimeCommit();
            commit.Time = time;
            commit.Text = text;
            commit.Chars = chars;
            return commit;
        }

        private static bool LooksLikeHeader(string line)
        {
            return line.IndexOf("时间", StringComparison.Ordinal) >= 0
                || line.IndexOf("汉字", StringComparison.Ordinal) >= 0
                || line.IndexOf("明文", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 拆一行 CSV。支持双引号包裹与 "" 转义 —— 上屏文本里出现逗号、引号、换行都很正常,
        /// 用 Split(',') 会在第一次遇到中文标点里的英文逗号时把整页数字弄错。
        /// </summary>
        internal static List<string> SplitCsv(string line)
        {
            List<string> parts = new List<string>(4);
            if (line == null) return parts;
            StringBuilder current = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                        else quoted = false;
                    }
                    else current.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { parts.Add(current.ToString()); current.Length = 0; }
                else current.Append(c);
            }
            parts.Add(current.ToString());
            return parts;
        }

        /// <summary>切出 [from, to] 的聚合视图(按日期,含两端)。</summary>
        public RimeSlice Slice(DateTime from, DateTime to)
        {
            RimeSlice slice = new RimeSlice();
            slice.From = from.Date;
            slice.To = to.Date;
            Dictionary<DateTime, bool> activeDays = new Dictionary<DateTime, bool>();
            Dictionary<DateTime, long> minutes = new Dictionary<DateTime, long>();
            DateTime burstStart = DateTime.MinValue, burstLast = DateTime.MinValue;
            long burstChars = 0;
            int burstCount = 0;

            for (int i = 0; i < Commits.Count; i++)
            {
                RimeCommit commit = Commits[i];
                DateTime day = commit.Time.Date;
                if (day < slice.From || day > slice.To) continue;

                slice.Chars += commit.Chars;
                slice.Commits++;
                slice.HourChars[commit.Time.Hour] += commit.Chars;
                slice.HourCommits[commit.Time.Hour]++;
                activeDays[day] = true;

                List<int> points = RimeText.CodePoints(commit.Text);
                int textCount = 0;
                for (int k = 0; k < points.Count; k++)
                {
                    long value;
                    if (!slice.CharCounts.TryGetValue(points[k], out value) && RimeText.IsCjk(points[k]))
                        slice.DistinctHan++;
                    slice.CharCounts[points[k]] = value + 1;
                    int kind = RimeText.Classify(points[k]);
                    slice.Composition[kind]++;
                    // 「平均每次字数」只数非标点字符:标点不算字,只上屏标点的那一次也不算次数。
                    if (kind != RimeText.PunctuationClass) textCount++;
                }
                slice.TextChars += textCount;
                if (textCount > 0) slice.TextCommits++;
                RimeSlice.AddPhrases(commit.Text, slice);

                int length = points.Count;
                if (length > 0)
                {
                    int bucket = length >= 8 ? 8 : length;
                    slice.WordLen[bucket]++;
                }

                // 只留最新的若干条。以前这里写的是「先塞满 40 条,之后只替换最后一个槽」,
                // 结果文件一超过 40 行,列表就永远是「最旧的 39 条 + 最新 1 条」,
                // 排序后看起来就是「19:24 之后突然跳回 16:28」。现在改成滚动窗口:进来一条丢最旧一条。
                slice.Recent.Add(commit);
                if (slice.Recent.Count > RecentKeep)
                    slice.Recent.RemoveRange(0, slice.Recent.Count - RecentKeep);

                DateTime minute = new DateTime(commit.Time.Year, commit.Time.Month, commit.Time.Day,
                    commit.Time.Hour, commit.Time.Minute, 0);
                long inMinute;
                minutes.TryGetValue(minute, out inMinute);
                minutes[minute] = inMinute + commit.Chars;

                // 连续输入段:相邻两次上屏间隔超过 120 秒就断开。
                // 日志通常按时间升序,乱序时把负间隔按 0 处理,不让它把整段拆开。
                if (burstCount == 0)
                {
                    burstStart = commit.Time;
                    burstLast = commit.Time;
                    burstChars = commit.Chars;
                    burstCount = 1;
                }
                else
                {
                    double gapSeconds = (commit.Time - burstLast).TotalSeconds;
                    if (gapSeconds < 0) gapSeconds = 0;
                    if (gapSeconds > BurstGapSeconds)
                    {
                        CloseBurst(slice, burstStart, burstLast, burstChars);
                        burstStart = commit.Time;
                        burstChars = commit.Chars;
                        burstCount = 1;
                    }
                    else
                    {
                        burstChars += commit.Chars;
                        burstCount++;
                    }
                    burstLast = commit.Time;
                }
            }

            if (burstCount > 0) CloseBurst(slice, burstStart, burstLast, burstChars);

            foreach (KeyValuePair<DateTime, long> pair in minutes)
                if (pair.Value > slice.PeakPerMinute) slice.PeakPerMinute = pair.Value;

            slice.ActiveMinutes = minutes.Count;
            slice.ActiveDays = activeDays.Count;
            slice.Recent.Sort(delegate(RimeCommit a, RimeCommit b) { return b.Time.CompareTo(a.Time); });
            if (slice.Recent.Count > 12) slice.Recent.RemoveRange(12, slice.Recent.Count - 12);
            return slice;
        }

        /// <summary>相邻两次上屏超过这个秒数就算另一段。</summary>
        private const double BurstGapSeconds = 120;

        /// <summary>结算一段连续输入,保留最长的那一段。</summary>
        private static void CloseBurst(RimeSlice slice, DateTime start, DateTime end, long chars)
        {
            int minutes = (int)Math.Ceiling((end - start).TotalMinutes);
            if (minutes < 1) minutes = 1;
            if (minutes > slice.LongestBurstMinutes)
            {
                slice.LongestBurstMinutes = minutes;
                slice.LongestBurstChars = chars;
            }
        }

        /// <summary>[from, to] 的每日序列,缺数据的日期补 0(趋势图需要连续的横轴)。</summary>
        public List<RimeDay> DaySeries(DateTime from, DateTime to)
        {
            List<RimeDay> list = new List<RimeDay>();
            DateTime start = from.Date, end = to.Date;
            if (start > end) return list;
            // 防御:区间异常大时最多给 400 天,别让一次绘制分配上万项。
            if ((end - start).TotalDays > 400) start = end.AddDays(-400);
            for (DateTime day = start; day <= end; day = day.AddDays(1))
            {
                RimeDay item = new RimeDay();
                item.Day = day;
                long value;
                DayChars.TryGetValue(day, out value);
                item.Chars = value;
                DayCommits.TryGetValue(day, out value);
                item.Commits = value;
                list.Add(item);
            }
            return list;
        }

        /// <summary>导出区间内的原始上屏记录(时间/字符数/明文)。</summary>
        public static string Export(RimeTextStats stats, DateTime from, DateTime to)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("时间点,字符数,明文");
            if (stats == null) return text.ToString();
            DateTime start = from.Date, end = to.Date;
            for (int i = 0; i < stats.Commits.Count; i++)
            {
                RimeCommit commit = stats.Commits[i];
                DateTime day = commit.Time.Date;
                if (day < start || day > end) continue;
                text.Append(commit.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                    .Append(',')
                    .Append(commit.Chars.ToString(CultureInfo.InvariantCulture))
                    .Append(',')
                    .Append(QuoteCsv(commit.Text))
                    .AppendLine();
            }
            return text.ToString();
        }

        /// <summary>CSV 单元格:含逗号/引号/换行时加引号并把内部引号双写。</summary>
        internal static string QuoteCsv(string value)
        {
            if (value == null) return "";
            if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0
                && value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
