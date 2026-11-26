using System.Text;
using Kms.Core.Privacy;

namespace Kms.Tools.ScrubFixtures;

/// <summary>
/// v13 文本档逐行清洗器。
///
/// 硬规则（来自 v3 <c>KeyMouseWidget.Store</c> 的读写实现）：
/// 1. 绝不 parse→serialize，只替换隐私字段的值；
/// 2. 行数、行序、key、字段数、数字、Ticks、O 时间串全部原样保留；
/// 3. base64 解码失败立即中止，不写半成品；
/// 4. 路径 / 标题做全局唯一化，避免脱敏后同名碰撞导致 Apps 合并丢数。
/// </summary>
internal sealed class TextFixtureScrubber
{
    private const int TitleCategory = 3; // 「其他」

    private readonly UniquePathMasker _paths = new();
    private readonly Dictionary<string, string> _titleMap = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedTitles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _ruleMap = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedRules = new(StringComparer.Ordinal);
    private int _keyboardIndex;
    private int _mouseIndex;

    public static void Scrub(string source, string destination)
    {
        new TextFixtureScrubber().Run(source, destination);
    }

    private void Run(string source, string destination)
    {
        byte[] bytes = File.ReadAllBytes(source);
        bool hasBom = bytes is [0xEF, 0xBB, 0xBF, ..];
        ReadOnlySpan<byte> payload = hasBom ? bytes.AsSpan(3) : bytes;

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(payload);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidOperationException($"{Path.GetFileName(source)} 不是合法 UTF-8：{exception.Message}");
        }

        // 按 '\n' 拆行并保留每行末尾的 '\r'，从而原样保留 CRLF / LF 混用与尾随换行。
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            bool hasCr = lines[i].EndsWith('\r');
            string core = hasCr ? lines[i][..^1] : lines[i];
            string transformed = TransformLine(core, i + 1);
            lines[i] = hasCr ? transformed + "\r" : transformed;
        }

        string output = string.Join('\n', lines);
        byte[] outputBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(output);
        if (hasBom)
        {
            outputBytes = [0xEF, 0xBB, 0xBF, .. outputBytes];
        }

        File.WriteAllBytes(destination, outputBytes);
    }

    private string TransformLine(string line, int lineNumber)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0 ||
            trimmed[0] == '#' ||
            trimmed[0] == ';' ||
            trimmed.StartsWith("[day=", StringComparison.Ordinal))
        {
            return line;
        }

        int equals = line.IndexOf('=');
        if (equals <= 0)
        {
            return line;
        }

        string key = line[..equals].Trim();
        string value = line[(equals + 1)..];

        string? transformed = key switch
        {
            "app" => TransformApp(value, lineNumber),
            "app_rule" => TransformAppRule(value, lineNumber),
            "archive_v1" => TransformArchive(value, lineNumber),
            "app_hours_v1" => TransformPathField(value, lineNumber, expectedParts: 2, pathIndex: 0, key),
            "session_app_v1" => TransformPathField(value, lineNumber, expectedParts: 3, pathIndex: 1, key),
            "app_keys_v1" => TransformPathField(value, lineNumber, expectedParts: 2, pathIndex: 0, key),
            "hardware_v1" => TransformHardware(value, lineNumber),
            "keyboard_v1" => TransformKeyboard(value, lineNumber),
            "mouse_v1" => TransformMouse(value, lineNumber),
            "wellbeing_v1" => TransformWellbeing(value, lineNumber),
            "click_screen_v1" => TransformClickScreen(value, lineNumber),
            _ => null
        };

        return transformed is null ? line : $"{key}={transformed}";
    }

    private string TransformApp(string value, int lineNumber)
    {
        string[] parts = value.Split('|');
        if (parts.Length is not (5 or 6))
        {
            throw Error(lineNumber, $"app 应为 5/6 段，实际 {parts.Length} 段");
        }

        string path = Decode(parts[0], lineNumber, "app.path");
        string title = Decode(parts[1], lineNumber, "app.title");

        parts[0] = Encode(_paths.Mask(path));
        parts[1] = Encode(MaskTitleUnique(path, title));
        return string.Join('|', parts);
    }

    private string TransformAppRule(string value, int lineNumber)
    {
        string[] parts = value.Split('|');
        if (parts.Length != 2)
        {
            throw Error(lineNumber, $"app_rule 应为 2 段，实际 {parts.Length} 段");
        }

        string ruleKey = Decode(parts[0], lineNumber, "app_rule.key");
        parts[0] = Encode(MaskRuleKeyUnique(ruleKey));
        return string.Join('|', parts);
    }

    private string TransformArchive(string value, int lineNumber)
    {
        string[] parts = value.Split('|');
        if (parts.Length < 9)
        {
            throw Error(lineNumber, $"archive_v1 至少 9 段，实际 {parts.Length} 段");
        }

        if (parts.Length >= 10 && parts[9].Length > 0)
        {
            string[] entries = parts[9].Split(',');
            for (int i = 0; i < entries.Length; i++)
            {
                int colon = entries[i].LastIndexOf(':');
                if (colon <= 0)
                {
                    throw Error(lineNumber, $"archive_v1 appList 条目格式错误：{entries[i]}");
                }

                string path = Decode(entries[i][..colon], lineNumber, "archive_v1.app");
                entries[i] = Encode(_paths.Mask(path)) + entries[i][colon..];
            }

            parts[9] = string.Join(',', entries);
        }

        return string.Join('|', parts);
    }

    private string TransformPathField(string value, int lineNumber, int expectedParts, int pathIndex, string key)
    {
        string[] parts = value.Split('|');
        if (parts.Length != expectedParts)
        {
            throw Error(lineNumber, $"{key} 应为 {expectedParts} 段，实际 {parts.Length} 段");
        }

        string path = Decode(parts[pathIndex], lineNumber, $"{key}.path");
        parts[pathIndex] = Encode(_paths.Mask(path));
        return string.Join('|', parts);
    }

    private string TransformHardware(string value, int lineNumber)
    {
        if (value.Length == 0)
        {
            return value;
        }

        string[] parts = value.Split('|');
        if (parts.Length != 8)
        {
            throw Error(lineNumber, $"hardware_v1 应为 8 段，实际 {parts.Length} 段");
        }

        if (parts[0].Length > 0)
        {
            parts[0] = Encode("键盘");
        }

        if (parts[3].Length > 0)
        {
            parts[3] = Encode("鼠标");
        }

        return string.Join('|', parts);
    }

    private string TransformKeyboard(string value, int lineNumber)
    {
        string[] parts = value.Split('|');
        if (parts.Length < 11)
        {
            throw Error(lineNumber, $"keyboard_v1 至少 11 段，实际 {parts.Length} 段");
        }

        int index = ++_keyboardIndex;
        if (parts[1].Length > 0)
        {
            parts[1] = Encode(DeviceLabel("键盘", index));
        }

        if (parts[2].Length > 0)
        {
            parts[2] = Encode(DeviceLabel("键盘", index));
        }

        if (parts[3].Length > 0)
        {
            parts[3] = Encode($"keyboard-device-{index}");
        }

        if (parts[4].Length > 0)
        {
            parts[4] = Encode($"keyboard-hardware-{index}");
        }

        if (parts[5].Length > 0)
        {
            parts[5] = Encode($"keyboard-instance-{index}");
        }

        return string.Join('|', parts);
    }

    private string TransformMouse(string value, int lineNumber)
    {
        string[] parts = value.Split('|');
        if (parts.Length < 12)
        {
            throw Error(lineNumber, $"mouse_v1 至少 12 段，实际 {parts.Length} 段");
        }

        int index = ++_mouseIndex;
        if (parts[1].Length > 0)
        {
            parts[1] = Encode(DeviceLabel("鼠标", index));
        }

        if (parts[2].Length > 0)
        {
            parts[2] = Encode(DeviceLabel("鼠标", index));
        }

        if (parts[3].Length > 0)
        {
            parts[3] = Encode($"mouse-device-{index}");
        }

        if (parts[4].Length > 0)
        {
            parts[4] = Encode($"mouse-hardware-{index}");
        }

        if (parts[5].Length > 0)
        {
            parts[5] = Encode($"mouse-instance-{index}");
        }

        return string.Join('|', parts);
    }

    private string TransformWellbeing(string value, int lineNumber)
    {
        string[] parts = value.Split('|');
        if (parts.Length < 10)
        {
            throw Error(lineNumber, $"wellbeing_v1 至少 10 段，实际 {parts.Length} 段");
        }

        // 只清已存在的段，不补段、不删段：旧档 10 段保持 10 段。
        if (parts.Length >= 12)
        {
            parts[11] = "";
        }

        if (parts.Length >= 13)
        {
            parts[12] = "";
        }

        return string.Join('|', parts);
    }

    private string TransformClickScreen(string value, int lineNumber)
    {
        if (value.Length == 0)
        {
            return value;
        }

        string[] parts = value.Split('|');
        if (parts.Length != 5)
        {
            throw Error(lineNumber, $"click_screen_v1 应为 5 段，实际 {parts.Length} 段");
        }

        parts[0] = "display";
        return string.Join('|', parts);
    }

    private string MaskTitleUnique(string path, string title)
    {
        string originalId = path.ToLowerInvariant() + "\n" + title;
        if (_titleMap.TryGetValue(originalId, out string? existing))
        {
            return existing;
        }

        string maskedTitle;
        if (PrivacySentinels.IsTitle(title))
        {
            maskedTitle = title;
        }
        else
        {
            maskedTitle = PrivacyMask.MaskTitle(title, TitleCategory);
        }

        string maskedId = _paths.Mask(path).ToLowerInvariant() + "\n" + maskedTitle;
        string candidate = maskedTitle;
        int suffix = 1;
        while (_usedTitles.Contains(maskedId))
        {
            suffix++;
            candidate = maskedTitle + "~" + suffix;
            maskedId = _paths.Mask(path).ToLowerInvariant() + "\n" + candidate;
        }

        EnsureSafe(candidate, "title");
        _titleMap[originalId] = candidate;
        _usedTitles.Add(maskedId);
        return candidate;
    }

    private string MaskRuleKeyUnique(string original)
    {
        if (_ruleMap.TryGetValue(original, out string? existing))
        {
            return existing;
        }

        string candidate = MaskRuleKey(original);
        int suffix = 1;
        while (_usedRules.Contains(candidate))
        {
            suffix++;
            candidate = AddRuleSuffix(MaskRuleKey(original), suffix);
        }

        EnsureSafe(candidate, "rule");
        _ruleMap[original] = candidate;
        _usedRules.Add(candidate);
        return candidate;
    }

    private string MaskRuleKey(string key)
    {
        if (key.StartsWith("app:", StringComparison.Ordinal))
        {
            return "app:" + _paths.Mask(key[4..]);
        }

        if (key.StartsWith("window:", StringComparison.Ordinal))
        {
            string rest = key[7..];
            int newline = rest.IndexOf('\n');
            if (newline < 0)
            {
                return "window:" + _paths.Mask(rest);
            }

            string path = rest[..newline];
            string title = rest[(newline + 1)..];
            return "window:" + _paths.Mask(path) + "\n" + MaskTitleUnique(path, title);
        }

        if (key.StartsWith("title:", StringComparison.Ordinal))
        {
            string rest = key[6..];
            int newline = rest.IndexOf('\n');
            if (newline < 0)
            {
                return "title:" + _paths.Mask(rest);
            }

            string path = rest[..newline];
            string keyword = rest[(newline + 1)..];
            return "title:" + _paths.Mask(path) + "\n" + PrivacyMask.MaskText(keyword);
        }

        return PrivacyMask.MaskText(key);
    }

    private static string AddRuleSuffix(string value, int suffix)
    {
        int newline = value.IndexOf('\n');
        return newline < 0
            ? value + "~" + suffix
            : value[..newline] + "\n" + value[(newline + 1)..] + "~" + suffix;
    }

    private static string DeviceLabel(string kind, int index) =>
        index == 1 ? kind : $"{kind} {index}";

    private static string Decode(string base64, int lineNumber, string field)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            throw Error(lineNumber, $"{field} 不是合法 base64");
        }
    }

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static InvalidOperationException Error(int lineNumber, string message) =>
        new($"{lineNumber} 行：{message}");

    private static void EnsureSafe(string value, string field)
    {
        if (FixtureScanner.LooksSensitive(value))
        {
            throw new InvalidOperationException($"清洗后 {field} 仍含路径：{value}");
        }
    }
}
