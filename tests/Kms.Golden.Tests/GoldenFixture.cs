using Kms.Core.Model;
using Kms.Storage.Text;

namespace Kms.Golden.Tests;

internal static class GoldenFixture
{
    public static string RepoRoot()
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

    public static StatsData LoadStats()
    {
        string path = Path.Combine(RepoRoot(), "tests", "Fixtures", "v13-text-current.txt");
        return TextStatsCodec.Load(File.ReadAllText(path));
    }
}
