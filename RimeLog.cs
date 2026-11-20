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
        /// <summary>写入方声明的字符数(旧三列格式的第二列);缺失或非法时回算。</summary>
        public int Chars;
        /// <summary>排除换行后的真实码点数,用于和字符构成严格闭环。</summary>
        public int TotalLength;
        /// <summary>纯汉字个数(CJK,不含标点字母)。</summary>
        public int HanziCount;
        /// <summary>normal / raw_ascii / punct / mixed / other。</summary>
        public string CommitType = "other";
        /// <summary>候选框选择下标；-1 表示非候选上屏或未采集。</summary>
        public int CandidateIndex = -1;
        /// <summary>候选下标是否由写入方明确给出(空字符串为未采集)。</summary>
        public bool CandidateKnown;
        /// <summary>Rime 输入的原始码长；-1 表示没有该列。</summary>
        public int CodeLength = -1;
        public string Application = "";
        public string Scheme = "";
        /// <summary>是否为完整七列格式；旧三列/空候选/缺程序或缺码长的行不参与产品统计。</summary>
        public bool FormatComplete;
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

    /// <summary>某个前台程序的文字产出摘要；只保留字符频次，不记录窗口标题。</summary>
    internal sealed class RimeAppSummary
    {
        public string Path = "";
        public long Chars, Commits, Hanzi;
        public readonly Dictionary<int, long> CharCounts = new Dictionary<int, long>();
        /// <summary>该应用自己的 2–4 字词组频次，用于「输出应用」的特征词胶囊。</summary>
        public readonly Dictionary<string, long> PhraseCounts = new Dictionary<string, long>(StringComparer.Ordinal);

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

        /// <summary>
        /// 输出应用卡片上的特征词:先取 2–4 字词组(过滤虚词与互相包含的 n-gram),
        /// 不够 limit 个时用实词单字兜底。返回顺序即胶囊展示顺序。
        /// </summary>
        public List<KeyValuePair<string, long>> FeatureWords(int limit)
        {
            List<KeyValuePair<string, long>> picked = RimePhraseFilter.PickPhrases(PhraseCounts, limit);
            if (picked.Count >= limit) return picked;
            HashSet<string> contained = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < picked.Count; i++)
                for (int k = 0; k < picked[i].Key.Length; k++) contained.Add(picked[i].Key.Substring(k, 1));
            List<KeyValuePair<int, long>> chars = TopChars(32);
            for (int i = 0; i < chars.Count && picked.Count < limit; i++)
            {
                int code = chars[i].Key;
                if (!RimeText.IsCjk(code)) continue;
                string name = RimeText.DisplayName(code);
                if (name.Length != 1 || contained.Contains(name)) continue;
                if (RimePhraseFilter.IsFunctionChar(name[0])) continue;
                contained.Add(name);
                picked.Add(new KeyValuePair<string, long>(name, chars[i].Value));
            }
            return picked;
        }

        /// <summary>悬停画像里的实词单字 Top N(不排除词组中已出现的字)。</summary>
        public List<KeyValuePair<string, long>> ContentChars(int limit)
        {
            List<KeyValuePair<string, long>> list = new List<KeyValuePair<string, long>>();
            List<KeyValuePair<int, long>> chars = TopChars(48);
            for (int i = 0; i < chars.Count && list.Count < limit; i++)
            {
                int code = chars[i].Key;
                if (!RimeText.IsCjk(code)) continue;
                string name = RimeText.DisplayName(code);
                if (name.Length != 1 || RimePhraseFilter.IsFunctionChar(name[0])) continue;
                list.Add(new KeyValuePair<string, long>(name, chars[i].Value));
            }
            return list;
        }
    }

    /// <summary>
    /// 词组级虚词过滤与 n-gram 去重。日报「主题词」与字符页「输出应用」共用同一套规则,
    /// 避免同一个词在一处被当成特征词、在另一处又被当成虚词。
    /// </summary>
    internal static class RimePhraseFilter
    {
        private const string FunctionChars =
            "的了是在我你他她它这那有和与就都也不吗呢吧啊一上个上下中大小到说要会着过很还被把让给对从而为以于之其或及等能可没好又再最更如若但因所但然后现今明昨";

        private static readonly HashSet<string> FunctionPhrases = new HashSet<string>(StringComparer.Ordinal)
        {
            "今天", "明天", "昨天", "现在", "我们", "你们", "他们", "这个", "那个", "什么",
            "怎么", "可以", "因为", "所以", "但是", "如果", "然后", "已经", "还是", "就是",
            "没有", "不是", "一个", "一下", "这样", "那样", "时候", "自己", "大家", "东西",
            "问题", "觉得", "知道", "看到", "起来", "出来", "一些", "这些", "那些", "的话",
            "之后", "之前", "以及", "或者", "而且", "不过", "只是", "真是", "有点", "非常",
            "可能", "应该", "需要", "进行", "通过", "对于", "关于", "由于", "因此", "其实",
            "其中", "目前", "今年", "去年", "明年"
        };

        internal static bool IsFunctionChar(char c)
        {
            return FunctionChars.IndexOf(c) >= 0;
        }

        internal static bool IsFunctionPhrase(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            if (FunctionPhrases.Contains(text)) return true;
            for (int i = 0; i < text.Length; i++)
                if (!IsFunctionChar(text[i])) return false;
            return true;
        }

        internal static bool Overlaps(List<string> picked, string text)
        {
            for (int i = 0; i < picked.Count; i++)
            {
                if (picked[i].IndexOf(text, StringComparison.Ordinal) >= 0) return true;
                if (text.IndexOf(picked[i], StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        internal static List<KeyValuePair<string, long>> PickPhrases(Dictionary<string, long> counts, int limit)
        {
            List<KeyValuePair<string, long>> picked = new List<KeyValuePair<string, long>>();
            if (counts == null || counts.Count == 0 || limit <= 0) return picked;
            List<KeyValuePair<string, long>> candidates = new List<KeyValuePair<string, long>>();
            foreach (KeyValuePair<string, long> pair in counts)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Key.Length < 2 || pair.Key.Length > 4) continue;
                if (pair.Value <= 0) continue;
                if (IsFunctionPhrase(pair.Key)) continue;
                candidates.Add(pair);
            }
            candidates.Sort(delegate(KeyValuePair<string, long> a, KeyValuePair<string, long> b)
            {
                int byCount = b.Value.CompareTo(a.Value);
                if (byCount != 0) return byCount;
                int byLength = b.Key.Length.CompareTo(a.Key.Length);
                return byLength != 0 ? byLength : string.CompareOrdinal(a.Key, b.Key);
            });
            List<string> texts = new List<string>();
            for (int i = 0; i < candidates.Count && picked.Count < limit; i++)
            {
                if (Overlaps(texts, candidates[i].Key)) continue;
                texts.Add(candidates[i].Key);
                picked.Add(candidates[i]);
            }
            return picked;
        }
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
        /// <summary>候选序有效样本、首选、非首选；-1/未知不进入候选率分母。</summary>
        public long CandidateKnown, CandidateFirst, CandidatePaged;
        /// <summary>码效样本：只统计有码长、有汉字且类型为 normal/mixed 的记录。</summary>
        public long CodeSamples, CodeLengthTotal, CodeHanziTotal;
        /// <summary>按前台程序聚合的字数、次数与字符频次。</summary>
        public readonly Dictionary<string, RimeAppSummary> Apps = new Dictionary<string, RimeAppSummary>(StringComparer.OrdinalIgnoreCase);

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

        public double CandidateCoverage
        {
            get { return Commits > 0 ? CandidateKnown / (double)Commits : 0; }
        }

        public double FirstChoiceRate
        {
            get { return CandidateKnown > 0 ? CandidateFirst / (double)CandidateKnown : double.NaN; }
        }

        public double PagedCandidateRate
        {
            get { return CandidateKnown > 0 ? CandidatePaged / (double)CandidateKnown : double.NaN; }
        }

        public double AverageCodeLength
        {
            get { return CodeHanziTotal > 0 ? CodeLengthTotal / (double)CodeHanziTotal : double.NaN; }
        }

        public List<RimeAppSummary> TopApps(int limit)
        {
            List<RimeAppSummary> list = new List<RimeAppSummary>(Apps.Values);
            list.Sort(delegate(RimeAppSummary a, RimeAppSummary b)
            {
                int byChars = b.Chars.CompareTo(a.Chars);
                return byChars != 0 ? byChars : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });
            if (limit > 0 && list.Count > limit) list.RemoveRange(limit, list.Count - limit);
            return list;
        }

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
            if (slice == null) return;
            AddPhrases(text, slice.PhraseCounts);
        }

        /// <summary>同一套切词规则写入任意频次字典(区间总表或单个应用的特征词表)。</summary>
        internal static void AddPhrases(string text, Dictionary<string, long> target)
        {
            if (string.IsNullOrEmpty(text) || target == null) return;
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
                            target.TryGetValue(key, out value);
                            target[key] = value + 1;
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
        /// <summary>格式不完整、被产品读取模式跳过的行数（旧日志/空候选等）。</summary>
        public long IncompleteRows;
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
            return Load(DefaultPath, true);
        }

        public static RimeTextStats Load(string path)
        {
            return Load(path, false);
        }

        /// <summary>completeOnly=true 时只接收完整七列记录；产品页面使用这个模式，测试用三列旧格式仍走 false。</summary>
        public static RimeTextStats Load(string path, bool completeOnly)
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
                        if (commit != null)
                        {
                            if (completeOnly && !commit.FormatComplete) { stats.IncompleteRows++; continue; }
                            stats.Add(commit);
                            continue;
                        }
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
                        if (commit != null)
                        {
                            if (completeOnly && !commit.FormatComplete) stats.IncompleteRows++;
                            else stats.Add(commit);
                        }
                        else stats.BadLines++;
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

            string application = "", scheme = "";
            int codeLength = -1, candidate = -1;
            bool candidateKnown = false;
            int textEnd = fields.Count;
            bool metadata = false;

            // 新日志:时间,字符数,明文,程序,方案,码长,候选序。
            // 明文可能没加引号而带逗号，所以从右侧锚定元数据列，而不是硬取 fields[3]。
            int count = fields.Count;
            if (count >= 7 && IsApplicationField(fields[count - 4]))
            {
                int code;
                application = fields[count - 4].Trim();
                scheme = fields[count - 3].Trim();
                if (int.TryParse(fields[count - 2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
                    codeLength = code >= 0 ? code : -1;
                string candidateField = fields[count - 1].Trim();
                if (candidateField.Length > 0 && int.TryParse(candidateField, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out candidate))
                    candidateKnown = true;
                textEnd = count - 4;
                metadata = true;
            }

            // 六列版本:时间,字符数,明文,程序,方案,码长(没有候选列)。
            if (!metadata && count == 6 && IsApplicationField(fields[3]))
            {
                int code;
                application = fields[3].Trim();
                scheme = fields[4].Trim();
                if (int.TryParse(fields[5].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
                    codeLength = code >= 0 ? code : -1;
                textEnd = 3;
                metadata = true;
            }

            // 导出文件额外带 总字符数/汉字数/输入类型，仍是同一套元数据锚点。
            if (!metadata && count >= 10 && IsCommitType(fields[count - 1].Trim()))
            {
                int code;
                int parsedTotal, parsedHanzi;
                if (int.TryParse(fields[count - 2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedHanzi)
                    && int.TryParse(fields[count - 3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedTotal))
                {
                    application = fields[count - 7].Trim();
                    scheme = fields[count - 6].Trim();
                    if (int.TryParse(fields[count - 5].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
                        codeLength = code >= 0 ? code : -1;
                    string candidateField = fields[count - 4].Trim();
                    if (candidateField.Length > 0 && int.TryParse(candidateField, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out candidate))
                        candidateKnown = true;
                    textEnd = count - 7;
                    metadata = true;
                }
            }

            string text;
            if (metadata)
            {
                StringBuilder joined = new StringBuilder(fields[2]);
                for (int i = 3; i < textEnd; i++) joined.Append(',').Append(fields[i]);
                text = joined.ToString();
            }
            else
            {
                // 旧格式末尾可能带一串空元数据列：时间,字符数,明文,,,,
                // 这些空列不属于明文，不能拼成「明文,,,」。
                bool trailingEmpty = true;
                for (int i = 3; i < fields.Count; i++)
                    if (fields[i].Trim().Length > 0) { trailingEmpty = false; break; }
                if (trailingEmpty) text = fields[2];
                else
                {
                    StringBuilder joined = new StringBuilder(fields[2]);
                    for (int i = 3; i < fields.Count; i++) joined.Append(',').Append(fields[i]);
                    text = joined.ToString();
                }
            }
            if (text == null) text = "";
            // 单次上屏不该有几百个字符;真出现这种行,大概率是文件坏了,截断防御而不是崩掉。
            if (text.Length > 512) text = text.Substring(0, 512);

            int chars;
            if (!int.TryParse(fields[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out chars)
                || chars <= 0 || chars > 100000)
                chars = RimeText.CountCodePoints(text);

            List<int> points = RimeText.CodePoints(text);
            int hanzi = 0, letters = 0, punctuation = 0;
            for (int i = 0; i < points.Count; i++)
            {
                if (RimeText.IsCjk(points[i])) hanzi++;
                else
                {
                    int kind = RimeText.Classify(points[i]);
                    if (kind == 2) letters++;
                    else if (kind == 1) punctuation++;
                }
            }

            RimeCommit commit = new RimeCommit();
            commit.Time = time;
            commit.Text = text;
            commit.Chars = chars;
            commit.TotalLength = points.Count;
            commit.HanziCount = hanzi;
            commit.CommitType = hanzi > 0 ? (letters > 0 ? "mixed" : "normal")
                : letters > 0 ? "raw_ascii" : punctuation > 0 ? "punct" : "other";
            commit.CandidateIndex = candidateKnown ? candidate : -1;
            commit.CandidateKnown = candidateKnown;
            commit.CodeLength = codeLength;
            commit.Application = application;
            commit.Scheme = scheme;
            commit.FormatComplete = metadata && candidateKnown
                && application.Length > 0 && scheme.Length > 0 && codeLength >= 0;
            return commit;
        }

        private static bool IsApplicationField(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string trimmed = value.Trim();
            return trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) >= 0
                || trimmed.IndexOf('\\') >= 0 || trimmed.IndexOf('/') >= 0;
        }

        private static bool IsCommitType(string value)
        {
            return value == "normal" || value == "raw_ascii" || value == "punct"
                || value == "mixed" || value == "other";
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

                if (commit.CandidateKnown && commit.CandidateIndex >= 0)
                {
                    slice.CandidateKnown++;
                    if (commit.CandidateIndex == 0) slice.CandidateFirst++;
                    else slice.CandidatePaged++;
                }
                if (commit.CodeLength > 0 && commit.HanziCount > 0
                    && (commit.CommitType == "normal" || commit.CommitType == "mixed"))
                {
                    slice.CodeSamples++;
                    slice.CodeLengthTotal += commit.CodeLength;
                    slice.CodeHanziTotal += commit.HanziCount;
                }

                string appKey = string.IsNullOrEmpty(commit.Application) ? "未识别应用" : commit.Application;
                RimeAppSummary app;
                if (!slice.Apps.TryGetValue(appKey, out app))
                {
                    app = new RimeAppSummary();
                    app.Path = appKey;
                    slice.Apps[appKey] = app;
                }
                app.Chars += commit.Chars;
                app.Commits++;
                app.Hanzi += commit.HanziCount;
                for (int k = 0; k < points.Count; k++)
                {
                    long appCount;
                    app.CharCounts.TryGetValue(points[k], out appCount);
                    app.CharCounts[points[k]] = appCount + 1;
                }

                RimeSlice.AddPhrases(commit.Text, slice);
                RimeSlice.AddPhrases(commit.Text, app.PhraseCounts);

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

        /// <summary>导出区间内的原始上屏记录,附带总字符/纯汉字/输入类型等衍生列。</summary>
        public static string Export(RimeTextStats stats, DateTime from, DateTime to)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("时间点,字符数,明文,程序,方案,码长,候选序,总字符数,汉字数,输入类型");
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
                    .Append(',').Append(QuoteCsv(commit.Application))
                    .Append(',').Append(QuoteCsv(commit.Scheme))
                    .Append(',').Append(commit.CodeLength >= 0 ? commit.CodeLength.ToString(CultureInfo.InvariantCulture) : "")
                    .Append(',').Append(commit.CandidateKnown ? commit.CandidateIndex.ToString(CultureInfo.InvariantCulture) : "-1")
                    .Append(',').Append(commit.TotalLength.ToString(CultureInfo.InvariantCulture))
                    .Append(',').Append(commit.HanziCount.ToString(CultureInfo.InvariantCulture))
                    .Append(',').Append(commit.CommitType)
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
