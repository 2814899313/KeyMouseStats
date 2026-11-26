using System.Security.Cryptography;
using System.Text;

namespace Kms.Tools.ScrubFixtures;

/// <summary>
/// <c>CHECKSUMS.txt</c>：清洗后的固件清单。任何字节漂移都必须让 CI 失败，
/// 防止有人把未清洗的真实数据补进来。
/// </summary>
internal static class FixtureManifest
{
    private const string ManifestName = "CHECKSUMS.txt";
    private const string ExpectedDirectory = "expected";
    private static readonly string[] Excluded = [ManifestName, "README.md"];

    public static void Write(string directory)
    {
        string[] files = EnumerateFiles(directory);
        List<string> lines =
        [
            "# 由 tools/scrub-fixtures 生成，请勿手工编辑。",
            "# 格式：<sha256>  <相对路径>",
            .. files.Select(file => $"{Hash(file)}  {Relative(directory, file)}")
        ];

        File.WriteAllLines(
            Path.Combine(directory, ManifestName),
            lines,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>返回问题列表；空列表表示通过。</summary>
    public static IReadOnlyList<string> Verify(string directory)
    {
        string manifestPath = Path.Combine(directory, ManifestName);
        if (!File.Exists(manifestPath))
        {
            return [$"缺少 {ManifestName}，请先运行 scrub 生成。"];
        }

        Dictionary<string, string> expected = [];
        foreach (string line in File.ReadLines(manifestPath))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            int separator = line.IndexOf("  ", StringComparison.Ordinal);
            if (separator != 64)
            {
                return [$"清单行格式错误：{line}"];
            }

            expected[line[(separator + 2)..]] = line[..separator];
        }

        List<string> problems = [];
        string[] actualFiles = EnumerateFiles(directory);

        foreach (string file in actualFiles)
        {
            string relative = Relative(directory, file);
            if (!expected.Remove(relative, out string? hash))
            {
                problems.Add($"清单里没有：{relative}");
                continue;
            }

            string actual = Hash(file);
            if (!string.Equals(hash, actual, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"sha256 不一致：{relative}");
            }
        }

        foreach (string missing in expected.Keys)
        {
            problems.Add($"文件缺失：{missing}");
        }

        return problems;
    }

    private static string[] EnumerateFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(file => !Excluded.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => !Path.GetRelativePath(directory, file)
                .StartsWith(ExpectedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string Relative(string directory, string file) =>
        Path.GetRelativePath(directory, file).Replace('\\', '/');

    private static string Hash(string file)
    {
        using FileStream stream = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
