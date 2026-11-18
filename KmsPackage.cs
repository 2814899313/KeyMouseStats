// ============================================================================
//  KeyMouseStats v3.0 · Milestone 3 / Step 3  KmsPackage.cs
//
//  跨设备 .kms 增量数据包:混合双层粒度(day_summary + window_session)。
//  纯函数:编码 / 解析 / 校验和 / 幂等合并账本,不碰 SQLite、不碰 UI。
//
//  幂等核心:账本以 (package_id, record_key) 为键,首次命中才纳入;
//  同一包导入两次,第二次所有记录都被账本挡住 → 逐字段恒等。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KeyMouseStats
{
    internal sealed class KmsDayRecord
    {
        public string Day = "";
        public long Keys;
        public long Clicks;
        public long Wheel;
        public long ActiveSeconds;

        public string RecordKey { get { return "D|" + Day; } }

        public string ContentHash()
        {
            return KmsPackage.Hash(Day + "|" + Keys + "|" + Clicks + "|" + Wheel + "|" + ActiveSeconds);
        }
    }

    internal sealed class KmsSessionRecord
    {
        public string Day = "";
        public long StartMs;
        public long EndMs;
        public long KeyCount;
        public int CategoryId;
        public string ProcessPath = "";
        public string ProcessName = "";
        public string WindowTitle = "";

        public string RecordKey
        {
            get { return "S|" + Day + "|" + StartMs + "|" + KmsPackage.Fingerprint(ProcessPath); }
        }

        public string ContentHash()
        {
            return KmsPackage.Hash(Day + "|" + StartMs + "|" + EndMs + "|" + KeyCount + "|"
                + CategoryId + "|" + ProcessPath + "|" + WindowTitle);
        }
    }

    internal sealed class KmsPackageData
    {
        public int Version = 1;
        public string DeviceId = "";
        public string DayRange = "";
        public List<KmsDayRecord> Days = new List<KmsDayRecord>();
        public List<KmsSessionRecord> Sessions = new List<KmsSessionRecord>();
    }

    internal sealed class KmsMergeResult
    {
        public int DaysApplied;
        public int SessionsApplied;
        public int DaysSkipped;
        public int SessionsSkipped;
        public bool Changed { get { return DaysApplied + SessionsApplied > 0; } }
    }

    internal static class KmsPackage
    {
        internal const string Magic = "KMS1";
        internal const string Separator = "---payload---";
        internal const string VersionPrefix = "version=";
        internal const string ChecksumPrefix = "checksum=";
        internal const string DevicePrefix = "device_id=";
        internal const string RangePrefix = "day_range=";

        /// <summary>SHA256 → 小写十六进制,供校验和与内容指纹复用。</summary>
        internal static string Hash(string text)
        {
            try
            {
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text == null ? "" : text));
                    StringBuilder builder = new StringBuilder(bytes.Length * 2);
                    for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
                    return builder.ToString();
                }
            }
            catch { return ""; }
        }

        /// <summary>短指纹,只用于拼 record_key,不参与完整性校验。</summary>
        internal static string Fingerprint(string text)
        {
            string full = Hash(text);
            return full.Length > 16 ? full.Substring(0, 16) : full;
        }

        /// <summary>制表符/换行必须转义,否则标题会把行结构切碎。</summary>
        internal static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        internal static string Unescape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            StringBuilder builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '\\' || i + 1 >= text.Length) { builder.Append(c); continue; }
                char next = text[++i];
                if (next == 't') builder.Append('\t');
                else if (next == 'r') builder.Append('\r');
                else if (next == 'n') builder.Append('\n');
                else if (next == '\\') builder.Append('\\');
                else { builder.Append('\\'); builder.Append(next); }
            }
            return builder.ToString();
        }

        internal static string BuildPayload(KmsPackageData data)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < data.Days.Count; i++)
            {
                KmsDayRecord day = data.Days[i];
                builder.Append("D\t").Append(Escape(day.Day)).Append('\t')
                    .Append(day.Keys.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(day.Clicks.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(day.Wheel.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(day.ActiveSeconds.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            for (int i = 0; i < data.Sessions.Count; i++)
            {
                KmsSessionRecord session = data.Sessions[i];
                builder.Append("S\t").Append(Escape(session.Day)).Append('\t')
                    .Append(session.StartMs.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(session.EndMs.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(session.KeyCount.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(session.CategoryId.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(Escape(session.ProcessPath)).Append('\t')
                    .Append(Escape(session.ProcessName)).Append('\t')
                    .Append(Escape(session.WindowTitle)).Append('\n');
            }
            return builder.ToString();
        }

        /// <summary>打包成完整 .kms 文本(带魔数、版本、校验和与分隔符)。</summary>
        internal static string Export(KmsPackageData data)
        {
            if (data == null) throw new ArgumentNullException("data");
            string payload = BuildPayload(data);
            StringBuilder builder = new StringBuilder();
            builder.Append(Magic).Append('\n');
            builder.Append(VersionPrefix).Append(data.Version.ToString(CultureInfo.InvariantCulture)).Append('\n');
            builder.Append(DevicePrefix).Append(Escape(data.DeviceId)).Append('\n');
            builder.Append(RangePrefix).Append(Escape(data.DayRange)).Append('\n');
            builder.Append(ChecksumPrefix).Append(Hash(payload)).Append('\n');
            builder.Append(Separator).Append('\n');
            builder.Append(payload);
            return builder.ToString();
        }

        /// <summary>解析并严格校验魔数与校验和;损坏或篡改直接抛。</summary>
        internal static KmsPackageData Import(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new InvalidOperationException("数据包为空");
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length < 2 || lines[0].Trim() != Magic)
                throw new InvalidOperationException("不是 .kms 数据包(魔数不匹配)");

            KmsPackageData data = new KmsPackageData();
            string expected = null;
            int separator = -1;
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Trim() == Separator) { separator = i; break; }
                if (line.StartsWith(VersionPrefix, StringComparison.Ordinal))
                {
                    int version;
                    if (int.TryParse(line.Substring(VersionPrefix.Length), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out version)) data.Version = version;
                }
                else if (line.StartsWith(DevicePrefix, StringComparison.Ordinal))
                    data.DeviceId = Unescape(line.Substring(DevicePrefix.Length));
                else if (line.StartsWith(RangePrefix, StringComparison.Ordinal))
                    data.DayRange = Unescape(line.Substring(RangePrefix.Length));
                else if (line.StartsWith(ChecksumPrefix, StringComparison.Ordinal))
                    expected = line.Substring(ChecksumPrefix.Length).Trim();
            }
            if (separator < 0) throw new InvalidOperationException("数据包缺少 ---payload--- 分隔符");
            if (string.IsNullOrEmpty(expected)) throw new InvalidOperationException("数据包缺少校验和");
            if (data.Version != 1) throw new InvalidOperationException("不支持的数据包版本: " + data.Version);

            StringBuilder payload = new StringBuilder();
            for (int i = separator + 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0 && i == lines.Length - 1) continue;
                payload.Append(lines[i]).Append('\n');
            }
            string body = payload.ToString();
            // 末尾换行是行结构的自然产物,校验时以原样为准(导出侧同样带尾换行)。
            string actual = Hash(body);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("校验和不匹配:数据包已损坏或被篡改");

            for (int i = separator + 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) continue;
                string[] parts = line.Split('\t');
                if (parts[0] == "D" && parts.Length >= 6)
                {
                    KmsDayRecord day = new KmsDayRecord();
                    day.Day = Unescape(parts[1]);
                    day.Keys = ParseLong(parts[2]);
                    day.Clicks = ParseLong(parts[3]);
                    day.Wheel = ParseLong(parts[4]);
                    day.ActiveSeconds = ParseLong(parts[5]);
                    data.Days.Add(day);
                }
                else if (parts[0] == "S" && parts.Length >= 9)
                {
                    KmsSessionRecord session = new KmsSessionRecord();
                    session.Day = Unescape(parts[1]);
                    session.StartMs = ParseLong(parts[2]);
                    session.EndMs = ParseLong(parts[3]);
                    session.KeyCount = ParseLong(parts[4]);
                    session.CategoryId = (int)ParseLong(parts[5]);
                    session.ProcessPath = Unescape(parts[6]);
                    session.ProcessName = Unescape(parts[7]);
                    session.WindowTitle = Unescape(parts[8]);
                    data.Sessions.Add(session);
                }
            }
            return data;
        }

        private static long ParseLong(string text)
        {
            long value;
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return value;
            return 0;
        }

        /// <summary>
        /// 幂等合并:账本命中 (record_key 已存在) 即跳过,只有首次出现的记录才被纳入。
        /// 返回本次真正应用了多少条 —— 重复导入一个包时该值必须为 0。
        /// </summary>
        internal static KmsMergeResult Merge(KmsPackageData data, IDictionary<string, string> ledger,
            IList<KmsDayRecord> daySink, IList<KmsSessionRecord> sessionSink)
        {
            KmsMergeResult result = new KmsMergeResult();
            if (data == null) return result;
            if (ledger == null) throw new ArgumentNullException("ledger");

            for (int i = 0; i < data.Days.Count; i++)
            {
                KmsDayRecord day = data.Days[i];
                if (ledger.ContainsKey(day.RecordKey)) { result.DaysSkipped++; continue; }
                ledger[day.RecordKey] = day.ContentHash();
                if (daySink != null) daySink.Add(day);
                result.DaysApplied++;
            }
            for (int i = 0; i < data.Sessions.Count; i++)
            {
                KmsSessionRecord session = data.Sessions[i];
                if (ledger.ContainsKey(session.RecordKey)) { result.SessionsSkipped++; continue; }
                ledger[session.RecordKey] = session.ContentHash();
                if (sessionSink != null) sessionSink.Add(session);
                result.SessionsApplied++;
            }
            return result;
        }
    }
}
