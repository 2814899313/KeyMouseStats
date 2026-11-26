namespace Kms.Tools.ScrubFixtures;

internal static class FixtureScrubber
{
    /// <summary>坏档固件无法保证逐行可解析，保留在本地，另用合成坏档替代。</summary>
    private const string UnscrubbableCorruptFixture = "v13-recovered-corrupt.txt";

    public static void Run(string source, string destination, bool force)
    {
        if (!Directory.Exists(source))
        {
            throw new InvalidOperationException($"源目录不存在：{source}");
        }

        PrepareDestination(destination, force);

        string[] databases = Directory.GetFiles(source, "*.db", SearchOption.TopDirectoryOnly);
        foreach (string database in databases)
        {
            SqliteFixtureScrubber.Scrub(database, Path.Combine(destination, Path.GetFileName(database)));
        }

        int scrubbed = databases.Length;
        foreach (string file in Directory.EnumerateFiles(source, "v13-*.txt", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(file);
            if (string.Equals(name, UnscrubbableCorruptFixture, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TextFixtureScrubber.Scrub(file, Path.Combine(destination, name));
            scrubbed++;
        }

        if (scrubbed == 0)
        {
            throw new InvalidOperationException($"源目录没有可清洗的 v13 文本：{source}");
        }
    }

    private static void PrepareDestination(string destination, bool force)
    {
        Directory.CreateDirectory(destination);

        string[] existing = Directory.GetFiles(destination, "*", SearchOption.AllDirectories)
            .Where(file => !string.Equals(Path.GetFileName(file), "README.md", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (existing.Length > 0 && !force)
        {
            throw new InvalidOperationException($"目标目录已有 {existing.Length} 个固件文件，重跑请加 --force。");
        }

        foreach (string file in existing)
        {
            File.Delete(file);
        }

        foreach (string directory in Directory.GetDirectories(destination))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }
}
