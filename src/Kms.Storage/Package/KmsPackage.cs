using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Kms.Storage.Package;

public sealed class KmsDayRecord
{
    public string Day { get; set; } = "";
    public long Keys { get; set; }
    public long Clicks { get; set; }
    public long Wheel { get; set; }
    public long ActiveSeconds { get; set; }

    public string RecordKey => "D|" + Day;

    public string ContentHash() =>
        KmsPackage.Hash($"{Day}|{Keys}|{Clicks}|{Wheel}|{ActiveSeconds}");
}

public sealed class KmsSessionRecord
{
    public string Day { get; set; } = "";
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public long KeyCount { get; set; }
    public int CategoryId { get; set; }
    public string ProcessPath { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string WindowTitle { get; set; } = "";

    public string RecordKey => $"S|{Day}|{StartMs}|{KmsPackage.Fingerprint(ProcessPath)}";

    public string ContentHash() =>
        KmsPackage.Hash(
            $"{Day}|{StartMs}|{EndMs}|{KeyCount}|{CategoryId}|{ProcessPath}|{WindowTitle}");
}

public sealed class KmsPackageData
{
    public int Version { get; set; } = 1;
    public string DeviceId { get; set; } = "";
    public string DayRange { get; set; } = "";
    public List<KmsDayRecord> Days { get; } = new();
    public List<KmsSessionRecord> Sessions { get; } = new();
}

public sealed class KmsMergeResult
{
    public int DaysApplied { get; set; }
    public int SessionsApplied { get; set; }
    public int DaysSkipped { get; set; }
    public int SessionsSkipped { get; set; }
    public bool Changed => DaysApplied + SessionsApplied > 0;
}

/// <summary>跨设备 .kms 增量包：编码 / 解析 / 校验和 / 幂等账本。</summary>
public static class KmsPackage
{
    public const string Magic = "KMS1";
    public const string Separator = "---payload---";
    private const string VersionPrefix = "version=";
    private const string ChecksumPrefix = "checksum=";
    private const string DevicePrefix = "device_id=";
    private const string RangePrefix = "day_range=";

    public static string Hash(string? text)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""));
        return Convert.ToHexStringLower(bytes);
    }

    public static string Fingerprint(string? text)
    {
        string full = Hash(text);
        return full.Length > 16 ? full[..16] : full;
    }

    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    public static string Unescape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        StringBuilder builder = new(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\\' || i + 1 >= text.Length)
            {
                builder.Append(c);
                continue;
            }

            char next = text[++i];
            if (next == 't')
            {
                builder.Append('\t');
            }
            else if (next == 'r')
            {
                builder.Append('\r');
            }
            else if (next == 'n')
            {
                builder.Append('\n');
            }
            else if (next == '\\')
            {
                builder.Append('\\');
            }
            else
            {
                builder.Append('\\');
                builder.Append(next);
            }
        }

        return builder.ToString();
    }

    public static string BuildPayload(KmsPackageData data)
    {
        StringBuilder builder = new();

        foreach (KmsDayRecord day in data.Days)
        {
            builder.Append("D\t").Append(Escape(day.Day)).Append('\t')
                .Append(day.Keys.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(day.Clicks.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(day.Wheel.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(day.ActiveSeconds.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        foreach (KmsSessionRecord session in data.Sessions)
        {
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

    public static string Export(KmsPackageData data)
    {
        string payload = BuildPayload(data);
        StringBuilder builder = new();
        builder.Append(Magic).Append('\n');
        builder.Append(VersionPrefix).Append(data.Version.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append(DevicePrefix).Append(Escape(data.DeviceId)).Append('\n');
        builder.Append(RangePrefix).Append(Escape(data.DayRange)).Append('\n');
        builder.Append(ChecksumPrefix).Append(Hash(payload)).Append('\n');
        builder.Append(Separator).Append('\n');
        builder.Append(payload);
        return builder.ToString();
    }

    public static KmsPackageData Import(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidOperationException("数据包为空");
        }

        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length < 2 || lines[0].Trim() != Magic)
        {
            throw new InvalidOperationException("不是 .kms 数据包(魔数不匹配)");
        }

        KmsPackageData data = new();
        string? expected = null;
        int separator = -1;

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Trim() == Separator)
            {
                separator = i;
                break;
            }

            if (line.StartsWith(VersionPrefix, StringComparison.Ordinal))
            {
                if (int.TryParse(
                        line[VersionPrefix.Length..],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int version))
                {
                    data.Version = version;
                }
            }
            else if (line.StartsWith(DevicePrefix, StringComparison.Ordinal))
            {
                data.DeviceId = Unescape(line[DevicePrefix.Length..]);
            }
            else if (line.StartsWith(RangePrefix, StringComparison.Ordinal))
            {
                data.DayRange = Unescape(line[RangePrefix.Length..]);
            }
            else if (line.StartsWith(ChecksumPrefix, StringComparison.Ordinal))
            {
                expected = line[ChecksumPrefix.Length..].Trim();
            }
        }

        if (separator < 0)
        {
            throw new InvalidOperationException("数据包缺少 ---payload--- 分隔符");
        }

        if (string.IsNullOrEmpty(expected))
        {
            throw new InvalidOperationException("数据包缺少校验和");
        }

        if (data.Version != 1)
        {
            throw new InvalidOperationException("不支持的数据包版本: " + data.Version);
        }

        StringBuilder payload = new();
        for (int i = separator + 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0 && i == lines.Length - 1)
            {
                continue;
            }

            payload.Append(lines[i]).Append('\n');
        }

        string body = payload.ToString();
        if (!string.Equals(Hash(body), expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("校验和不匹配:数据包已损坏或被篡改");
        }

        for (int i = separator + 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = line.Split('\t');
            if (parts[0] == "D" && parts.Length >= 6)
            {
                data.Days.Add(new KmsDayRecord
                {
                    Day = Unescape(parts[1]),
                    Keys = ParseLong(parts[2]),
                    Clicks = ParseLong(parts[3]),
                    Wheel = ParseLong(parts[4]),
                    ActiveSeconds = ParseLong(parts[5])
                });
            }
            else if (parts[0] == "S" && parts.Length >= 9)
            {
                data.Sessions.Add(new KmsSessionRecord
                {
                    Day = Unescape(parts[1]),
                    StartMs = ParseLong(parts[2]),
                    EndMs = ParseLong(parts[3]),
                    KeyCount = ParseLong(parts[4]),
                    CategoryId = (int)ParseLong(parts[5]),
                    ProcessPath = Unescape(parts[6]),
                    ProcessName = Unescape(parts[7]),
                    WindowTitle = Unescape(parts[8])
                });
            }
        }

        return data;
    }

    public static KmsMergeResult Merge(
        KmsPackageData data,
        IDictionary<string, string> ledger,
        IList<KmsDayRecord>? daySink,
        IList<KmsSessionRecord>? sessionSink)
    {
        KmsMergeResult result = new();

        foreach (KmsDayRecord day in data.Days)
        {
            if (ledger.ContainsKey(day.RecordKey))
            {
                result.DaysSkipped++;
                continue;
            }

            ledger[day.RecordKey] = day.ContentHash();
            daySink?.Add(day);
            result.DaysApplied++;
        }

        foreach (KmsSessionRecord session in data.Sessions)
        {
            if (ledger.ContainsKey(session.RecordKey))
            {
                result.SessionsSkipped++;
                continue;
            }

            ledger[session.RecordKey] = session.ContentHash();
            sessionSink?.Add(session);
            result.SessionsApplied++;
        }

        return result;
    }

    private static long ParseLong(string text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : 0;
}
