using Kms.Storage.Package;
using Kms.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Tests;

public sealed class KmsPackageImporterTests
{
    [Fact]
    public void Import_TwiceIsIdempotent()
    {
        string root = FindRepoRoot();
        string directory = Path.Combine(root, ".tmp-package-tests");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".db");

        try
        {
            KmsPackageData package = new()
            {
                DeviceId = "test",
                Days =
                {
                    new KmsDayRecord { Day = "2026-11-22", Keys = 100, Clicks = 10, Wheel = 3, ActiveSeconds = 600 }
                },
                Sessions =
                {
                    new KmsSessionRecord
                    {
                        Day = "2026-11-22", StartMs = 1000, EndMs = 2000, KeyCount = 5,
                        CategoryId = 1, ProcessPath = "a.exe", ProcessName = "a.exe", WindowTitle = "t"
                    }
                }
            };

            KmsMergeResult first = KmsPackageImporter.Import(path, "pkg-1", package);
            KmsMergeResult second = KmsPackageImporter.Import(path, "pkg-1", package);

            Assert.Equal(1, first.DaysApplied);
            Assert.Equal(1, first.SessionsApplied);
            Assert.Equal(0, second.DaysApplied);
            Assert.Equal(0, second.SessionsApplied);

            using SqliteConnection connection = new($"Data Source={path};Mode=ReadOnly");
            connection.Open();
            Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM daily_summary;"));
            Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM events_window_session;"));
            Assert.Equal(2L, Scalar(connection, "SELECT COUNT(*) FROM kms_import_log;"));
            Assert.Equal(100L, Scalar(connection, "SELECT keys FROM daily_summary WHERE day='2026-11-22';"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string file in Directory.EnumerateFiles(directory, Path.GetFileName(path) + "*"))
            {
                File.Delete(file);
            }
        }
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
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
