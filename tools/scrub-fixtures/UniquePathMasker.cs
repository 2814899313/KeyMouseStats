using Kms.Core.Privacy;

namespace Kms.Tools.ScrubFixtures;

/// <summary>
/// 路径脱敏 + 全局唯一化。
///
/// <see cref="PrivacyMask.MaskPath"/> 只留文件名，不同目录的同名 exe 会碰撞；
/// 在 v3 里这会导致 Apps 字典合并、小时聚合互相覆盖。因此统一加 <c>~n</c> 后缀，
/// 保证「同一个原路径 → 同一个脱敏名；不同原路径 → 不同脱敏名」。
/// </summary>
internal sealed class UniquePathMasker
{
    private const string AggregateSentinel = "[其他应用汇总]";

    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    public string Mask(string path)
    {
        if (path.Length == 0)
        {
            return "";
        }

        if (string.Equals(path, AggregateSentinel, StringComparison.Ordinal))
        {
            return path;
        }

        if (_map.TryGetValue(path, out string? existing))
        {
            return existing;
        }

        string baseName = PrivacyMask.MaskPath(path).ToLowerInvariant();
        if (baseName.Length == 0)
        {
            baseName = "已隐藏";
        }

        string candidate = baseName;
        int suffix = 1;
        while (_used.Contains(candidate))
        {
            suffix++;
            candidate = AddSuffixBeforeExtension(baseName, suffix);
        }

        if (FixtureScanner.LooksSensitive(candidate))
        {
            throw new InvalidOperationException($"脱敏后的路径仍含目录：{candidate}");
        }

        _map[path] = candidate;
        _used.Add(candidate);
        return candidate;
    }

    public static string AddSuffixBeforeExtension(string name, int suffix)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0
            ? name[..dot] + "~" + suffix + name[dot..]
            : name + "~" + suffix;
    }
}
