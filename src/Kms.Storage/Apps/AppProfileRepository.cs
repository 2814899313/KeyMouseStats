using System.Globalization;
using System.Text;
using Kms.Core.Apps;
using Kms.Core.Model;
using Kms.Core.Time;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Apps;

/// <summary>
/// 应用画像后台只读仓库。
///
/// 设计约束：
/// <list type="bullet">
/// <item>应用级总量优先从内存 <see cref="StatsData.History"/> 聚合；四键点击与标题明细可从
///       window_daily_agg 补齐。</item>
/// <item>小时/VK/热区/会话只从 SQLite v2 分桶与会话表读取；没有记录时返回空并标记
///       <see cref="AppDataCapability"/> Pending，绝不按比例分摊或伪造 0。</item>
/// <item>每次查询使用独立只读短连接，不复制 WAL 库、不在 UI 线程执行。</item>
/// </list>
/// </summary>
public sealed class AppProfileRepository : IDisposable
{
    private const int CacheCapacity = 48;
    private readonly string _databasePath;
    private readonly TimeSpan _cacheTtl;
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _cacheOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _cacheNodes = new(StringComparer.Ordinal);
    private StatsData? _memory;
    private bool _disposed;

    public AppProfileRepository(
        string databasePath,
        StatsData? memorySnapshot = null,
        TimeSpan? cacheTtl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        _memory = memorySnapshot;
        _cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(5);
    }

    public string DatabasePath => _databasePath;

    public void UpdateMemorySnapshot(StatsData? data)
    {
        _memory = data;
        Invalidate();
    }

    /// <summary>保存周期结束或用户手动刷新时调用。</summary>
    public void Invalidate()
    {
        lock (_cacheGate)
        {
            _cache.Clear();
            _cacheOrder.Clear();
            _cacheNodes.Clear();
        }
    }

    public Task<AppProfileSnapshot> QueryAsync(
        AppProfileQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (TryGetCached(query.CacheKey, out AppProfileSnapshot? cached))
        {
            return Task.FromResult(cached);
        }

        return Task.Run(() => QueryCore(query, cancellationToken), cancellationToken);
    }

    public async Task<AppTotalsSnapshot> GetTotalsAsync(
        AppProfileQuery query,
        CancellationToken cancellationToken = default) =>
        (await QueryAsync(query, cancellationToken).ConfigureAwait(false)).Totals;

    public async Task<IReadOnlyList<AppHourlyBucket>> GetHoursAsync(
        AppProfileQuery query,
        CancellationToken cancellationToken = default) =>
        (await QueryAsync(query, cancellationToken).ConfigureAwait(false)).Hours;

    public async Task<IReadOnlyList<AppKeyCount>> GetKeysAsync(
        AppProfileQuery query,
        CancellationToken cancellationToken = default) =>
        (await QueryAsync(query, cancellationToken).ConfigureAwait(false)).Keys;

    public async Task<IReadOnlyList<AppHeatCell>> GetHeatAsync(
        AppProfileQuery query,
        CancellationToken cancellationToken = default) =>
        (await QueryAsync(query, cancellationToken).ConfigureAwait(false)).Heat;

    public async Task<IReadOnlyList<ActiveSession>> GetSessionsAsync(
        AppProfileQuery query,
        CancellationToken cancellationToken = default) =>
        (await QueryAsync(query, cancellationToken).ConfigureAwait(false)).Sessions;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Invalidate();
    }

    private AppProfileSnapshot QueryCore(AppProfileQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTime start = query.Start.Date;
        DateTime end = query.End.Date;
        if (end < start)
        {
            return EmptySnapshot(query);
        }

        string startDay = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string endDay = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        AppTotalsSnapshot totals = new();
        List<AppWindowUsage> windows = [];
        HashSet<string> memoryKeys = new(StringComparer.Ordinal);
        List<AppHourlyBucket> hours = [];
        List<AppKeyCount> keys = [];
        List<AppHeatCell> heat = [];
        List<ActiveSession> sessions = [];
        AppDataCapabilities capabilities = query.Scope.IsAll
            ? AppDataCapabilities.AllExact
            : new AppDataCapabilities(
                AppDataCapability.None,
                AppDataCapability.All,
                "该区间尚无按应用明细；单应用小时/VK/热区自 v4.2.0 起累积。");

        bool hasWindowRows = false;
        bool hasHourRows = false;
        bool hasKeyRows = false;
        bool hasHeatRows = false;
        bool hasMetricRows = false;
        bool hasSessionRows = false;
        double metricMove = 0;
        long metricPeak = 0;
        long metricSessions = 0;
        long metricSwitches = 0;
        double sessionMove = 0;

        AppDirectory directory = new();
        try
        {
            using SqliteConnection? connection = OpenReadOnly();
            bool hasV2 = connection is not null && HasV2Tables(connection);
            if (hasV2)
            {
                directory = LoadDirectory(connection!);
            }

            cancellationToken.ThrowIfCancellationRequested();
            HashSet<int> scopeUids = ResolveScopeUids(query.Scope, directory);
            bool hasMemoryApp = AggregateMemory(
                query,
                directory,
                scopeUids,
                ref totals,
                memoryKeys,
                windows,
                cancellationToken);
            List<string> scopeWindowKeys = ResolveWindowKeys(query.Scope, scopeUids, directory);
            List<string> scopePaths = ResolveProcessPaths(query.Scope, scopeUids, directory);

            if (connection is not null)
            {
                WindowAggregate window = ReadWindowAggregate(
                    connection,
                    startDay,
                    endDay,
                    scopeWindowKeys,
                    query.Scope.IsAll);
                hasWindowRows = window.Rows > 0;
                totals = new AppTotalsSnapshot
                {
                    Keys = totals.Keys > 0 ? totals.Keys : window.Keys,
                    Clicks = totals.Clicks > 0 ? totals.Clicks : window.Clicks,
                    Left = window.Left,
                    Right = window.Right,
                    Middle = window.Middle,
                    XButtons = window.XButtons,
                    Wheel = totals.Wheel > 0 ? totals.Wheel : window.Wheel,
                    ActiveSeconds = totals.ActiveSeconds > 0 ? totals.ActiveSeconds : window.ActiveSeconds,
                    MovePx = totals.MovePx,
                    Sessions = totals.Sessions,
                    Switches = totals.Switches,
                    PeakApm = totals.PeakApm,
                    AppCount = memoryKeys.Count > 0 ? memoryKeys.Count : window.AppCount,
                    KeyGroups = memoryKeys.Count > 0 ? totals.KeyGroups : window.KeyGroups
                };

                if (hasV2 && (query.Scope.IsAll || scopeUids.Count > 0))
                {
                    (hours, hasHourRows) = ReadHours(
                        connection, startDay, endDay, scopeUids, query.Scope.IsAll, cancellationToken);
                    (keys, hasKeyRows) = ReadKeys(
                        connection, startDay, endDay, scopeUids, query.Scope.IsAll, cancellationToken);
                    (heat, hasHeatRows) = ReadHeat(
                        connection, startDay, endDay, scopeUids, query.Scope.IsAll, cancellationToken);
                    (metricMove, metricPeak, metricSessions, metricSwitches, hasMetricRows) =
                        ReadMetrics(connection, startDay, endDay, scopeUids, query.Scope.IsAll);
                }

                if (query.Scope.IsAll || scopePaths.Count > 0)
                {
                    (sessions, hasSessionRows) = ReadSessions(
                        connection, startDay, endDay, scopePaths, query.Scope.IsAll, cancellationToken);
                    sessionMove = sessions.Sum(static session => session.MovePx);
                }
            }

            long sessionCount = hasMetricRows ? metricSessions : sessions.Count;
            totals = new AppTotalsSnapshot
            {
                Keys = totals.Keys,
                Clicks = totals.Clicks,
                Left = totals.Left,
                Right = totals.Right,
                Middle = totals.Middle,
                XButtons = totals.XButtons,
                Wheel = totals.Wheel,
                ActiveSeconds = totals.ActiveSeconds,
                MovePx = hasMetricRows ? metricMove : sessionMove,
                Sessions = sessionCount,
                Switches = metricSwitches,
                PeakApm = metricPeak,
                AppCount = totals.AppCount,
                KeyGroups = totals.KeyGroups
            };

            if (!query.Scope.IsAll)
            {
                AppDataCapability exact = AppDataCapability.None;
                // 内存 AppUsage 与 window_daily_agg 已是全量总量；小时/VK/热区分桶缺失
                // 不能反过来把击键/点击/滚轮/有效时长判成 PENDING。
                if (hasMemoryApp || hasWindowRows || memoryKeys.Count > 0)
                {
                    exact |= AppDataCapability.AppTotals;
                }

                if (hasHourRows)
                {
                    exact |= AppDataCapability.Hourly;
                }

                if (hasKeyRows)
                {
                    exact |= AppDataCapability.KeyDetail;
                }

                if (hasHeatRows)
                {
                    exact |= AppDataCapability.Heatmap;
                }

                if (hasSessionRows || metricSessions > 0)
                {
                    exact |= AppDataCapability.Focus;
                }

                AppDataCapability pending = AppDataCapability.All & ~exact;
                capabilities = new AppDataCapabilities(
                    exact,
                    pending,
                    pending == AppDataCapability.None
                        ? ""
                        : "未采集到的单应用维度返回空；历史数据不做比例分摊。");
            }
        }
        catch (SqliteException)
        {
            // 只读短连接失败（例如 WAL 旁文件不可读）时，内存总量仍然可用；
            // 其余维度保持 Pending，绝不把空结果伪装成精确 0。
        }

        AppProfileSnapshot snapshot = new()
        {
            Filter = query.Filter,
            Scope = query.Scope,
            Start = start,
            End = end,
            Capabilities = capabilities,
            Totals = totals,
            Hours = hours,
            Keys = keys,
            Heat = heat,
            Sessions = sessions,
            Windows = windows,
            SourceNote = "应用总量优先取内存 StatsData；小时/VK/热区/会话取 SQLite v2"
        };
        PutCached(query.CacheKey, snapshot);
        return snapshot;
    }

    private bool AggregateMemory(
        AppProfileQuery query,
        AppDirectory directory,
        IReadOnlySet<int> scopeUids,
        ref AppTotalsSnapshot totals,
        HashSet<string> memoryKeys,
        List<AppWindowUsage> windows,
        CancellationToken cancellationToken)
    {
        StatsData? data = _memory;
        if (data is null)
        {
            return false;
        }

        bool any = false;
        long keys = 0;
        long clicks = 0;
        long wheel = 0;
        double active = 0;
        long[] keyGroups = new long[6];
        for (DateTime date = query.Start.Date; date <= query.End.Date; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dayKey = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!data.History.TryGetValue(dayKey, out DayRecord? day))
            {
                continue;
            }

            foreach (AppUsage app in day.Apps.Values)
            {
                string appKey = AppIdentity.NormalizeKey(app.ProcessPath);
                int uid = directory.Resolve(appKey);
                bool included = query.Scope.IsAll
                    || query.Scope.AppKeys.Contains(appKey)
                    || scopeUids.Contains(uid);
                if (!included && query.Filter.Kind == AppFilterKind.App)
                {
                    // 旧档可能只保存 exe 名或安装路径发生过大小写/目录变化；
                    // 单应用筛选时用文件名兜底，避免内存总量被错判为无数据。
                    string filterKey = AppIdentity.NormalizeKey(query.Filter.AppKey);
                    included = string.Equals(appKey, filterKey, StringComparison.Ordinal)
                        || (appKey.Length > 0
                            && filterKey.Length > 0
                            && string.Equals(
                                System.IO.Path.GetFileName(appKey),
                                System.IO.Path.GetFileName(filterKey),
                                StringComparison.OrdinalIgnoreCase));
                }

                if (!included)
                {
                    continue;
                }

                any = true;
                memoryKeys.Add(appKey.Length == 0 ? "__unattributed__" : appKey);
                keys += app.Keys;
                clicks += app.Clicks;
                wheel += app.Wheel;
                active += app.ActiveSeconds;
                for (int group = 0; group < keyGroups.Length && group < app.KeyGroups.Length; group++)
                {
                    keyGroups[group] += app.KeyGroups[group];
                }

                windows.Add(new AppWindowUsage(
                    appKey,
                    app.ProcessPath ?? "",
                    app.Title ?? "",
                    app.Keys,
                    app.Clicks,
                    app.Wheel,
                    app.ActiveSeconds));
            }
        }

        totals = new AppTotalsSnapshot
        {
            Keys = keys,
            Clicks = clicks,
            Wheel = wheel,
            ActiveSeconds = active,
            AppCount = memoryKeys.Count,
            KeyGroups = keyGroups
        };
        return any;
    }

    private static HashSet<int> ResolveScopeUids(AppScopeSet scope, AppDirectory directory)
    {
        HashSet<int> result = [];
        if (scope.IsAll)
        {
            return result;
        }

        foreach (string key in scope.AppKeys)
        {
            if (directory.UidByKey.TryGetValue(key, out int uid))
            {
                result.Add(uid);
            }

            if (directory.Aliases.TryGetValue(key, out int aliasUid))
            {
                result.Add(aliasUid);
            }
        }

        return result;
    }

    private static List<string> ResolveWindowKeys(
        AppScopeSet scope,
        IReadOnlySet<int> scopeUids,
        AppDirectory directory)
    {
        if (scope.IsAll)
        {
            return [];
        }

        HashSet<string> result = new(StringComparer.Ordinal);
        foreach (string key in scope.AppKeys)
        {
            result.Add(key);
        }

        foreach (int uid in scopeUids)
        {
            if (directory.KeyByUid.TryGetValue(uid, out string? key))
            {
                result.Add(key);
            }
        }

        return [.. result];
    }

    private static List<string> ResolveProcessPaths(
        AppScopeSet scope,
        IReadOnlySet<int> scopeUids,
        AppDirectory directory)
    {
        if (scope.IsAll)
        {
            return [];
        }

        HashSet<string> result = new(StringComparer.Ordinal);
        foreach (string key in scope.AppKeys)
        {
            result.Add(key);
        }

        foreach (int uid in scopeUids)
        {
            if (directory.PathByUid.TryGetValue(uid, out string? path) && path.Length > 0)
            {
                result.Add(path);
            }

            if (directory.KeyByUid.TryGetValue(uid, out string? key))
            {
                result.Add(key);
            }
        }

        return [.. result];
    }

    private static (List<AppHourlyBucket> Hours, bool HasRows) ReadHours(
        SqliteConnection connection,
        string startDay,
        string endDay,
        IReadOnlySet<int> uids,
        bool all,
        CancellationToken cancellationToken)
    {
        List<AppHourlyBucket> result = new(new AppHourlyBucket[24]);
        using SqliteCommand command = connection.CreateCommand();
        StringBuilder sql = new(
            "SELECT hour, IFNULL(SUM(keys),0), IFNULL(SUM(clicks),0), " +
            "IFNULL(SUM(active_seconds),0) FROM daily_app_hour_agg " +
            "WHERE day BETWEEN $start AND $end");
        command.Parameters.AddWithValue("$start", startDay);
        command.Parameters.AddWithValue("$end", endDay);
        int index = 0;
        AppendUidClause(sql, command, "app_uid", uids, all, ref index);
        sql.Append(" GROUP BY hour ORDER BY hour;");
        command.CommandText = sql.ToString();
        bool hasRows = false;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            int hour = reader.GetInt32(0);
            if (hour is < 0 or > 23)
            {
                continue;
            }

            hasRows = true;
            result[hour] = new AppHourlyBucket(
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetDouble(3));
        }

        return (result, hasRows);
    }

    private static (List<AppKeyCount> Keys, bool HasRows) ReadKeys(
        SqliteConnection connection,
        string startDay,
        string endDay,
        IReadOnlySet<int> uids,
        bool all,
        CancellationToken cancellationToken)
    {
        List<AppKeyCount> result = [];
        using SqliteCommand command = connection.CreateCommand();
        StringBuilder sql = new(
            "SELECT vk, IFNULL(SUM(cnt),0) AS total FROM daily_app_key_agg " +
            "WHERE day BETWEEN $start AND $end");
        command.Parameters.AddWithValue("$start", startDay);
        command.Parameters.AddWithValue("$end", endDay);
        int index = 0;
        AppendUidClause(sql, command, "app_uid", uids, all, ref index);
        sql.Append(" GROUP BY vk ORDER BY total DESC, vk;");
        command.CommandText = sql.ToString();
        bool hasRows = false;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            hasRows = true;
            result.Add(new AppKeyCount(reader.GetInt32(0), reader.GetInt64(1)));
        }

        return (result, hasRows);
    }

    private static (List<AppHeatCell> Cells, bool HasRows) ReadHeat(
        SqliteConnection connection,
        string startDay,
        string endDay,
        IReadOnlySet<int> uids,
        bool all,
        CancellationToken cancellationToken)
    {
        List<AppHeatCell> result = [];
        using SqliteCommand command = connection.CreateCommand();
        StringBuilder sql = new(
            "SELECT channel, cell_x, cell_y, IFNULL(SUM(count),0) AS total " +
            "FROM daily_app_click_grid WHERE day BETWEEN $start AND $end");
        command.Parameters.AddWithValue("$start", startDay);
        command.Parameters.AddWithValue("$end", endDay);
        int index = 0;
        AppendUidClause(sql, command, "app_uid", uids, all, ref index);
        sql.Append(" GROUP BY channel, cell_x, cell_y ORDER BY total DESC;");
        command.CommandText = sql.ToString();
        bool hasRows = false;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            hasRows = true;
            result.Add(new AppHeatCell(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt64(3)));
        }

        return (result, hasRows);
    }

    private static (double Move, long Peak, long Sessions, long Switches, bool HasRows) ReadMetrics(
        SqliteConnection connection,
        string startDay,
        string endDay,
        IReadOnlySet<int> uids,
        bool all)
    {
        using SqliteCommand command = connection.CreateCommand();
        StringBuilder sql = new(
            "SELECT IFNULL(SUM(move_px),0), IFNULL(MAX(peak_apm),0), " +
            "IFNULL(SUM(sessions),0), IFNULL(SUM(switches),0), COUNT(*) " +
            "FROM daily_app_metric_agg WHERE day BETWEEN $start AND $end");
        command.Parameters.AddWithValue("$start", startDay);
        command.Parameters.AddWithValue("$end", endDay);
        int index = 0;
        AppendUidClause(sql, command, "app_uid", uids, all, ref index);
        command.CommandText = sql.ToString();
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return (0, 0, 0, 0, false);
        }

        long rows = reader.GetInt64(4);
        return (
            reader.GetDouble(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            rows > 0);
    }

    private static (List<ActiveSession> Sessions, bool HasRows) ReadSessions(
        SqliteConnection connection,
        string startDay,
        string endDay,
        IReadOnlyList<string> paths,
        bool all,
        CancellationToken cancellationToken)
    {
        List<ActiveSession> result = [];
        using SqliteCommand command = connection.CreateCommand();
        StringBuilder sql = new(
            "SELECT start_ts_ms, end_ts_ms, day, active_ms, process_path, process_name, " +
            "window_title, category_id, move_px FROM events_window_session " +
            "WHERE day BETWEEN $start AND $end");
        command.Parameters.AddWithValue("$start", startDay);
        command.Parameters.AddWithValue("$end", endDay);
        int index = 0;
        AppendTextClause(sql, command, "process_path", paths, all, ref index);
        sql.Append(" ORDER BY start_ts_ms, id;");
        command.CommandText = sql.ToString();
        bool hasRows = false;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            hasRows = true;
            long startMs = reader.GetInt64(0);
            long endMs = reader.GetInt64(1);
            long activeMs = reader.GetInt64(3);
            result.Add(new ActiveSession
            {
                Start = UnixTime.ToLocal(startMs),
                End = UnixTime.ToLocal(endMs),
                StartReason = SessionStartReason.Legacy,
                Seconds = activeMs / 1000.0,
                ProcessPath = ReadText(reader, 4),
                ProcessName = ReadText(reader, 5),
                WindowTitle = ReadText(reader, 6),
                CategoryId = reader.IsDBNull(7) ? -1 : reader.GetInt32(7),
                MovePx = reader.IsDBNull(8) ? 0 : reader.GetDouble(8)
            });
        }

        return (result, hasRows);
    }

    private static WindowAggregate ReadWindowAggregate(
        SqliteConnection connection,
        string startDay,
        string endDay,
        IReadOnlyList<string> processKeys,
        bool all)
    {
        using SqliteCommand command = connection.CreateCommand();
        StringBuilder sql = new(
            "SELECT IFNULL(SUM(keys),0), IFNULL(SUM(clicks),0), IFNULL(SUM(left),0), " +
            "IFNULL(SUM(right),0), IFNULL(SUM(middle),0), IFNULL(SUM(xbuttons),0), " +
            "IFNULL(SUM(wheel),0), IFNULL(SUM(active_seconds),0), " +
            "IFNULL(SUM(key_g0),0), IFNULL(SUM(key_g1),0), IFNULL(SUM(key_g2),0), " +
            "IFNULL(SUM(key_g3),0), IFNULL(SUM(key_g4),0), IFNULL(SUM(key_g5),0), " +
            "COUNT(*), COUNT(DISTINCT process_key) FROM window_daily_agg " +
            "WHERE day BETWEEN $start AND $end");
        command.Parameters.AddWithValue("$start", startDay);
        command.Parameters.AddWithValue("$end", endDay);
        int index = 0;
        AppendTextClause(sql, command, "process_key", processKeys, all, ref index);
        command.CommandText = sql.ToString();
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return default;
        }

        return new WindowAggregate(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetDouble(7),
            [
                reader.GetInt64(8), reader.GetInt64(9), reader.GetInt64(10),
                reader.GetInt64(11), reader.GetInt64(12), reader.GetInt64(13)
            ],
            reader.GetInt64(14),
            reader.GetInt32(15));
    }

    private static void AppendUidClause(
        StringBuilder sql,
        SqliteCommand command,
        string column,
        IReadOnlySet<int> uids,
        bool all,
        ref int index)
    {
        if (all)
        {
            return;
        }

        if (uids.Count == 0)
        {
            sql.Append(" AND 1=0");
            return;
        }

        List<string> names = new(uids.Count);
        foreach (int uid in uids)
        {
            string name = "$u" + index.ToString(CultureInfo.InvariantCulture);
            index++;
            names.Add(name);
            command.Parameters.AddWithValue(name, uid);
        }

        sql.Append(" AND ").Append(column).Append(" IN (")
            .Append(string.Join(",", names)).Append(')');
    }

    private static void AppendTextClause(
        StringBuilder sql,
        SqliteCommand command,
        string column,
        IReadOnlyList<string> values,
        bool all,
        ref int index)
    {
        if (all)
        {
            return;
        }

        if (values.Count == 0)
        {
            sql.Append(" AND 1=0");
            return;
        }

        List<string> names = new(values.Count);
        foreach (string value in values)
        {
            string name = "$t" + index.ToString(CultureInfo.InvariantCulture);
            index++;
            names.Add(name);
            command.Parameters.AddWithValue(name, value);
        }

        sql.Append(" AND ").Append(column).Append(" IN (")
            .Append(string.Join(",", names)).Append(')');
    }

    private SqliteConnection? OpenReadOnly()
    {
        if (!File.Exists(_databasePath))
        {
            return null;
        }

        SqliteConnection connection = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());
        try
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout=3000;";
            command.ExecuteNonQuery();
            command.CommandText = "PRAGMA query_only=ON;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            return null;
        }
    }

    private static bool HasV2Tables(SqliteConnection connection)
    {
        foreach (string table in Kms.Storage.Sqlite.KmsSchema.V2Tables)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
            command.Parameters.AddWithValue("$name", table);
            if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static AppDirectory LoadDirectory(SqliteConnection connection)
    {
        AppDirectory directory = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT app_uid, app_key, canonical_path FROM app_dim;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                int uid = Convert.ToInt32(reader.GetInt64(0), CultureInfo.InvariantCulture);
                string key = reader.GetString(1);
                string path = reader.IsDBNull(2) ? "" : reader.GetString(2);
                directory.UidByKey[key] = uid;
                directory.KeyByUid[uid] = key;
                directory.PathByUid[uid] = path;
            }
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT from_app_key, to_app_uid FROM app_alias;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                directory.Aliases[reader.GetString(0)] = Convert.ToInt32(
                    reader.GetInt64(1),
                    CultureInfo.InvariantCulture);
            }
        }

        return directory;
    }

    private bool TryGetCached(string key, out AppProfileSnapshot snapshot)
    {
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out CacheEntry? entry)
                && DateTime.UtcNow - entry.CreatedAt <= _cacheTtl)
            {
                if (_cacheNodes.TryGetValue(key, out LinkedListNode<string>? node))
                {
                    _cacheOrder.Remove(node);
                    _cacheOrder.AddFirst(node);
                }

                snapshot = entry.Snapshot;
                return true;
            }

            if (entry is not null)
            {
                RemoveCached(key);
            }

            snapshot = null!;
            return false;
        }
    }

    private void PutCached(string key, AppProfileSnapshot snapshot)
    {
        lock (_cacheGate)
        {
            RemoveCached(key);
            _cache[key] = new CacheEntry(snapshot, DateTime.UtcNow);
            _cacheNodes[key] = _cacheOrder.AddFirst(key);
            if (_cache.Count > CacheCapacity)
            {
                LinkedListNode<string>? last = _cacheOrder.Last;
                if (last is not null)
                {
                    RemoveCached(last.Value);
                }
            }
        }
    }

    private void RemoveCached(string key)
    {
        _cache.Remove(key);
        if (_cacheNodes.TryGetValue(key, out LinkedListNode<string>? node))
        {
            _cacheOrder.Remove(node);
            _cacheNodes.Remove(key);
        }
    }

    private AppProfileSnapshot EmptySnapshot(AppProfileQuery query) => new()
    {
        Filter = query.Filter,
        Scope = query.Scope,
        Start = query.Start.Date,
        End = query.End.Date,
        Capabilities = query.Scope.IsAll
            ? AppDataCapabilities.AllExact
            : new AppDataCapabilities(
                AppDataCapability.None,
                AppDataCapability.All,
                "日期范围为空或无效。"),
        Totals = new AppTotalsSnapshot(),
        SourceNote = "空区间"
    };

    private static string ReadText(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);

    private readonly record struct WindowAggregate(
        long Keys,
        long Clicks,
        long Left,
        long Right,
        long Middle,
        long XButtons,
        long Wheel,
        double ActiveSeconds,
        IReadOnlyList<long> KeyGroups,
        long Rows,
        int AppCount);

    private sealed record CacheEntry(AppProfileSnapshot Snapshot, DateTime CreatedAt);

    private sealed class AppDirectory
    {
        public Dictionary<string, int> UidByKey { get; } = new(StringComparer.Ordinal);

        public Dictionary<int, string> KeyByUid { get; } = [];

        public Dictionary<int, string> PathByUid { get; } = [];

        public Dictionary<string, int> Aliases { get; } = new(StringComparer.Ordinal);

        public int Resolve(string? appKey)
        {
            string key = AppIdentity.NormalizeKey(appKey);
            if (key.Length == 0)
            {
                return AppIdentity.OtherAppUid;
            }

            if (Aliases.TryGetValue(key, out int aliasUid))
            {
                return aliasUid;
            }

            return UidByKey.GetValueOrDefault(key, AppIdentity.OtherAppUid);
        }
    }
}
