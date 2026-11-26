using System.Globalization;
using Kms.Core.Input;
using Kms.Core.Model;
using Kms.Core.Time;
using Kms.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Tests;

public sealed class DbAggregateSyncTests
{
    private const long T0 = 1_700_000_000_000L;

    [Fact]
    public void Sync_UpdatesDerivedColumnsWithoutTouchingCoreCounts()
    {
        string directory = Path.Combine(Path.GetTempPath(), "kms-dbagg-" + Guid.NewGuid().ToString("N"));
        string dbPath = Path.Combine(directory, KmsSchema.DbFileName);
        Directory.CreateDirectory(directory);
        string day = UnixTime.ToLocal(T0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        try
        {
            EventRing ring = new(256);
            using (DbWriter writer = DbWriter.Open(
                dbPath,
                ring,
                new DbWriterOptions { RunSelfHeal = false, Diagnostic = static _ => { } }))
            {
                writer.ResolveWindow = static _ => new WindowTarget
                {
                    ProcessPath = @"C:\Apps\Code.exe",
                    ProcessName = "Code.exe",
                    WindowTitle = "main.cs - Code"
                };

                Assert.True(writer.Enqueue(
                    InputEventKind.KeyDown,
                    new IntPtr(0x101),
                    65,
                    0,
                    0,
                    0,
                    writer.TicksForMs(T0)));
                Assert.Equal(1, writer.DrainOnce());
            }

            StatsData data = new();
            DayRecord record = data.GetOrCreate(UnixTime.ToLocal(T0).Date);
            record.ActiveSeconds = 123.5;
            record.IdleSeconds = 456.25;
            record.MoveMeters = 1.25;
            record.AppObservedSeconds = 99;
            record.AppSwitches = 7;
            record.PeakApm = 321;
            record.HoldCount = 42;
            record.HoldDiscarded = 2;
            record.HoldTotalMs = 12_345.5;
            record.HoldMaxMs = 987.25;
            record.HoldActiveSeconds = 33.5;
            record.HourKeys[10] = 5;
            record.HourClicks[10] = 3;
            record.ActiveHours[10] = 12.5;
            record.KeyCounts[65] = 5;
            AppUsage app = new()
            {
                ProcessPath = @"C:\Apps\Code.exe",
                Title = "main.cs - Code",
                Keys = 5,
                Clicks = 3,
                Wheel = 1,
                ActiveSeconds = 44.5
            };
            app.KeyGroups[0] = 2;
            app.KeyGroups[1] = 3;
            app.HasKeyGroups = true;
            record.Apps[app.Id] = app;
            record.Sessions.Add(new ActiveSession
            {
                Start = DateTime.Now.AddMinutes(-1),
                End = DateTime.Now,
                Seconds = 60
            });

            Assert.Equal(1, DbAggregateSync.Sync(dbPath, data));

            using SqliteConnection db = Open(dbPath);
            Assert.Equal(1, Long(db, "SELECT keys FROM daily_summary WHERE day='" + day + "';"));
            Assert.Equal(123.5, Real(db, "SELECT active_seconds FROM daily_summary WHERE day='" + day + "';"), 6);
            Assert.Equal(1.25, Real(db, "SELECT move_meters FROM daily_summary WHERE day='" + day + "';"), 6);
            Assert.Equal(456.25, Real(db, "SELECT idle_seconds FROM daily_summary WHERE day='" + day + "';"), 6);
            Assert.Equal(99, Real(db, "SELECT app_observed_seconds FROM daily_summary WHERE day='" + day + "';"), 6);
            Assert.Equal(7, Long(db, "SELECT app_switches FROM daily_summary WHERE day='" + day + "';"));
            Assert.Equal(321, Long(db, "SELECT peak_apm FROM daily_summary WHERE day='" + day + "';"));
            Assert.Equal(42, Long(db, "SELECT hold_count FROM daily_summary WHERE day='" + day + "';"));
            Assert.Equal(2, Long(db, "SELECT hold_discarded FROM daily_summary WHERE day='" + day + "';"));
            Assert.Equal(12_345.5, Real(db, "SELECT hold_total_ms FROM daily_summary WHERE day='" + day + "';"), 6);
            Assert.Equal(987.25, Real(db, "SELECT hold_max_ms FROM daily_summary WHERE day='" + day + "';"), 6);
            Assert.Equal(33.5, Real(db, "SELECT hold_active_seconds FROM daily_summary WHERE day='" + day + "';"), 6);
            Assert.Equal(1, Long(db, "SELECT sessions FROM daily_summary WHERE day='" + day + "';"));
            Assert.Equal(5, Long(db, "SELECT keys FROM daily_hour_agg WHERE day='" + day + "' AND hour=10;"));
            Assert.Equal(3, Long(db, "SELECT clicks FROM daily_hour_agg WHERE day='" + day + "' AND hour=10;"));
            Assert.Equal(12.5, Real(db, "SELECT active_seconds FROM daily_hour_agg WHERE day='" + day + "' AND hour=10;"), 6);
            Assert.Equal(5, Long(db, "SELECT cnt FROM daily_key_agg WHERE day='" + day + "' AND vk=65;"));
            Assert.Equal(5, Long(db, "SELECT keys FROM window_daily_agg WHERE day='" + day + "' AND process_key='c:\\apps\\code.exe';"));
            Assert.Equal(44.5, Real(db, "SELECT active_seconds FROM window_daily_agg WHERE day='" + day + "' AND process_key='c:\\apps\\code.exe';"), 6);
            Assert.Equal(2, Long(db, "SELECT key_g0 FROM window_daily_agg WHERE day='" + day + "' AND process_key='c:\\apps\\code.exe';"));
            Assert.Equal(3, Long(db, "SELECT key_g1 FROM window_daily_agg WHERE day='" + day + "' AND process_key='c:\\apps\\code.exe';"));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Sync_MissingDatabaseIsNoOp()
    {
        StatsData data = new();
        data.GetOrCreate(DateTime.Today).Keys = 5;
        Assert.Equal(0, DbAggregateSync.Sync(Path.Combine(Path.GetTempPath(), "kms-none-" + Guid.NewGuid() + ".db"), data));
    }

    private static SqliteConnection Open(string path)
    {
        SqliteConnection connection = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString());
        connection.Open();
        return connection;
    }

    private static long Long(SqliteConnection db, string sql)
    {
        using SqliteCommand command = db.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static double Real(SqliteConnection db, string sql)
    {
        using SqliteCommand command = db.CreateCommand();
        command.CommandText = sql;
        return Convert.ToDouble(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
