using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Kms.Tools.ScrubFixtures;

/// <summary>
/// 清洗后的安全网：不依赖具体表结构，扫描所有文本列与文本档，
/// 发现残留的绝对路径 / 用户目录就报错。
/// </summary>
internal static partial class FixtureScanner
{
    public static IReadOnlyList<string> Verify(string directory)
    {
        List<string> problems = [];

        foreach (string file in Directory.EnumerateFiles(directory, "*.db", SearchOption.AllDirectories))
        {
            problems.AddRange(ScanDatabase(file));
            problems.AddRange(ScanRawDatabase(file));
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*.txt", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(file), "CHECKSUMS.txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            problems.AddRange(ScanTextFile(file));

            if (Path.GetFileName(file).StartsWith("v13-", StringComparison.OrdinalIgnoreCase))
            {
                problems.AddRange(V13PrivacyVerifier.VerifyFile(file));
            }
        }

        return problems;
    }

    private static IEnumerable<string> ScanDatabase(string path)
    {
        using SqliteConnection connection = new($"Data Source={path};Mode=ReadOnly");
        connection.Open();

        foreach (string table in ReadTables(connection))
        {
            List<string> textColumns = ReadTextColumns(connection, table);
            if (textColumns.Count == 0)
            {
                continue;
            }

            foreach (string column in textColumns)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT \"{column}\" FROM \"{table}\" WHERE \"{column}\" IS NOT NULL";
                using SqliteDataReader reader = command.ExecuteReader();

                while (reader.Read())
                {
                    string? value = Convert.ToString(reader.GetValue(0));
                    if (value is not null && LooksSensitive(value))
                    {
                        yield return $"{Path.GetFileName(path)} → {table}.{column} 仍含绝对路径：{Preview(value)}";
                    }
                }
            }
        }
    }

    private static IEnumerable<string> ScanTextFile(string path)
    {
        int lineNumber = 0;

        foreach (string line in File.ReadLines(path))
        {
            lineNumber++;
            if (LooksSensitive(line))
            {
                yield return $"{Path.GetFileName(path)}:{lineNumber} 仍含绝对路径：{Preview(line)}";
            }
        }
    }

    /// <summary>
    /// VACUUM 之后仍检查原始字节，防止 free page / overflow page 里残留旧路径。
    /// </summary>
    private static IEnumerable<string> ScanRawDatabase(string path)
    {
        string fileName = Path.GetFileName(path);
        string raw = Encoding.Latin1.GetString(File.ReadAllBytes(path));

        foreach (string marker in new[]
                 {
                     "C:\\Users\\", "\\Users\\", "/home/", "/Users/", "/root/",
                     "AppData\\", "http://", "https://"
                 })
        {
            if (raw.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{fileName} 原始字节仍含标记：{marker}";
            }
        }
    }

    internal static bool LooksSensitive(string value) => SensitivePath().IsMatch(value);

    private static string Preview(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80] + "…";
    }

    private static List<string> ReadTables(SqliteConnection connection)
    {
        List<string> tables = [];

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static List<string> ReadTextColumns(SqliteConnection connection, string table)
    {
        List<string> columns = [];

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            string type = reader.GetString(2);
            if (type.Contains("TEXT", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("CHAR", StringComparison.OrdinalIgnoreCase) ||
                type.Length == 0)
            {
                columns.Add(reader.GetString(1));
            }
        }

        return columns;
    }

    // 盘符路径、UNC、Unix 用户目录；不匹配普通文件名。
    [GeneratedRegex(@"[A-Za-z]:[\\/]|\\\\[A-Za-z0-9._-]+\\|/(home|Users)/[^/\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex SensitivePath();
}
