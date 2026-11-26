namespace Kms.Tools.ScrubFixtures;

internal static class RepoLayout
{
    public static string Root { get; } = FindRoot();

    public static string DefaultSource => Path.Combine(Root, "_fixtures", "v3");

    public static string DefaultDestination => Path.Combine(Root, "tests", "Fixtures");

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KeyMouseStats.slnx")) ||
                File.Exists(Path.Combine(directory.FullName, "KeyMouseStats.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到仓库根目录（KeyMouseStats.slnx）。");
    }
}
