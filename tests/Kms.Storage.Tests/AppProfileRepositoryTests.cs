using System.Diagnostics;
using System.Globalization;
using Kms.Core.Apps;
using Kms.Core.Model;
using Kms.Core.Time;
using Kms.Storage.Apps;
using Kms.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Tests;

public sealed class AppProfileRepositoryTests
{
    private const string GoinkPath = @"C:\Apps\Goink.exe";
    private const string GoinkOldPath = @"C:\Old\Goink.exe";
    private const string ComsolPath = @"C:\Apps\COMSOL.exe";
    private static readonly DateTime Day1 = new(2026, 11, 24);
    private static readonly DateTime Day2 = new(2026, 11, 25);

    [Fact]
    public async Task Query_AppScope_UsesMemoryTotalsAndV2Buckets()
    {
        string directory = NewDirectory();
        string dbPath = Path.Combine(directory, KmsSchema.DbFileName);
        try
        {
            CreateDatabase(dbPath);
            SeedApp1(dbPath);
            StatsData memory = MemoryWithGoink();

            using AppProfileRepository repository = new(dbPath, memory);
            AppProfileQuery query = AppProfileQuery.Create(
                AppFilterScope.ByApp(GoinkOldPath),
                null,
                Day1,
                Day1);
            AppProfileSnapshot snapshot = await repository.QueryAsync(query);

            Assert.Equal("app:c:\\old\\goink.exe", query.Filter.CacheKey);
            Assert.Equal(10, snapshot.Totals.Keys);
            Assert.Equal(2, snapshot.Totals.Clicks);
            Assert.Equal(1, snapshot.Totals.Left);
            Assert.Equal(1, snapshot.Totals.Right);
            Assert.Equal(3, snapshot.Totals.Wheel);
            Assert.Equal(5.5, snapshot.Totals.ActiveSeconds, 6);
            Assert.Equal(12.5, snapshot.Totals.MovePx, 6);
            Assert.Equal(2, snapshot.Totals.Sessions);
            Assert.Equal(1, snapshot.Totals.Switches);
            Assert.Equal(30, snapshot.Totals.PeakApm);
            Assert.Equal(1, snapshot.Totals.AppCount);
            Assert.Equal(4, snapshot.Totals.KeyGroups[0]);
            Assert.Single(snapshot.Keys);
            Assert.Equal(7, snapshot.Keys[0].Count);
            Assert.Equal(4, snapshot.Heat[0].Count);
            Assert.Single(snapshot.Sessions);
            Assert.Equal(GoinkPath, snapshot.Sessions[0].ProcessPath);
            Assert.True(snapshot.Capabilities.IsExact(AppDataCapability.AppTotals));
            Assert.True(snapshot.Capabilities.IsExact(AppDataCapability.Hourly));
            Assert.True(snapshot.Capabilities.IsExact(AppDataCapability.KeyDetail));
            Assert.True(snapshot.Capabilities.IsExact(AppDataCapability.Heatmap));
            Assert.True(snapshot.Capabilities.IsExact(AppDataCapability.Focus));
            Assert.True(snapshot.Capabilities.IsPending(AppDataCapability.Chars));
            Assert.True(snapshot.Capabilities.IsPending(AppDataCapability.LiveRate));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Query_NoAppBuckets_ReturnsEmptyWithPendingCapabilities()
    {
        string directory = NewDirectory();
        string dbPath = Path.Combine(directory, KmsSchema.DbFileName);
        try
        {
            CreateDatabase(dbPath);
            StatsData memory = new();
            DayRecord day = memory.GetOrCreate(Day1);
            AppUsage app = new()
            {
                ProcessPath = ComsolPath,
                Title = "model.mph",
                Keys = 99,
                Clicks = 7,
                Wheel = 1,
                ActiveSeconds = 8
            };
            day.Apps[app.Id] = app;

            using AppProfileRepository repository = new(dbPath, memory);
            AppProfileSnapshot snapshot = await repository.QueryAsync(
                AppProfileQuery.Create(AppFilterScope.ByApp(ComsolPath), null, Day1, Day1));

            Assert.Equal(99, snapshot.Totals.Keys);
            Assert.Equal(7, snapshot.Totals.Clicks);
            Assert.True(snapshot.Capabilities.IsExact(AppDataCapability.AppTotals));
            Assert.True(snapshot.Capabilities.IsPending(AppDataCapability.Hourly));
            Assert.True(snapshot.Capabilities.IsPending(AppDataCapability.KeyDetail));
            Assert.True(snapshot.Capabilities.IsPending(AppDataCapability.Heatmap));
            Assert.True(snapshot.Capabilities.IsPending(AppDataCapability.Focus));
            Assert.All(snapshot.Hours, bucket => Assert.Equal(0, bucket.Keys));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Query_MemoryAppsWithoutV2Tables_AppTotalsRemainExact()
    {
        string directory = NewDirectory();
        string dbPath = Path.Combine(directory, KmsSchema.DbFileName);
        try
        {
            using (SqliteConnection db = Open(dbPath, SqliteOpenMode.ReadWriteCreate))
            {
                foreach (string statement in KmsSchema.Statements())
                {
                    Execute(db, statement);
                }
            }

            StatsData memory = new();
            DayRecord day = memory.GetOrCreate(Day1);
            AppUsage app = new()
            {
                ProcessPath = ComsolPath,
                Title = "model.mph",
                Keys = 99,
                Clicks = 7,
                Wheel = 1,
                ActiveSeconds = 8
            };
            app.KeyGroups[0] = 12;
            app.HasKeyGroups = true;
            day.Apps[app.Id] = app;

            using AppProfileRepository repository = new(dbPath, memory);
            AppProfileSnapshot snapshot = await repository.QueryAsync(
                AppProfileQuery.Create(AppFilterScope.ByApp(ComsolPath), null, Day1, Day1));

            Assert.Equal(99, snapshot.Totals.Keys);
            Assert.Equal(7, snapshot.Totals.Clicks);
            Assert.Equal(1, snapshot.Totals.Wheel);
            Assert.Equal(8, snapshot.Totals.ActiveSeconds, 6);
            Assert.Equal(12, snapshot.Totals.KeyGroups[0]);
            Assert.True(snapshot.Capabilities.IsExact(AppDataCapability.AppTotals));
            Assert.False(snapshot.Capabilities.IsPending(AppDataCapability.AppTotals));
            Assert.True(snapshot.Capabilities.IsPending(AppDataCapability.Hourly));
            Assert.True(snapshot.Capabilities.IsPending(AppDataCapability.KeyDetail));
            Assert.True(snapshot.Capabilities.IsPending(AppDataCapability.Heatmap));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Query_GroupedAppScope_AggregatesMultiplePaths()
    {
        string directory = NewDirectory();
        string dbPath = Path.Combine(directory, KmsSchema.DbFileName);
        const string oldChatGpt = @"C:\Old\ChatGPT.exe";
        const string newChatGpt = @"C:\New\ChatGPT.exe";
        try
        {
            CreateDatabase(dbPath);
            using (SqliteConnection db = Open(dbPath, SqliteOpenMode.ReadWrite))
            {
                InsertApp(db, 2, newChatGpt, "ChatGPT.exe");
            }

            StatsData memory = new();
            DayRecord day = memory.GetOrCreate(Day1);
            AppUsage oldApp = new()
            {
                ProcessPath = oldChatGpt,
                Title = "old",
                Keys = 10,
                Clicks = 1,
                Wheel = 2,
                ActiveSeconds = 3
            };
            AppUsage newApp = new()
            {
                ProcessPath = newChatGpt,
                Title = "new",
                Keys = 20,
                Clicks = 2,
                Wheel = 4,
                ActiveSeconds = 6
            };
            day.Apps[oldApp.Id] = oldApp;
            day.Apps[newApp.Id] = newApp;

            AppFilterScope filter = AppFilterScope.ByAppGroup(
                newChatGpt,
                [oldChatGpt, newChatGpt]);
            using AppProfileRepository repository = new(dbPath, memory);
            AppProfileSnapshot snapshot = await repository.QueryAsync(
                AppProfileQuery.Create(filter, null, Day1, Day1));

            Assert.Equal(2, filter.ToScopeSet().Count);
            Assert.Equal(30, snapshot.Totals.Keys);
            Assert.Equal(3, snapshot.Totals.Clicks);
            Assert.Equal(6, snapshot.Totals.Wheel);
            Assert.Equal(9, snapshot.Totals.ActiveSeconds, 6);
            Assert.Equal(2, snapshot.Totals.AppCount);
            Assert.True(snapshot.Capabilities.IsExact(AppDataCapability.AppTotals));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Query_CategoryScope_AggregatesExpandedApps()
    {
        string directory = NewDirectory();
        string dbPath = Path.Combine(directory, KmsSchema.DbFileName);
        try
        {
            CreateDatabase(dbPath);
            SeedApp1(dbPath);
            using (SqliteConnection db = Open(dbPath, SqliteOpenMode.ReadWrite))
            {
                InsertApp(db, 2, ComsolPath, "COMSOL.exe");
                Execute(db,
                    "INSERT INTO daily_app_hour_agg (app_uid,day,hour,keys,clicks,active_seconds) " +
                    "VALUES (2,'" + Day1.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "',9,20,1,4);");
            }

            StatsData memory = MemoryWithGoink();
            DayRecord day = memory.GetOrCreate(Day1);
            AppUsage comsol = new()
            {
                ProcessPath = ComsolPath,
                Title = "model.mph",
                Keys = 20,
                Clicks = 1,
                ActiveSeconds = 4
            };
            day.Apps[comsol.Id] = comsol;

            using AppProfileRepository repository = new(dbPath, memory);
            AppProfileSnapshot snapshot = await repository.QueryAsync(
                AppProfileQuery.Create(
                    AppFilterScope.ByCategory("science"),
                    [GoinkOldPath, ComsolPath],
                    Day1,
                    Day1));

            Assert.Equal("cat:science", snapshot.Scope.CacheKey);
            Assert.Equal(30, snapshot.Totals.Keys);
            Assert.Equal(2, snapshot.Totals.AppCount);
            Assert.Equal(30, snapshot.Hours[9].Keys);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Query_All_ReturnsAllExactAndDoesNotDependOnAppDim()
    {
        string directory = NewDirectory();
        string dbPath = Path.Combine(directory, KmsSchema.DbFileName);
        try
        {
            CreateDatabase(dbPath);
            SeedApp1(dbPath);
            StatsData memory = MemoryWithGoink();

            using AppProfileRepository repository = new(dbPath, memory);
            AppProfileSnapshot snapshot = await repository.QueryAsync(
                AppProfileQuery.Create(AppFilterScope.All, null, Day1, Day1));

            Assert.True(snapshot.Capabilities.IsExact(AppDataCapability.All));
            Assert.Equal(AppDataCapability.None, snapshot.Capabilities.Pending);
            Assert.Equal(10, snapshot.Totals.Keys);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Query_MonthSingleApp_CompletesWithinInteractiveBudget()
    {
        string directory = NewDirectory();
        string dbPath = Path.Combine(directory, KmsSchema.DbFileName);
        try
        {
            CreateDatabase(dbPath);
            using (SqliteConnection db = Open(dbPath, SqliteOpenMode.ReadWrite))
            using (SqliteTransaction transaction = db.BeginTransaction())
            {
                using SqliteCommand hour = db.CreateCommand();
                hour.Transaction = transaction;
                hour.CommandText =
                    "INSERT INTO daily_app_hour_agg (app_uid,day,hour,keys,clicks,active_seconds) " +
                    "VALUES (1,$day,$hour,1,0,1);";
                SqliteParameter hourDay = hour.Parameters.Add("$day", SqliteType.Text);
                SqliteParameter hourValue = hour.Parameters.Add("$hour", SqliteType.Integer);
                for (int offset = 0; offset < 31; offset++)
                {
                    string day = Day1.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    for (int h = 0; h < 24; h++)
                    {
                        hourDay.Value = day;
                        hourValue.Value = h;
                        hour.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }

            using AppProfileRepository repository = new(dbPath, MemoryWithGoink());
            Stopwatch watch = Stopwatch.StartNew();
            AppProfileSnapshot snapshot = await repository.QueryAsync(
                AppProfileQuery.Create(
                    AppFilterScope.ByApp(GoinkPath),
                    null,
                    Day1,
                    Day1.AddDays(30)));
            watch.Stop();

            Assert.Equal(744, snapshot.Hours.Sum(static bucket => bucket.Keys));
            Assert.True(watch.ElapsedMilliseconds < 2_000,
                $"单应用 Month 查询应低于 2s，实际 {watch.ElapsedMilliseconds}ms");
        }
        finally
        {
            DeleteDirectory(dir: directory);
        }
    }

    private static StatsData MemoryWithGoink()
    {
        StatsData data = new();
        DayRecord day = data.GetOrCreate(Day1);
        AppUsage app = new()
        {
            ProcessPath = GoinkOldPath,
            Title = "document.gk",
            Keys = 10,
            Clicks = 2,
            Wheel = 3,
            ActiveSeconds = 5.5
        };
        app.KeyGroups[0] = 4;
        app.HasKeyGroups = true;
        day.Apps[app.Id] = app;
        return data;
    }

    private static void CreateDatabase(string path)
    {
        using SqliteConnection db = Open(path, SqliteOpenMode.ReadWriteCreate);
        KmsSchema.Ensure(db, "4.2.0-test");
        InsertApp(db, 1, GoinkPath, "Goink.exe");
        Execute(db,
            "INSERT INTO app_alias (from_app_key,to_app_uid,reason,updated_ms) " +
            "VALUES ('c:\\old\\goink.exe',1,'安装路径变更',0);");
    }

    private static void SeedApp1(string path)
    {
        using SqliteConnection db = Open(path, SqliteOpenMode.ReadWrite);
        string day = Day1.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Execute(db,
            "INSERT INTO daily_app_hour_agg (app_uid,day,hour,keys,clicks,active_seconds) " +
            "VALUES (1,'" + day + "',9,10,2,5.5);");
        Execute(db,
            "INSERT INTO daily_app_hour_agg (app_uid,day,hour,keys,clicks,active_seconds) " +
            "VALUES (1,'" + day + "',10,5,0,2);");
        Execute(db,
            "INSERT INTO daily_app_key_agg (app_uid,day,vk,cnt) VALUES (1,'" + day + "',65,7);");
        Execute(db,
            "INSERT INTO daily_app_click_grid (app_uid,day,channel,cell_x,cell_y,count) " +
            "VALUES (1,'" + day + "',1,3,6,4);");
        Execute(db,
            "INSERT INTO daily_app_metric_agg (app_uid,day,move_px,peak_apm,sessions,switches) " +
            "VALUES (1,'" + day + "',12.5,30,2,1);");
        Execute(db,
            "INSERT INTO window_daily_agg (day,process_key,window_title,process_path,process_name," +
            "keys,clicks,left,right,middle,xbuttons,wheel,active_seconds) " +
            "VALUES ('" + day + "','c:\\apps\\goink.exe','document.gk','" + GoinkPath + "'," +
            "'Goink.exe',10,2,1,1,0,0,3,5.5);");
        long start = new DateTimeOffset(Day1.AddHours(9)).ToUnixTimeMilliseconds();
        Execute(db,
            "INSERT INTO events_window_session (start_ts_ms,end_ts_ms,day,process_path,process_name," +
            "window_title,key_count,left_clicks,right_clicks,middle_clicks,x_clicks,wheel,move_px," +
            "active_ms,category_id,category_source,is_open) " +
            "VALUES (" + start.ToString(CultureInfo.InvariantCulture) + "," +
            (start + 60_000).ToString(CultureInfo.InvariantCulture) + ",'" + day + "','" +
            GoinkPath + "','Goink.exe','document.gk',10,1,1,0,0,3,12.5,60_000,-1,'none',0);");
    }

    private static void InsertApp(SqliteConnection db, int uid, string path, string name)
    {
        using SqliteCommand command = db.CreateCommand();
        command.CommandText =
            "INSERT OR IGNORE INTO app_dim (app_uid,app_key,display_name,canonical_path," +
            "first_seen_ms,last_seen_ms,flags) VALUES ($uid,$key,$name,$path,0,0,0);";
        command.Parameters.AddWithValue("$uid", uid);
        command.Parameters.AddWithValue("$key", path.ToLowerInvariant());
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$path", path);
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection db, string sql)
    {
        using SqliteCommand command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        SqliteConnection connection = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = mode,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());
        connection.Open();
        return connection;
    }

    private static string NewDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "kms-app-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
