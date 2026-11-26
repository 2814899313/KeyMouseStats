using System.Text;

namespace Kms.Tools.ScrubFixtures;

/// <summary>
/// 在 <see cref="FixtureScanner"/> 的原始文本扫描之外，额外解码 v13 的 base64 字段，
/// 因为路径/标题正是藏在 base64 里的。
/// </summary>
internal static class V13PrivacyVerifier
{
    public static IReadOnlyList<string> VerifyFile(string path)
    {
        List<string> problems = [];
        string fileName = System.IO.Path.GetFileName(path);
        int lineNumber = 0;

        foreach (string line in File.ReadLines(path))
        {
            lineNumber++;
            problems.AddRange(VerifyLine(fileName, lineNumber, line));
        }

        return problems;
    }

    private static IEnumerable<string> VerifyLine(string fileName, int lineNumber, string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0 ||
            trimmed[0] == '#' ||
            trimmed[0] == ';' ||
            trimmed.StartsWith("[day=", StringComparison.Ordinal))
        {
            yield break;
        }

        int equals = line.IndexOf('=');
        if (equals <= 0)
        {
            yield break;
        }

        string key = line[..equals].Trim();
        string value = line[(equals + 1)..];

        foreach ((string field, string text) in DecodedFields(key, value))
        {
            if (FixtureScanner.LooksSensitive(text))
            {
                yield return $"{fileName}:{lineNumber} {field} 解码后仍含路径：{Preview(text)}";
            }
        }
    }

    private static IEnumerable<(string Field, string Text)> DecodedFields(string key, string value)
    {
        string[] parts = value.Split('|');

        switch (key)
        {
            case "app" when parts.Length is 5 or 6:
                yield return ("app.path", Decode(parts[0]));
                yield return ("app.title", Decode(parts[1]));
                break;

            case "app_rule" when parts.Length == 2:
                yield return ("app_rule.key", Decode(parts[0]));
                break;

            case "archive_v1" when parts.Length >= 10 && parts[9].Length > 0:
                foreach (string entry in parts[9].Split(','))
                {
                    int colon = entry.LastIndexOf(':');
                    if (colon > 0)
                    {
                        yield return ("archive_v1.app", Decode(entry[..colon]));
                    }
                }

                break;

            case "app_hours_v1" when parts.Length == 2:
                yield return ("app_hours_v1.path", Decode(parts[0]));
                break;

            case "session_app_v1" when parts.Length == 3:
                yield return ("session_app_v1.path", Decode(parts[1]));
                break;

            case "app_keys_v1" when parts.Length == 2:
                yield return ("app_keys_v1.path", Decode(parts[0]));
                break;

            case "hardware_v1" when parts.Length == 8:
                foreach (int index in new[] { 0, 3 })
                {
                    if (parts[index].Length > 0)
                    {
                        yield return ("hardware_v1.name", Decode(parts[index]));
                    }
                }

                break;

            case "keyboard_v1" when parts.Length >= 11:
                foreach (int index in new[] { 1, 2, 3, 4, 5 })
                {
                    if (parts[index].Length > 0)
                    {
                        yield return ("keyboard_v1", Decode(parts[index]));
                    }
                }

                break;

            case "mouse_v1" when parts.Length >= 12:
                foreach (int index in new[] { 1, 2, 3, 4, 5 })
                {
                    if (parts[index].Length > 0)
                    {
                        yield return ("mouse_v1", Decode(parts[index]));
                    }
                }

                break;

            case "wellbeing_v1" when parts.Length >= 10:
                foreach (int index in new[] { 11, 12 })
                {
                    if (index < parts.Length && parts[index].Length > 0)
                    {
                        yield return ("wellbeing_v1", Decode(parts[index]));
                    }
                }

                break;
        }
    }

    private static string Decode(string base64)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return "<非法 base64>";
        }
    }

    private static string Preview(string value) =>
        value.Length <= 80 ? value : value[..80] + "…";
}
