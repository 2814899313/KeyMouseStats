using System.Globalization;
using Kms.Core.Apps;
using Kms.Core.Time;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Sqlite;

/// <summary>
/// 按应用按日增量分桶写入器。只在 DbWriter 的写线程上使用：
/// 事件先进入内存字典，随现有单事务提交一次性 UPSERT 到四张 daily_app_* 表。
/// app_dim 在写线程启动时一次性加载，运行期新应用分配稳定 uid，不查库。
/// </summary>
internal sealed class AppBucketWriter : IDisposable
{
    public const int DailyAppLimit = 64;
    private const int LruCapacity = 256;
    private const int RetentionDays = 30;

    private readonly SqliteConnection _connection;
    private readonly Dictionary<string, int> _uidByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<int, AppDimension> _dimensions = [];
    private readonly Dictionary<string, int> _aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LinkedListNode<string>> _lruNodes = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();
    private readonly HashSet<int> _dayActive = [];
    private readonly Dictionary<int, long> _lastSeen = [];
    private readonly Dictionary<int, AppDimension> _newApps = [];
    private readonly Dictionary<(int Uid, string Day, int Hour), AppHourAgg> _hours = [];
    private readonly Dictionary<(int Uid, string Day, int Vk), long> _keys = [];
    private readonly Dictionary<(int Uid, string Day, int Channel, int CellX, int CellY), long> _cells = [];
    private readonly Dictionary<(int Uid, string Day), AppMetricAgg> _metrics = [];
    private readonly Dictionary<int, AppRateWindow> _rates = [];

    private readonly SqliteCommand _insertApp;
    private readonly SqliteCommand _touchApp;
    private readonly SqliteCommand _upsertHour;
    private readonly SqliteCommand _upsertKey;
    private readonly SqliteCommand _upsertCell;
    private readonly SqliteCommand _upsertMetric;
    private readonly SqliteCommand _cleanupCells;

    private readonly SqliteParameter _appUid;
    private readonly SqliteParameter _appKey;
    private readonly SqliteParameter _displayName;
    private readonly SqliteParameter _canonicalPath;
    private readonly SqliteParameter _firstSeen;
    private readonly SqliteParameter _lastSeenValue;
    private readonly SqliteParameter _flags;
    private readonly SqliteParameter _touchLastSeen;
    private readonly SqliteParameter _touchUid;
    private readonly SqliteParameter _hourUid;
    private readonly SqliteParameter _hourDay;
    private readonly SqliteParameter _hour;
    private readonly SqliteParameter _hourKeys;
    private readonly SqliteParameter _hourClicks;
    private readonly SqliteParameter _hourActive;
    private readonly SqliteParameter _keyUid;
    private readonly SqliteParameter _keyDay;
    private readonly SqliteParameter _keyVk;
    private readonly SqliteParameter _keyCount;
    private readonly SqliteParameter _cellUid;
    private readonly SqliteParameter _cellDay;
    private readonly SqliteParameter _cellChannel;
    private readonly SqliteParameter _cellX;
    private readonly SqliteParameter _cellY;
    private readonly SqliteParameter _cellCount;
    private readonly SqliteParameter _metricUid;
    private readonly SqliteParameter _metricDay;
    private readonly SqliteParameter _metricMove;
    private readonly SqliteParameter _metricPeak;
    private readonly SqliteParameter _metricSessions;
    private readonly SqliteParameter _metricSwitches;
    private readonly SqliteParameter _cleanupBefore;

    private string _day = "";
    private string _lastCleanupDay = "";
    private int _nextUid = 1;

    public AppBucketWriter(SqliteConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        LoadDirectory();

        _insertApp = Command(
            "INSERT OR IGNORE INTO app_dim " +
            "(app_uid, app_key, display_name, canonical_path, first_seen_ms, last_seen_ms, flags) " +
            "VALUES ($uid,$key,$name,$path,$first,$last,$flags);");
        _appUid = Add(_insertApp, "$uid", SqliteType.Integer);
        _appKey = Add(_insertApp, "$key", SqliteType.Text);
        _displayName = Add(_insertApp, "$name", SqliteType.Text);
        _canonicalPath = Add(_insertApp, "$path", SqliteType.Text);
        _firstSeen = Add(_insertApp, "$first", SqliteType.Integer);
        _lastSeenValue = Add(_insertApp, "$last", SqliteType.Integer);
        _flags = Add(_insertApp, "$flags", SqliteType.Integer);

        _touchApp = Command(
            "UPDATE app_dim SET last_seen_ms=MAX(last_seen_ms,$last) WHERE app_uid=$uid;");
        _touchLastSeen = Add(_touchApp, "$last", SqliteType.Integer);
        _touchUid = Add(_touchApp, "$uid", SqliteType.Integer);

        _upsertHour = Command(
            "INSERT INTO daily_app_hour_agg (app_uid, day, hour, keys, clicks, active_seconds) " +
            "VALUES ($uid,$day,$hour,$keys,$clicks,$active) " +
            "ON CONFLICT(app_uid, day, hour) DO UPDATE SET " +
            "keys=keys+excluded.keys, clicks=clicks+excluded.clicks, " +
            "active_seconds=active_seconds+excluded.active_seconds;");
        _hourUid = Add(_upsertHour, "$uid", SqliteType.Integer);
        _hourDay = Add(_upsertHour, "$day", SqliteType.Text);
        _hour = Add(_upsertHour, "$hour", SqliteType.Integer);
        _hourKeys = Add(_upsertHour, "$keys", SqliteType.Integer);
        _hourClicks = Add(_upsertHour, "$clicks", SqliteType.Integer);
        _hourActive = Add(_upsertHour, "$active", SqliteType.Real);

        _upsertKey = Command(
            "INSERT INTO daily_app_key_agg (app_uid, day, vk, cnt) VALUES ($uid,$day,$vk,$cnt) " +
            "ON CONFLICT(app_uid, day, vk) DO UPDATE SET cnt=cnt+excluded.cnt;");
        _keyUid = Add(_upsertKey, "$uid", SqliteType.Integer);
        _keyDay = Add(_upsertKey, "$day", SqliteType.Text);
        _keyVk = Add(_upsertKey, "$vk", SqliteType.Integer);
        _keyCount = Add(_upsertKey, "$cnt", SqliteType.Integer);

        _upsertCell = Command(
            "INSERT INTO daily_app_click_grid (app_uid, day, channel, cell_x, cell_y, count) " +
            "VALUES ($uid,$day,$channel,$x,$y,$count) " +
            "ON CONFLICT(app_uid, day, channel, cell_x, cell_y) DO UPDATE SET count=count+excluded.count;");
        _cellUid = Add(_upsertCell, "$uid", SqliteType.Integer);
        _cellDay = Add(_upsertCell, "$day", SqliteType.Text);
        _cellChannel = Add(_upsertCell, "$channel", SqliteType.Integer);
        _cellX = Add(_upsertCell, "$x", SqliteType.Integer);
        _cellY = Add(_upsertCell, "$y", SqliteType.Integer);
        _cellCount = Add(_upsertCell, "$count", SqliteType.Integer);

        _upsertMetric = Command(
            "INSERT INTO daily_app_metric_agg (app_uid, day, move_px, peak_apm, sessions, switches) " +
            "VALUES ($uid,$day,$move,$peak,$sessions,$switches) " +
            "ON CONFLICT(app_uid, day) DO UPDATE SET " +
            "move_px=move_px+excluded.move_px, " +
            "peak_apm=MAX(peak_apm,excluded.peak_apm), " +
            "sessions=sessions+excluded.sessions, " +
            "switches=switches+excluded.switches;");
        _metricUid = Add(_upsertMetric, "$uid", SqliteType.Integer);
        _metricDay = Add(_upsertMetric, "$day", SqliteType.Text);
        _metricMove = Add(_upsertMetric, "$move", SqliteType.Real);
        _metricPeak = Add(_upsertMetric, "$peak", SqliteType.Integer);
        _metricSessions = Add(_upsertMetric, "$sessions", SqliteType.Integer);
        _metricSwitches = Add(_upsertMetric, "$switches", SqliteType.Integer);

        _cleanupCells = Command("DELETE FROM daily_app_click_grid WHERE day < $before;");
        _cleanupBefore = Add(_cleanupCells, "$before", SqliteType.Text);
    }

    /// <summary>开始新的一天：重置当日 64 应用上限与瞬态速率窗口。</summary>
    public void BeginDay(string day)
    {
        if (string.Equals(_day, day, StringComparison.Ordinal))
        {
            return;
        }

        _day = day;
        _dayActive.Clear();
        _rates.Clear();
        _hours.Clear();
        _keys.Clear();
        _cells.Clear();
        // 不在这里清 _metrics：跨日收口的上一段会话可能刚写入旧日期的 sessions。
    }

    /// <summary>解析前台进程到稳定 app_uid；超出当日 64 应用上限落到 __other__。</summary>
    public int Resolve(string? processPath, string? processName, long timestampMs)
    {
        EnsureDay(timestampMs);
        string key = AppIdentity.NormalizeKey(processPath);
        if (key.Length == 0 || AppIdentity.IsOther(key))
        {
            return AppIdentity.OtherAppUid;
        }

        if (_aliases.TryGetValue(key, out int aliasUid) && TryActivate(aliasUid))
        {
            TouchLru(key, aliasUid, timestampMs);
            return aliasUid;
        }

        if (_uidByKey.TryGetValue(key, out int uid) && TryActivate(uid))
        {
            TouchLru(key, uid, timestampMs);
            return uid;
        }

        if (_dayActive.Count >= DailyAppLimit)
        {
            return AppIdentity.OtherAppUid;
        }

        uid = _nextUid++;
        AppDimension dimension = new()
        {
            Uid = uid,
            Key = key,
            DisplayName = AppIdentity.DisplayName(processPath, processName),
            CanonicalPath = processPath?.Trim() ?? "",
            FirstSeenMs = timestampMs,
            LastSeenMs = timestampMs
        };
        _uidByKey[key] = uid;
        _dimensions[uid] = dimension;
        _newApps[uid] = dimension;
        _dayActive.Add(uid);
        _lastSeen[uid] = timestampMs;
        TouchLru(key, uid, timestampMs);
        return uid;
    }

    public void AddActive(int appUid, long timestampMs, long gapMs)
    {
        if (gapMs <= 0)
        {
            return;
        }

        string day = EnsureDay(timestampMs);
        Hour(appUid, day, HourOf(timestampMs)).ActiveSeconds += gapMs / 1000.0;
    }

    public void AddKey(int appUid, int vk, long timestampMs)
    {
        string day = EnsureDay(timestampMs);
        Hour(appUid, day, HourOf(timestampMs)).Keys++;
        (int, string, int) key = (appUid, day, vk);
        _keys[key] = _keys.GetValueOrDefault(key) + 1;
        UpdatePeak(appUid, day, timestampMs);
    }

    public void AddClick(
        int appUid,
        int channel,
        int cellX,
        int cellY,
        long timestampMs)
    {
        string day = EnsureDay(timestampMs);
        Hour(appUid, day, HourOf(timestampMs)).Clicks++;
        (int, string, int, int, int) key = (appUid, day, channel, cellX, cellY);
        _cells[key] = _cells.GetValueOrDefault(key) + 1;
        UpdatePeak(appUid, day, timestampMs);
    }

    public void AddMove(int appUid, double distancePx, long timestampMs)
    {
        if (distancePx <= 0)
        {
            return;
        }

        string day = EnsureDay(timestampMs);
        Metric(appUid, day).MovePx += distancePx;
    }

    public void RecordSessionClosed(int appUid, long startMs)
    {
        Metric(appUid, DayOf(startMs)).Sessions++;
    }

    public void RecordSwitch(int appUid, long timestampMs)
    {
        string day = EnsureDay(timestampMs);
        Metric(appUid, day).Switches++;
    }

    /// <summary>在 DbWriter 现有事务内提交本批应用分桶。</summary>
    public void Flush(SqliteTransaction transaction, string day)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        _day = day;
        SetTransaction(transaction);
        CleanupOldCells(transaction, day);

        foreach (KeyValuePair<int, AppDimension> pair in _newApps)
        {
            AppDimension dimension = pair.Value;
            _appUid.Value = dimension.Uid;
            _appKey.Value = dimension.Key;
            _displayName.Value = dimension.DisplayName;
            _canonicalPath.Value = dimension.CanonicalPath;
            _firstSeen.Value = dimension.FirstSeenMs;
            _lastSeenValue.Value = dimension.LastSeenMs;
            _flags.Value = 0;
            _insertApp.ExecuteNonQuery();
        }

        foreach (KeyValuePair<int, long> pair in _lastSeen)
        {
            _touchLastSeen.Value = pair.Value;
            _touchUid.Value = pair.Key;
            _touchApp.ExecuteNonQuery();
        }

        foreach (KeyValuePair<(int Uid, string Day, int Hour), AppHourAgg> pair in _hours)
        {
            (int uid, string bucketDay, int hour) = pair.Key;
            AppHourAgg agg = pair.Value;
            _hourUid.Value = uid;
            _hourDay.Value = bucketDay;
            _hour.Value = hour;
            _hourKeys.Value = agg.Keys;
            _hourClicks.Value = agg.Clicks;
            _hourActive.Value = agg.ActiveSeconds;
            _upsertHour.ExecuteNonQuery();
        }

        foreach (KeyValuePair<(int Uid, string Day, int Vk), long> pair in _keys)
        {
            (int uid, string keyDay, int vk) = pair.Key;
            _keyUid.Value = uid;
            _keyDay.Value = keyDay;
            _keyVk.Value = vk;
            _keyCount.Value = pair.Value;
            _upsertKey.ExecuteNonQuery();
        }

        foreach (KeyValuePair<(int Uid, string Day, int Channel, int CellX, int CellY), long> pair in _cells)
        {
            (int uid, string cellDay, int channel, int x, int y) = pair.Key;
            _cellUid.Value = uid;
            _cellDay.Value = cellDay;
            _cellChannel.Value = channel;
            _cellX.Value = x;
            _cellY.Value = y;
            _cellCount.Value = pair.Value;
            _upsertCell.ExecuteNonQuery();
        }

        foreach (KeyValuePair<(int Uid, string Day), AppMetricAgg> pair in _metrics)
        {
            (int uid, string metricDay) = pair.Key;
            AppMetricAgg agg = pair.Value;
            _metricUid.Value = uid;
            _metricDay.Value = metricDay;
            _metricMove.Value = agg.MovePx;
            _metricPeak.Value = agg.PeakApm;
            _metricSessions.Value = agg.Sessions;
            _metricSwitches.Value = agg.Switches;
            _upsertMetric.ExecuteNonQuery();
        }

        _newApps.Clear();
        _lastSeen.Clear();
        _hours.Clear();
        _keys.Clear();
        _cells.Clear();
        _metrics.Clear();
        _rates.Clear();
    }

    public void Dispose()
    {
        _insertApp.Dispose();
        _touchApp.Dispose();
        _upsertHour.Dispose();
        _upsertKey.Dispose();
        _upsertCell.Dispose();
        _upsertMetric.Dispose();
        _cleanupCells.Dispose();
    }

    private void LoadDirectory()
    {
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText =
                "SELECT app_uid, app_key, display_name, canonical_path, first_seen_ms, last_seen_ms " +
                "FROM app_dim;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                int uid = Convert.ToInt32(reader.GetInt64(0), CultureInfo.InvariantCulture);
                string key = reader.GetString(1);
                AppDimension dimension = new()
                {
                    Uid = uid,
                    Key = key,
                    DisplayName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    CanonicalPath = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    FirstSeenMs = reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
                    LastSeenMs = reader.IsDBNull(5) ? 0 : reader.GetInt64(5)
                };
                _dimensions[uid] = dimension;
                _uidByKey[key] = uid;
                if (uid >= _nextUid)
                {
                    _nextUid = uid + 1;
                }
            }
        }

        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT from_app_key, to_app_uid FROM app_alias;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                _aliases[reader.GetString(0)] = Convert.ToInt32(
                    reader.GetInt64(1),
                    CultureInfo.InvariantCulture);
            }
        }

        if (!_dimensions.ContainsKey(AppIdentity.OtherAppUid))
        {
            throw new InvalidOperationException("app_dim 缺少 __other__ 保留行。");
        }
    }

    private bool TryActivate(int uid)
    {
        if (!_dimensions.ContainsKey(uid))
        {
            return false;
        }

        if (_dayActive.Contains(uid))
        {
            return true;
        }

        if (_dayActive.Count >= DailyAppLimit)
        {
            return false;
        }

        _dayActive.Add(uid);
        return true;
    }

    private void TouchLru(string key, int uid, long timestampMs)
    {
        _lastSeen[uid] = timestampMs;
        if (_lruNodes.TryGetValue(key, out LinkedListNode<string>? node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            return;
        }

        node = _lru.AddFirst(key);
        _lruNodes[key] = node;
        if (_lru.Count > LruCapacity)
        {
            LinkedListNode<string>? last = _lru.Last;
            if (last is not null)
            {
                _lru.RemoveLast();
                _lruNodes.Remove(last.Value);
            }
        }
    }

    private string EnsureDay(long timestampMs)
    {
        string day = DayOf(timestampMs);
        BeginDay(day);
        return day;
    }

    private AppHourAgg Hour(int appUid, string day, int hour)
    {
        (int, string, int) key = (appUid, day, hour);
        if (!_hours.TryGetValue(key, out AppHourAgg? agg))
        {
            agg = new AppHourAgg();
            _hours[key] = agg;
        }

        return agg;
    }

    private AppMetricAgg Metric(int appUid, string day)
    {
        (int, string) key = (appUid, day);
        if (!_metrics.TryGetValue(key, out AppMetricAgg? agg))
        {
            agg = new AppMetricAgg();
            _metrics[key] = agg;
        }

        return agg;
    }

    private void UpdatePeak(int appUid, string day, long timestampMs)
    {
        if (!_rates.TryGetValue(appUid, out AppRateWindow? rate))
        {
            rate = new AppRateWindow();
            _rates[appUid] = rate;
        }

        long peak = rate.Add(timestampMs);
        AppMetricAgg metric = Metric(appUid, day);
        if (peak > metric.PeakApm)
        {
            metric.PeakApm = peak;
        }
    }

    private void CleanupOldCells(SqliteTransaction transaction, string day)
    {
        if (string.Equals(_lastCleanupDay, day, StringComparison.Ordinal))
        {
            return;
        }

        if (!DateTime.TryParseExact(
                day,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime date))
        {
            return;
        }

        _cleanupBefore.Value = date
            .AddDays(-(RetentionDays - 1))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _cleanupCells.Transaction = transaction;
        _cleanupCells.ExecuteNonQuery();
        _lastCleanupDay = day;
    }

    private void SetTransaction(SqliteTransaction transaction)
    {
        _insertApp.Transaction = transaction;
        _touchApp.Transaction = transaction;
        _upsertHour.Transaction = transaction;
        _upsertKey.Transaction = transaction;
        _upsertCell.Transaction = transaction;
        _upsertMetric.Transaction = transaction;
        _cleanupCells.Transaction = transaction;
    }

    private SqliteCommand Command(string sql)
    {
        SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private static SqliteParameter Add(SqliteCommand command, string name, SqliteType type) =>
        command.Parameters.Add(name, type);

    private static string DayOf(long unixMs) =>
        UnixTime.ToLocal(unixMs).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static int HourOf(long unixMs) => UnixTime.ToLocal(unixMs).Hour;

    private sealed class AppDimension
    {
        public int Uid;
        public string Key = "";
        public string DisplayName = "";
        public string CanonicalPath = "";
        public long FirstSeenMs;
        public long LastSeenMs;
    }

    private sealed class AppHourAgg
    {
        public long Keys;
        public long Clicks;
        public double ActiveSeconds;
    }

    private sealed class AppMetricAgg
    {
        public double MovePx;
        public long PeakApm;
        public long Sessions;
        public long Switches;
    }

    private sealed class AppRateWindow
    {
        private readonly long[] _seconds = new long[60];
        private long _lastSecond = -1;
        private long _total;

        public long Add(long timestampMs)
        {
            long second = timestampMs / 1000;
            if (_lastSecond < 0 || second < _lastSecond || second - _lastSecond >= 60)
            {
                Array.Clear(_seconds);
                _total = 0;
            }
            else
            {
                for (long s = _lastSecond + 1; s <= second; s++)
                {
                    _total -= _seconds[s % 60];
                    _seconds[s % 60] = 0;
                }
            }

            _seconds[second % 60]++;
            _total++;
            _lastSecond = second;
            return _total;
        }
    }
}
