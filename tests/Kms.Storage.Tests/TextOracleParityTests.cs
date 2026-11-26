using System.Globalization;
using System.Text;
using Kms.Oracle;
using Kms.Storage.Text;

namespace Kms.Storage.Tests;

public sealed class TextOracleParityTests
{
    private const double Tolerance = 1e-9;

    [Theory]
    [InlineData("v13-text-current.txt")]
    [InlineData("v13-month-2026-09.txt")]
    [InlineData("v13-month-2026-10.txt")]
    [InlineData("v13-synthetic.txt")]
    public void V4Dump_MatchesV3Oracle(string fixture)
    {
        string root = FindRepoRoot();
        string textPath = Path.Combine(root, "tests", "Fixtures", fixture);
        string expectedPath = Path.Combine(
            root,
            "tests",
            "Fixtures",
            "expected",
            Path.GetFileNameWithoutExtension(fixture) + ".dump");

        Assert.True(File.Exists(textPath), $"缺少固件：{textPath}");
        Assert.True(File.Exists(expectedPath), $"缺少 v3 期望值：{expectedPath}");

        string expected = Normalize(File.ReadAllText(expectedPath, new UTF8Encoding(false)));
        string actual = Normalize(Program.Dump(TextStatsCodec.Load(File.ReadAllText(textPath, Encoding.UTF8))));

        string? difference = FirstDifference(expected, actual);
        if (difference is not null)
        {
            throw new Xunit.Sdk.XunitException(difference);
        }
    }

    /// <summary>
    /// GitHub Actions 的 Windows runner 默认 core.autocrlf=true，期望 dump 会检出为 CRLF；
    /// 这里统一成 LF，避免行尾差异被误判成数据差异。
    /// </summary>
    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string? FirstDifference(string expected, string actual)
    {
        string[] expectedLines = expected.Split('\n');
        string[] actualLines = actual.Split('\n');
        int count = Math.Min(expectedLines.Length, actualLines.Length);

        for (int i = 0; i < count; i++)
        {
            if (!Equivalent(expectedLines[i], actualLines[i]))
            {
                return $"第 {i + 1} 行\n  期望: {expectedLines[i]}\n  实际: {actualLines[i]}";
            }
        }

        return expectedLines.Length == actualLines.Length
            ? null
            : $"行数不同：期望 {expectedLines.Length}，实际 {actualLines.Length}";
    }

    /// <summary>
    /// v3 跑在 .NET Framework，double 解析/格式化与 .NET 10 有最后一位差异；
    /// 按方案 §10.2 用相对误差 1e-9 比较数值 token，文本 token 仍逐字节比较。
    /// </summary>
    private static bool Equivalent(string expected, string actual)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return true;
        }

        string[] expectedParts = expected.Split('|');
        string[] actualParts = actual.Split('|');
        if (expectedParts.Length != actualParts.Length)
        {
            return false;
        }

        for (int i = 0; i < expectedParts.Length; i++)
        {
            if (!EquivalentToken(expectedParts[i], actualParts[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EquivalentToken(string expected, string actual)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return true;
        }

        if (double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out double left) &&
            double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out double right))
        {
            if (double.IsNaN(left) && double.IsNaN(right))
            {
                return true;
            }

            double scale = Math.Max(1, Math.Max(Math.Abs(left), Math.Abs(right)));
            return Math.Abs(left - right) <= Tolerance * scale;
        }

        char[] separators = [',', ':'];
        string[] expectedItems = expected.Split(separators);
        string[] actualItems = actual.Split(separators);
        if (expectedItems.Length != actualItems.Length)
        {
            return false;
        }

        if (expectedItems.Length == 1)
        {
            return false;
        }

        for (int i = 0; i < expectedItems.Length; i++)
        {
            if (!EquivalentToken(expectedItems[i], actualItems[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KeyMouseStats.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到仓库根目录。");
    }
}
