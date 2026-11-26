using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Kms.Core;
using Kms.Core.Model;
using Kms.Core.Time;
using Kms.Storage.Sqlite;
using Kms.Storage.Text;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Migration;

/// <summary>
/// v2.x 文本存档（format 10~13）→ SQLite 13 表的一次性迁移结果。
/// <see cref="Failed"/> 与 <see cref="Skipped"/> 必须区分：前者要报警，后者是正常路径。
/// </summary>
public sealed class MigrationOutcome
{
    /// <summary>是否真正执行了导入（第二次启动会因已迁移而为 false）。</summary>
    public bool Performed { get; internal set; }

    /// <summary>正常跳过：源目录不存在、源档为空、或库中已迁移完成。</summary>
    public bool Skipped { get; internal set; }

    /// <summary>失败：坏档、备份校验失败、守恒断言失败等；原文本文件与既有库均未被破坏。</summary>
    public bool Failed { get; internal set; }

    /// <summary>面向用户/日志的原因说明。</summary>
    public string Reason { get; internal set; } = "";

    /// <summary>stats.txt 里记录的源格式版本；无 stats.txt 时为默认 10（与 v3 对齐）。</summary>
    public int SourceFormat { get; internal set; }

    public int DaysImported { get; internal set; }

    public int ArchivesImported { get; internal set; }

    public int RulesImported { get; internal set; }

    /// <summary>本次尝试写入的网格行数（不是点击数）。</summary>
    public long GridCellsImported { get; internal set; }

    public string BackupFolder { get; internal set; } = "";

    /// <summary><c>created</c> / <c>reused</c>；未进入备份阶段时为空。</summary>
    public string BackupAction { get; internal set; } = "";

    public string Describe()
    {
        if (Failed)
        {
            return "迁移失败:" + Reason;
        }

        if (Skipped)
        {
            return "跳过迁移:" + Reason;
        }

        if (!Performed)
        {
            return "未执行迁移:" + Reason;
        }

        return "迁移完成:format=" + SourceFormat.ToString(CultureInfo.InvariantCulture)
            + " · 日=" + DaysImported.ToString(CultureInfo.InvariantCulture)
            + " · 月档=" + ArchivesImported.ToString(CultureInfo.InvariantCulture)
            + " · 规则=" + RulesImported.ToString(CultureInfo.InvariantCulture)
            + " · 格子=" + GridCellsImported.ToString(CultureInfo.InvariantCulture)
            + " · 备份=" + BackupAction;
    }
}

/// <summary>
/// v2.x 文本存档 → <c>kmstats.db</c> 的无损自动迁移（1:1 移植自 v3 <c>V2Migrator.cs</c>）。
///
/// 安全契约：
/// <list type="number">
/// <item>只读探测：复用 <see cref="TextStatsCodec.Load(string)"/>，绝不调用任何有损导出/裁剪。</item>
/// <item>坏档保护：文件里有 <c>[day=]</c> 却解析不出任何日记录时立即失败，不覆盖、不移动原文件。</item>
/// <item>原子备份：写库前先把全部源文件复制到 <c>backup_v2\&lt;时间戳&gt;\</c>，
///       写 <c>manifest.txt</c>（sha256 / 相对路径 / 字节数）并逐字节复核；同指纹复用既有备份。</item>
/// <item>单事务导入：meta / rules / archives / days / 窗口·小时·键·点击·会话 / migration_log
///       全在一次 SQLite 事务内完成。</item>
/// <item>三重幂等：<c>migration_log</c> 主键 + 每日 <c>INSERT OR IGNORE</c>（忽略即整日跳过）
///       + 全表主键去重。</item>
/// <item>提交前 5 条守恒硬断言（日击键 / 日点击 / 网格 / 小时桶 / 月档），任一不符回滚并失败。</item>
/// </list>
///
/// 用 <see cref="Run(string, string)"/> 显式传入源目录与库文件路径；关闭
/// <c>KeyMouseStats.exe</c> 的进程互斥由宿主负责，本类型不做假设。
/// </summary>
public static class V2Migrator
{
    /// <summary>源目录下的备份子目录名。</summary>
    public const string BackupFolderName = "backup_v2";

    /// <summary>主文本档文件名。</summary>
    public const string StatsFileName = "stats.txt";

    /// <summary>备份清单文件名。</summary>
    public const string ManifestName = "manifest.txt";

    /// <summary><c>meta.migrated_from_format</c> ≥ 该值即视为已迁移完成。</summary>
    public const int TargetFormat = 1;

    private static readonly string[] Pragmas =
    [
        "PRAGMA journal_mode = WAL;",
        "PRAGMA synchronous = FULL;",
        "PRAGMA foreign_keys = ON;",
        "PRAGMA busy_timeout = 5000;",
        "PRAGMA temp_store = MEMORY;"
    ];

    private sealed class SourceFile
    {
        internal string FullPath = "";
        internal string Relative = "";
        internal long Bytes;
        internal string Sha256 = "";
    }

    /// <summary>
    /// 产品/测试共用入口。
    /// </summary>
    /// <param name="sourceRoot">v2 数据目录；读取 <c>stats.txt</c>、<c>stats.txt.bak</c> 与
    /// <c>YYYY\YYYY-MM.txt</c>。不会被写入或移动。</param>
    /// <param name="dbRoot">目标数据目录；库文件固定为其中的 <c>kmstats.db</c>。
    /// 目录不存在时只在确认有数据、备份成功后才创建。</param>
    /// <returns>迁移结果。任何异常都被收口成 <see cref="MigrationOutcome.Failed"/>，不向调用方抛出。</returns>
    public static MigrationOutcome Run(string sourceRoot, string dbRoot)
    {
        MigrationOutcome outcome = new();
        try
        {
            RunCore(sourceRoot, dbRoot, outcome);
        }
        catch (Exception error)
        {
            outcome.Performed = false;
            outcome.Failed = true;
            outcome.Reason = error.GetType().Name + " · " + error.Message;
            LogDiag(dbRoot, "迁移中止:" + outcome.Reason);
        }

        return outcome;
    }

    private static void RunCore(string sourceRoot, string dbRoot, MigrationOutcome outcome)
    {
        if (string.IsNullOrEmpty(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            outcome.Skipped = true;
            outcome.Reason = "源存档目录不存在";
            return;
        }

        if (string.IsNullOrWhiteSpace(dbRoot))
        {
            outcome.Failed = true;
            outcome.Reason = "数据库目录为空";
            return;
        }

        string dbPath = Path.Combine(Path.GetFullPath(dbRoot), KmsSchema.DbFileName);
        string statsPath = Path.Combine(sourceRoot, StatsFileName);
        List<SourceFile> months = CollectMonthFiles(sourceRoot);
        bool hasStats = File.Exists(statsPath);
        if (!hasStats && months.Count == 0)
        {
            outcome.Skipped = true;
            outcome.Reason = "无 v2 文本存档";
            return;
        }

        // ---- 1. 是否已迁移（只读探测，不建库） -----------------------------
        if (File.Exists(dbPath) && AlreadyMigrated(dbPath, out int existingFormat))
        {
            outcome.Skipped = true;
            outcome.Reason = "已迁移(format=" + existingFormat.ToString(CultureInfo.InvariantCulture) + ")";
            return;
        }

        // ---- 2. 只读解析（绝不触发任何有损导出/裁剪） -----------------------
        string statsText = hasStats ? File.ReadAllText(statsPath, Encoding.UTF8) : "";
        bool hasDayLines = statsText.Contains("[day=", StringComparison.Ordinal);
        StatsData parsed = TextStatsCodec.Load(statsText);
        if (hasDayLines && parsed.History.Count == 0)
        {
            // 与 v3 Store.Load 的坏档判定同源：有分节却一天都解析不出来。
            outcome.Failed = true;
            outcome.Reason = "stats.txt 含 [day=] 分节却解析不出任何日记录,已中止(原文件未被修改)";
            LogDiag(dbRoot, outcome.Reason);
            return;
        }

        // 月档并入；stats.txt 中的当月记录优先（与 v3 Store.MergeMonthlyFiles 同序）。
        for (int i = 0; i < months.Count; i++)
        {
            StatsData monthParsed = TextStatsCodec.Load(File.ReadAllText(months[i].FullPath, Encoding.UTF8));
            foreach (KeyValuePair<string, DayRecord> pair in monthParsed.History)
            {
                parsed.History.TryAdd(pair.Key, pair.Value);
            }
        }

        outcome.SourceFormat = parsed.FormatVersion;

        if (parsed.History.Count == 0 && parsed.Archives.Count == 0 && parsed.Totals.Keys == 0)
        {
            outcome.Skipped = true;
            outcome.Reason = "空档:没有可导入的日记录/月档/累计";
            return;
        }

        // ---- 3. 备份 + 校验 -------------------------------------------------
        List<SourceFile> sources = [];
        if (hasStats)
        {
            sources.Add(Describe(statsPath, StatsFileName));
            string bak = statsPath + ".bak";
            if (File.Exists(bak))
            {
                sources.Add(Describe(bak, StatsFileName + ".bak"));
            }
        }

        sources.AddRange(months);

        if (!EnsureBackup(sourceRoot, sources, out string backupFolder, out string action))
        {
            outcome.Failed = true;
            outcome.Reason = "备份校验失败,已中止(原文件未被修改)";
            return;
        }

        outcome.BackupFolder = backupFolder;
        outcome.BackupAction = action;

        // ---- 4. 单事务导入 ---------------------------------------------------
        string fullDbPath = Path.GetFullPath(dbPath);
        string? dbDirectory = Path.GetDirectoryName(fullDbPath);
        if (!string.IsNullOrEmpty(dbDirectory))
        {
            Directory.CreateDirectory(dbDirectory);
        }

        using (SqliteConnection connection = OpenWriter(fullDbPath))
        {
            ApplyPragmas(connection);
            // KmsSchema.Ensure 不处理 3995；必须先修时间基准再建表（与 DbWriter.Open 同序）。
            Epoch3995Fix.Apply(connection, fullDbPath);
            KmsSchema.Ensure(connection, BuildInfo.Version);

            using SqliteTransaction transaction = connection.BeginTransaction();
            ImportMeta(connection, transaction, parsed);
            ImportRules(connection, transaction, parsed, outcome);
            ImportArchives(connection, transaction, parsed, outcome);
            ImportDays(connection, transaction, parsed, outcome);
            ImportLog(connection, transaction, sources, outcome);
            WriteMigrationMeta(connection, transaction, parsed, outcome, statsPath);
            AssertConservation(connection, transaction, parsed);
            transaction.Commit();
        }

        outcome.Performed = true;
        outcome.Reason = "完成";
        LogDiag(dbRoot, outcome.Describe() + " · backup=" + backupFolder);
    }

    // ---------------------------------------------------------------- 探测

    private static bool AlreadyMigrated(string dbPath, out int format)
    {
        format = 0;
        try
        {
            using SqliteConnection connection = new(new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT v FROM meta WHERE k='migrated_from_format';";
            object? raw = command.ExecuteScalar();
            string? text = raw is null or DBNull
                ? null
                : Convert.ToString(raw, CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                format = value;
            }

            return format >= TargetFormat;
        }
        catch
        {
            // 读不出元信息时按「未迁移」处理：后续导入全靠幂等账本兜底。
            return false;
        }
    }

    private static List<SourceFile> CollectMonthFiles(string sourceRoot)
    {
        List<SourceFile> files = [];
        string[] dirs;
        try
        {
            dirs = Directory.GetDirectories(sourceRoot);
        }
        catch
        {
            return files;
        }

        for (int i = 0; i < dirs.Length; i++)
        {
            string yearName = Path.GetFileName(dirs[i]);
            if (yearName.Length != 4
                || !int.TryParse(yearName, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            string[] monthFiles;
            try
            {
                monthFiles = Directory.GetFiles(dirs[i], "*.txt");
            }
            catch
            {
                continue;
            }

            for (int k = 0; k < monthFiles.Length; k++)
            {
                string relative = yearName + "\\" + Path.GetFileName(monthFiles[k]);
                try
                {
                    files.Add(Describe(monthFiles[k], relative));
                }
                catch
                {
                    // 单个月档读不了不应拖死其余文件；备份阶段会再次校验。
                }
            }
        }

        files.Sort(static (a, b) => string.CompareOrdinal(a.Relative, b.Relative));
        return files;
    }

    private static SourceFile Describe(string fullPath, string relative)
    {
        SourceFile file = new()
        {
            FullPath = fullPath,
            Relative = relative,
            Bytes = new FileInfo(fullPath).Length,
            Sha256 = HashFile(fullPath)
        };
        return file;
    }

    // ---------------------------------------------------------------- 备份

    private static bool EnsureBackup(
        string sourceRoot,
        List<SourceFile> sources,
        out string folder,
        out string action)
    {
        folder = "";
        action = "";
        string backupRoot = Path.Combine(sourceRoot, BackupFolderName);

        string fingerprint = Fingerprint(sources);
        string? reused = FindReusableBackup(backupRoot, fingerprint);
        if (!string.IsNullOrEmpty(reused))
        {
            folder = reused;
            action = "reused";
            return true;
        }

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        folder = Path.Combine(backupRoot, stamp);
        int suffix = 1;
        while (Directory.Exists(folder))
        {
            folder = Path.Combine(
                backupRoot,
                stamp + "-" + suffix.ToString(CultureInfo.InvariantCulture));
            suffix++;
        }

        Directory.CreateDirectory(folder);

        StringBuilder manifest = new();
        for (int i = 0; i < sources.Count; i++)
        {
            SourceFile source = sources[i];
            string target = Path.Combine(folder, BackupRelative(source.Relative));
            string? targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            File.Copy(source.FullPath, target, true);
            manifest.Append(source.Sha256).Append("  ")
                .Append(source.Relative).Append("  ")
                .Append(source.Bytes.ToString(CultureInfo.InvariantCulture))
                .Append(Environment.NewLine);
        }

        manifest.Append("# fingerprint=").Append(fingerprint).Append(Environment.NewLine);

        string manifestPath = Path.Combine(folder, ManifestName);
        File.WriteAllText(manifestPath, manifest.ToString(), new UTF8Encoding(false));

        // 逐字节复核：副本长度与 sha256 必须与源一致。
        for (int i = 0; i < sources.Count; i++)
        {
            SourceFile source = sources[i];
            string target = Path.Combine(folder, BackupRelative(source.Relative));
            if (!File.Exists(target)
                || new FileInfo(target).Length != source.Bytes
                || !string.Equals(HashFile(target), source.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // manifest 自身也要能读回同样的三元组，避免磁盘截断造成「假备份」。
        if (!VerifyManifest(manifestPath, sources))
        {
            return false;
        }

        action = "created";
        return true;
    }

    private static string BackupRelative(string relative) =>
        relative.Replace('\\', Path.DirectorySeparatorChar);

    private static bool VerifyManifest(string manifestPath, List<SourceFile> sources)
    {
        try
        {
            Dictionary<string, (string Sha, long Bytes)> entries =
                new(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(manifestPath, Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] parts = line.Split(["  "], StringSplitOptions.None);
                if (parts.Length != 3
                    || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes))
                {
                    return false;
                }

                entries[parts[1]] = (parts[0], bytes);
            }

            if (entries.Count != sources.Count)
            {
                return false;
            }

            for (int i = 0; i < sources.Count; i++)
            {
                SourceFile source = sources[i];
                if (!entries.TryGetValue(source.Relative, out (string Sha, long Bytes) entry)
                    || entry.Bytes != source.Bytes
                    || !string.Equals(entry.Sha, source.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindReusableBackup(string backupRoot, string fingerprint)
    {
        if (!Directory.Exists(backupRoot))
        {
            return null;
        }

        string[] dirs;
        try
        {
            dirs = Directory.GetDirectories(backupRoot);
        }
        catch
        {
            return null;
        }

        for (int i = 0; i < dirs.Length; i++)
        {
            string manifest = Path.Combine(dirs[i], ManifestName);
            if (!File.Exists(manifest))
            {
                continue;
            }

            try
            {
                string text = File.ReadAllText(manifest, Encoding.UTF8);
                if (text.Contains("# fingerprint=" + fingerprint, StringComparison.Ordinal))
                {
                    return dirs[i];
                }
            }
            catch
            {
                // 坏 manifest 不参与复用。
            }
        }

        return null;
    }

    private static string Fingerprint(List<SourceFile> sources)
    {
        StringBuilder sb = new();
        for (int i = 0; i < sources.Count; i++)
        {
            sb.Append(sources[i].Relative).Append('|')
                .Append(sources[i].Sha256).Append('\n');
        }

        return HashText(sb.ToString());
    }

    private static string HashFile(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using SHA256 sha = SHA256.Create();
        return ToHex(sha.ComputeHash(stream));
    }

    private static string HashText(string text)
    {
        using SHA256 sha = SHA256.Create();
        return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
    }

    private static string ToHex(byte[] bytes)
    {
        StringBuilder sb = new(bytes.Length * 2);
        for (int i = 0; i < bytes.Length; i++)
        {
            sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    // ---------------------------------------------------------------- 导入

    private static void ImportMeta(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StatsData parsed)
    {
        SetIfMissing(connection, transaction, "theme_id", Setting(parsed, "art_theme", "0"));
        SetIfMissing(connection, transaction, "keyboard_layout", Setting(parsed, "keyboard_layout", "0"));
        SetIfMissing(connection, transaction, "idle_threshold", Setting(parsed, "idle_threshold", "60"));
        SetIfMissing(connection, transaction, "backup_keep", Setting(parsed, "backup_keep", "7"));
        SetIfMissing(connection, transaction, "pos_x", Setting(parsed, "pos_x", "-1"));
        SetIfMissing(connection, transaction, "pos_y", Setting(parsed, "pos_y", "-1"));
        SetIfMissing(connection, transaction, "all_time_peak_apm", Setting(parsed, "all_time_peak_apm", "0"));

        string motion = Setting(parsed, "motion_v1", "2|1|0");
        string[] motionFields = motion.Split('|');
        string motionLevel = motionFields.Length > 0 ? motionFields[0] : "2";
        string motionFollow = motionFields.Length > 1 ? motionFields[1] : "1";
        string globalHotkey = motionFields.Length > 2 ? motionFields[2] : "0";
        SetIfMissing(connection, transaction, "motion_level", motionLevel);
        SetIfMissing(connection, transaction, "motion_follow_system", motionFollow);
        SetIfMissing(connection, transaction, "global_hotkey", globalHotkey);

        SetIfMissing(
            connection,
            transaction,
            "text_refresh_seconds",
            Setting(parsed, "text_refresh_v1", "15"));
        SetIfMissing(
            connection,
            transaction,
            "daily_report_day",
            LastGlobalValue(parsed, "daily_report_v1", ""));

        if (parsed.Settings.TryGetValue("mouse_dpi", out string? dpi))
        {
            SetIfMissing(connection, transaction, "mouse_dpi", dpi);
        }

        if (parsed.Settings.TryGetValue("keep_days", out string? keepDays))
        {
            SetIfMissing(connection, transaction, "keep_days", keepDays);
        }

        if (parsed.Settings.TryGetValue("wellbeing_v1", out string? wellbeing)
            && !string.IsNullOrEmpty(wellbeing))
        {
            SetIfMissing(connection, transaction, "wellbeing_v1", wellbeing);
        }

        string shortcut = LastGlobalValue(parsed, "shortcut_model_v1", "");
        if (!string.IsNullOrEmpty(shortcut))
        {
            SetIfMissing(connection, transaction, "shortcut_model_v1", shortcut);
        }
    }

    private static string Setting(StatsData data, string key, string fallback) =>
        data.Settings.TryGetValue(key, out string? value) ? value : fallback;

    private static string LastGlobalValue(StatsData data, string key, string fallback)
    {
        for (int i = data.GlobalLines.Count - 1; i >= 0; i--)
        {
            if (string.Equals(data.GlobalLines[i].Key, key, StringComparison.Ordinal))
            {
                return data.GlobalLines[i].Value;
            }
        }

        return fallback;
    }

    private static void SetIfMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string? value)
    {
        Execute(
            connection,
            transaction,
            "INSERT OR IGNORE INTO meta (k, v) VALUES ($p0, $p1);",
            key,
            value ?? "");
    }

    private static void ImportRules(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StatsData parsed,
        MigrationOutcome outcome)
    {
        long now = NowMs();
        for (int i = 0; i < parsed.AppRules.Count; i++)
        {
            AppRule rule = parsed.AppRules[i];
            if (string.IsNullOrEmpty(rule.RuleKey)
                || rule.CategoryId < 0
                || rule.CategoryId >= V13Codecs.AppCategoryCount)
            {
                continue;
            }

            int changed = Execute(
                connection,
                transaction,
                "INSERT OR IGNORE INTO app_category_rule (rule_key, category_id, source, updated_ms) " +
                "VALUES ($p0,$p1,$p2,$p3);",
                rule.RuleKey,
                rule.CategoryId,
                "user_override",
                now);
            if (changed > 0)
            {
                outcome.RulesImported++;
            }
        }
    }

    private static void ImportArchives(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StatsData parsed,
        MigrationOutcome outcome)
    {
        foreach (KeyValuePair<string, MonthArchive> pair in parsed.Archives)
        {
            MonthArchive archive = pair.Value;
            int changed = Execute(
                connection,
                transaction,
                "INSERT OR IGNORE INTO month_archive (ym, days, keys, clicks, wheel, combos, active_seconds, " +
                "move_meters, sessions, hold_count, hold_discarded, hold_total_ms, hold_max_ms, hold_active_seconds) " +
                "VALUES ($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10,$p11,$p12,$p13);",
                pair.Key,
                archive.Days,
                archive.Keys,
                archive.Clicks,
                archive.Wheel,
                archive.Combos,
                archive.ActiveSeconds,
                archive.MoveMeters,
                archive.Sessions,
                archive.HoldCount,
                archive.HoldDiscarded,
                archive.HoldTotalMs,
                archive.HoldMaxMs,
                archive.HoldActiveSeconds);
            if (changed > 0)
            {
                outcome.ArchivesImported++;
            }

            foreach (KeyValuePair<string, long> app in archive.AppKeys)
            {
                Execute(
                    connection,
                    transaction,
                    "INSERT OR IGNORE INTO month_archive_app (ym, process_path, keys) VALUES ($p0,$p1,$p2);",
                    pair.Key,
                    app.Key,
                    app.Value);
            }
        }
    }

    private static void ImportDays(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StatsData parsed,
        MigrationOutcome outcome)
    {
        List<DayRecord> days = [.. parsed.History.Values];
        days.Sort(static (a, b) => a.Date.CompareTo(b.Date));

        for (int i = 0; i < days.Count; i++)
        {
            DayRecord record = days[i];
            string day = record.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            int inserted = Execute(
                connection,
                transaction,
                "INSERT OR IGNORE INTO daily_summary (day, keys, clicks, left, right, middle, xbuttons, " +
                "wheel, move_px, move_meters, active_seconds, idle_seconds, app_observed_seconds, " +
                "app_switches, peak_apm, hold_count, hold_discarded, hold_total_ms, hold_max_ms, " +
                "hold_active_seconds, sessions, origin) " +
                "VALUES ($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10,$p11,$p12,$p13,$p14,$p15,$p16,$p17,$p18,$p19,$p20,'migrated');",
                day,
                record.Keys,
                record.Clicks,
                record.Left,
                record.Right,
                record.Middle,
                record.XButtons,
                record.Wheel,
                record.MovePx,
                record.MoveMeters,
                record.ActiveSeconds,
                record.IdleSeconds,
                record.AppObservedSeconds,
                record.AppSwitches,
                record.PeakApm,
                record.HoldCount,
                record.HoldDiscarded,
                record.HoldTotalMs,
                record.HoldMaxMs,
                record.HoldActiveSeconds,
                record.Sessions.Count);
            if (inserted == 0)
            {
                // 该日已存在 → 整日跳过（幂等），不重复补派生表。
                continue;
            }

            outcome.DaysImported++;
            ImportHours(connection, transaction, day, record);
            ImportKeys(connection, transaction, day, record);
            ImportWindows(connection, transaction, day, record);
            outcome.GridCellsImported += ImportGrid(connection, transaction, day, record);
        }
    }

    private static void ImportHours(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string day,
        DayRecord record)
    {
        for (int hour = 0; hour < 24; hour++)
        {
            long keys = record.HourKeys[hour];
            long clicks = record.HourClicks[hour];
            double active = record.ActiveHours[hour];
            if (keys == 0 && clicks == 0 && active <= 0)
            {
                continue;
            }

            Execute(
                connection,
                transaction,
                "INSERT OR IGNORE INTO daily_hour_agg (day, hour, keys, clicks, active_seconds) " +
                "VALUES ($p0,$p1,$p2,$p3,$p4);",
                day,
                hour,
                keys,
                clicks,
                active);
        }
    }

    private static void ImportKeys(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string day,
        DayRecord record)
    {
        foreach (KeyValuePair<int, long> pair in record.KeyCounts)
        {
            if (pair.Value <= 0)
            {
                continue;
            }

            Execute(
                connection,
                transaction,
                "INSERT OR IGNORE INTO daily_key_agg (day, vk, cnt) VALUES ($p0,$p1,$p2);",
                day,
                pair.Key,
                pair.Value);
        }
    }

    private static void ImportWindows(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string day,
        DayRecord record)
    {
        foreach (AppUsage app in record.Apps.Values)
        {
            string path = app.ProcessPath ?? "";
            string processKey = path.ToLowerInvariant();
            string title = app.Title ?? "";
            long[] groups = app.KeyGroups;

            Execute(
                connection,
                transaction,
                "INSERT OR IGNORE INTO window_daily_agg (day, process_key, window_title, process_path, " +
                "process_name, keys, clicks, wheel, active_seconds, key_g0, key_g1, key_g2, key_g3, key_g4, " +
                "key_g5, category_id, category_source, semantic_tag) " +
                "VALUES ($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10,$p11,$p12,$p13,$p14,-1,'none','');",
                day,
                processKey,
                title,
                path,
                app.AppName,
                app.Keys,
                app.Clicks,
                app.Wheel,
                app.ActiveSeconds,
                groups[0],
                groups[1],
                groups[2],
                groups[3],
                groups[4],
                groups[5]);
        }
    }

    /// <summary>
    /// 历史单通道网格 → channel = 0（兼容通道）。
    /// 绝不归入左键：否则「左键」视图会显示用户从未点过的热点，等于伪造结论。
    /// v3.0 存档带 chv1 且与 ch 自洽时才按通道导入。
    /// </summary>
    private static long ImportGrid(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string day,
        DayRecord record)
    {
        bool hasFlat = record.ClickCells.Count > 0;
        bool hasChanneled = record.ClickCellsV1.Count > 0;
        if (!hasFlat && !hasChanneled)
        {
            return 0;
        }

        if (hasChanneled && InvariantHolds(record))
        {
            long channeled = 0;
            foreach (KeyValuePair<long, long> pair in record.ClickCellsV1)
            {
                if (pair.Value <= 0)
                {
                    continue;
                }

                int channel = V13Codecs.ClickChannelOfKey(pair.Key);
                V13Codecs.SplitClickCellKey(
                    V13Codecs.ClickCellKeyOfChannelKey(pair.Key),
                    out int cellX,
                    out int cellY);
                Execute(
                    connection,
                    transaction,
                    "INSERT OR IGNORE INTO daily_click_grid (day, channel, cell_x, cell_y, screen_id, count) " +
                    "VALUES ($p0,$p1,$p2,$p3,0,$p4);",
                    day,
                    channel,
                    cellX,
                    cellY,
                    pair.Value);
                channeled++;
            }

            return channeled;
        }

        long cells = 0;
        foreach (KeyValuePair<int, long> pair in record.ClickCells)
        {
            if (pair.Value <= 0)
            {
                continue;
            }

            V13Codecs.SplitClickCellKey(pair.Key, out int flatX, out int flatY);
            Execute(
                connection,
                transaction,
                "INSERT OR IGNORE INTO daily_click_grid (day, channel, cell_x, cell_y, screen_id, count) " +
                "VALUES ($p0,0,$p1,$p2,0,$p3);",
                day,
                flatX,
                flatY,
                pair.Value);
            cells++;
        }

        return cells;
    }

    /// <summary>chv1 与 ch 的一致性校验：分通道总和必须等于全通道汇总；只有 chv1 时直接信任。</summary>
    private static bool InvariantHolds(DayRecord record)
    {
        if (record.ClickCellsV1.Count == 0)
        {
            return false;
        }

        long channeled = 0;
        foreach (KeyValuePair<long, long> pair in record.ClickCellsV1)
        {
            if (pair.Value > 0)
            {
                channeled += pair.Value;
            }
        }

        if (record.ClickCells.Count == 0)
        {
            return true;
        }

        long flat = 0;
        foreach (KeyValuePair<int, long> pair in record.ClickCells)
        {
            if (pair.Value > 0)
            {
                flat += pair.Value;
            }
        }

        return channeled == flat;
    }

    private static void ImportLog(
        SqliteConnection connection,
        SqliteTransaction transaction,
        List<SourceFile> sources,
        MigrationOutcome outcome)
    {
        for (int i = 0; i < sources.Count; i++)
        {
            SourceFile source = sources[i];
            string kind = source.Relative.StartsWith(StatsFileName, StringComparison.Ordinal)
                ? "v2-stats"
                : "v2-month";
            Execute(
                connection,
                transaction,
                "INSERT OR IGNORE INTO migration_log (source_kind, source_id, content_sha256, days_imported, imported_ms) " +
                "VALUES ($p0,$p1,$p2,$p3,$p4);",
                kind,
                source.Relative,
                source.Sha256,
                outcome.DaysImported,
                NowMs());
        }
    }

    private static void WriteMigrationMeta(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StatsData parsed,
        MigrationOutcome outcome,
        string statsPath)
    {
        long dayKeys = 0;
        long dayClicks = 0;
        foreach (DayRecord record in parsed.History.Values)
        {
            dayKeys += record.Keys;
            dayClicks += record.Clicks;
        }

        long archiveKeys = 0;
        foreach (MonthArchive archive in parsed.Archives.Values)
        {
            archiveKeys += archive.Keys;
        }

        SetMeta(connection, transaction, "migrated_from_format", parsed.FormatVersion.ToString(CultureInfo.InvariantCulture));
        SetMeta(connection, transaction, "migrated_at_ms", NowMs().ToString(CultureInfo.InvariantCulture));
        SetMeta(connection, transaction, "migrated_days", outcome.DaysImported.ToString(CultureInfo.InvariantCulture));

        // 原累计只留痕，不参与统计，避免与日记录/月档双计。
        SetMeta(connection, transaction, "legacy_total_keys", parsed.Totals.Keys.ToString(CultureInfo.InvariantCulture));
        SetMeta(connection, transaction, "legacy_total_clicks", parsed.Totals.Clicks.ToString(CultureInfo.InvariantCulture));
        SetMeta(connection, transaction, "legacy_total_left", parsed.Totals.Left.ToString(CultureInfo.InvariantCulture));
        SetMeta(connection, transaction, "legacy_total_right", parsed.Totals.Right.ToString(CultureInfo.InvariantCulture));
        SetMeta(connection, transaction, "legacy_total_middle", parsed.Totals.Middle.ToString(CultureInfo.InvariantCulture));
        SetMeta(connection, transaction, "legacy_total_wheel", parsed.Totals.Wheel.ToString(CultureInfo.InvariantCulture));
        SetMeta(
            connection,
            transaction,
            "legacy_total_delta",
            (parsed.Totals.Keys - (dayKeys + archiveKeys)).ToString(CultureInfo.InvariantCulture));

        // 快照桥基线：此刻的 stats.txt 已被本次迁移完整覆盖。
        if (File.Exists(statsPath))
        {
            SetMeta(
                connection,
                transaction,
                "last_export_ms",
                UnixTime.ToUnixMs(File.GetLastWriteTimeUtc(statsPath)).ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void SetMeta(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string value)
    {
        Execute(
            connection,
            transaction,
            "INSERT OR REPLACE INTO meta (k, v) VALUES ($p0, $p1);",
            key,
            value);
    }

    // ---------------------------------------------------------------- 守恒

    /// <summary>提交前五条硬断言；任一不符抛异常 → 事务自动回滚。</summary>
    private static void AssertConservation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StatsData parsed)
    {
        long sourceKeys = 0;
        long sourceClicks = 0;
        long sourceCells = 0;
        foreach (DayRecord record in parsed.History.Values)
        {
            sourceKeys += record.Keys;
            sourceClicks += record.Clicks;
            foreach (KeyValuePair<int, long> cell in record.ClickCells)
            {
                if (cell.Value > 0)
                {
                    sourceCells += cell.Value;
                }
            }
        }

        // ① 每日击键总和守恒
        long dbKeys = ScalarLong(
            connection,
            transaction,
            "SELECT IFNULL(SUM(keys),0) FROM daily_summary WHERE origin='migrated';");
        AssertEqual(dbKeys, sourceKeys, "① 每日击键总和守恒");

        // ①b 每日点击总和守恒（与击键同源，单独断言更易定位）
        long dbClicks = ScalarLong(
            connection,
            transaction,
            "SELECT IFNULL(SUM(clicks),0) FROM daily_summary WHERE origin='migrated';");
        AssertEqual(dbClicks, sourceClicks, "①b 每日点击总和守恒");

        // ② 网格同源总和守恒（带 chv1 的源档会分通道落库，所以对所有通道求和）
        long dbCells = ScalarLong(
            connection,
            transaction,
            "SELECT IFNULL(SUM(count),0) FROM daily_click_grid;");
        AssertEqual(dbCells, sourceCells, "② 网格同源总和守恒");

        // ③ 小时桶总和守恒
        long hourKeys = ScalarLong(
            connection,
            transaction,
            "SELECT IFNULL(SUM(a.keys),0) FROM daily_hour_agg a " +
            "JOIN daily_summary d ON d.day=a.day WHERE d.origin='migrated';");
        AssertEqual(hourKeys, sourceKeys, "③ 小时桶总和守恒");

        // ④ 月档总和守恒
        long archiveKeys = 0;
        foreach (MonthArchive archive in parsed.Archives.Values)
        {
            archiveKeys += archive.Keys;
        }

        long dbArchiveKeys = ScalarLong(
            connection,
            transaction,
            "SELECT IFNULL(SUM(keys),0) FROM month_archive;");
        AssertEqual(dbArchiveKeys, archiveKeys, "④ 月档总和守恒");
    }

    private static void AssertEqual(long actual, long expected, string what)
    {
        if (actual == expected)
        {
            return;
        }

        throw new InvalidOperationException(
            what + "断言失败(期望 " + expected.ToString(CultureInfo.InvariantCulture)
            + ",实际 " + actual.ToString(CultureInfo.InvariantCulture) + ")");
    }

    // ---------------------------------------------------------------- 快照桥

    /// <summary>
    /// 启动时的受限日汇总同步：<c>stats.txt</c> 的 mtime 晚于 <c>meta.last_export_ms</c> 时，
    /// 说明文本被外部（例如清爽版）改写过。此时只补库中缺失的日期，绝不更新已有行，
    /// 因此天然幂等、不会双重计数。
    /// </summary>
    /// <param name="sourceRoot">v2 数据目录。</param>
    /// <param name="dbRoot">数据目录；库文件固定为其中的 <c>kmstats.db</c>。库不存在时直接返回 0。</param>
    /// <param name="reason">诊断说明。</param>
    /// <returns>补入的天数。</returns>
    public static int SyncDerivedSnapshotIfNewer(string sourceRoot, string dbRoot, out string reason)
    {
        reason = "";
        if (string.IsNullOrEmpty(sourceRoot) || string.IsNullOrWhiteSpace(dbRoot))
        {
            return 0;
        }

        string dbPath = Path.Combine(Path.GetFullPath(dbRoot), KmsSchema.DbFileName);
        string statsPath = Path.Combine(sourceRoot, StatsFileName);
        if (!File.Exists(statsPath) || !File.Exists(dbPath))
        {
            return 0;
        }

        try
        {
            using SqliteConnection connection = OpenWriter(dbPath);
            ApplyPragmas(connection);
            Epoch3995Fix.Apply(connection, dbPath);
            KmsSchema.Ensure(connection, BuildInfo.Version);

            long exported = 0;
            using (SqliteCommand read = connection.CreateCommand())
            {
                read.CommandText = "SELECT v FROM meta WHERE k='last_export_ms';";
                object? raw = read.ExecuteScalar();
                string? text = raw is null or DBNull
                    ? null
                    : Convert.ToString(raw, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(text))
                {
                    _ = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out exported);
                }
            }

            long mtime = UnixTime.ToUnixMs(File.GetLastWriteTimeUtc(statsPath));
            if (mtime <= exported)
            {
                reason = "文本快照未变更";
                return 0;
            }

            StatsData parsed = TextStatsCodec.Load(File.ReadAllText(statsPath, Encoding.UTF8));
            int added = 0;
            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                List<DayRecord> days = [.. parsed.History.Values];
                days.Sort(static (a, b) => a.Date.CompareTo(b.Date));
                for (int i = 0; i < days.Count; i++)
                {
                    DayRecord record = days[i];
                    string day = record.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    int changed = Execute(
                        connection,
                        transaction,
                        "INSERT OR IGNORE INTO daily_summary (day, keys, clicks, left, right, middle, " +
                        "xbuttons, wheel, move_px, move_meters, active_seconds, idle_seconds, origin) " +
                        "VALUES ($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10,$p11,'snapshot-bridge');",
                        day,
                        record.Keys,
                        record.Clicks,
                        record.Left,
                        record.Right,
                        record.Middle,
                        record.XButtons,
                        record.Wheel,
                        record.MovePx,
                        record.MoveMeters,
                        record.ActiveSeconds,
                        record.IdleSeconds);
                    if (changed > 0)
                    {
                        added++;
                    }
                }

                SetMeta(
                    connection,
                    transaction,
                    "last_export_ms",
                    mtime.ToString(CultureInfo.InvariantCulture));
                transaction.Commit();
            }

            reason = "补入 " + added.ToString(CultureInfo.InvariantCulture) + " 天";
            return added;
        }
        catch (Exception error)
        {
            reason = "同步失败:" + error.Message;
            return 0;
        }
    }

    // ---------------------------------------------------------------- SQL

    private static SqliteConnection OpenWriter(string dbPath)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        for (int i = 0; i < Pragmas.Length; i++)
        {
            Execute(connection, null, Pragmas[i]);
        }
    }

    private static int Execute(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params object?[] values)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        for (int i = 0; i < values.Length; i++)
        {
            command.Parameters.AddWithValue(
                "$p" + i.ToString(CultureInfo.InvariantCulture),
                values[i] ?? DBNull.Value);
        }

        return command.ExecuteNonQuery();
    }

    private static long ScalarLong(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? 0
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- 工具

    private static long NowMs() => UnixTime.NowMs(SystemClock.Instance);

    private static void LogDiag(string dbPath, string message)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            return;
        }

        try
        {
            string fullPath = Path.GetFullPath(dbPath);
            string? root = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            Directory.CreateDirectory(root);
            string stamp = UnixTime.ToLocal(NowMs())
                .ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            File.AppendAllText(
                Path.Combine(root, "migration.log"),
                stamp + "  " + message + Environment.NewLine,
                new UTF8Encoding(false));
        }
        catch
        {
            // 诊断日志失败不得影响迁移结果。
        }
    }
}
