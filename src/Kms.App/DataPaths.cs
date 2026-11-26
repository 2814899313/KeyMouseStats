namespace Kms.App;

/// <summary>v4 与 v3 数据目录分离（方案 §9.2 / D3）。</summary>
internal static class DataPaths
{
    /// <summary>测试/隔离用：设置 <c>KMS4_DATA_DIR</c> 可把 v4 数据目录指到别处。</summary>
    public static string V4Root { get; } = ResolveRoot();

    private static string ResolveRoot()
    {
        string? custom = Environment.GetEnvironmentVariable("KMS4_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return Path.GetFullPath(custom);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeyMouseStats4");
    }

    public static string V4Database { get; } = Path.Combine(V4Root, "kmstats.db");

    public static string V4Text { get; } = Path.Combine(V4Root, "stats.txt");

    public static string V3TextRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "键鼠统计");

    public static string V3Text { get; } = Path.Combine(V3TextRoot, "stats.txt");

    public static string V3Database { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KeyMouseStats",
        "kmstats.db");
}
