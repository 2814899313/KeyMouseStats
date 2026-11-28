namespace Kms.Core.Apps;

/// <summary>
/// 筛选面板的展示名归一化。把同一软件的多版本/多目录记录折叠成一个分组键，
/// 但不改动真实路径与 AppScopeSet 查询键。
/// </summary>
public static class AppDisplayName
{
    /// <summary>去扩展名、去尾部版本号、忽略大小写后的稳定分组键。</summary>
    public static string GroupKey(string? displayName) =>
        FriendlyName(displayName).ToLowerInvariant();

    /// <summary>去扩展名与常见尾部版本号，保留原始大小写用于展示。</summary>
    public static string FriendlyName(string? displayName)
    {
        string text = (displayName ?? "").Trim();
        if (text.Length == 0)
        {
            return "";
        }

        text = Path.GetFileName(text);
        if (text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^4];
        }

        text = text.Trim();
        while (text.Length > 2)
        {
            int cut = FindVersionCut(text);
            if (cut < 2)
            {
                break;
            }

            text = text[..cut].TrimEnd('-', '_', '~', ' ', '.');
        }

        return text;
    }

    /// <summary>旧数据里可能只保存 exe 名；用于分组键的路径回退。</summary>
    public static string GroupKeyForPath(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return "";
        }

        return GroupKey(Path.GetFileName(processPath.Trim()));
    }

    private static int FindVersionCut(string text)
    {
        for (int i = text.Length - 1; i >= 2; i--)
        {
            char separator = text[i];
            if (separator is not ('-' or '_' or '~' or ' ' or '.'))
            {
                continue;
            }

            if (IsVersionSuffix(text[(i + 1)..]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsVersionSuffix(string suffix)
    {
        if (suffix.Length == 0)
        {
            return false;
        }

        string lower = suffix.ToLowerInvariant();
        if (lower is "clean" or "portable" or "beta" or "alpha" or "rc"
            or "preview" or "nightly" or "setup" or "installer"
            or "x64" or "x86" or "win64" or "win32" or "windows"
            or "amd64" or "arm64")
        {
            return true;
        }

        bool hasDigit = false;
        foreach (char c in suffix)
        {
            if (char.IsDigit(c))
            {
                hasDigit = true;
                continue;
            }

            if (c is '.' or '-' or '_' or 'r' or 'R' or 'v' or 'V')
            {
                continue;
            }

            return false;
        }

        return hasDigit;
    }
}
