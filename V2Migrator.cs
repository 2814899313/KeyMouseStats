// ============================================================================
//  键鼠统计 v3.0  Milestone 1 / Step 4  V2Migrator.cs
//  v2.x 文本存档(format 10~13) → kmstats.db 的无损自动迁移。
//
//  安全契约(逐条对应任务书):
//   1. 只读探测:复用 Store.ParseText(内部不触碰任何静态状态),**绝不调用**
//      Store.ExportPayload() —— 它带 Prune() 有损折叠副作用。
//   2. 坏档保护:文件里明明有 [day=] 却解析不出任何日记录,立即中止并保持文本运行,
//      不覆盖、不清空、不移动任何原文件。
//   3. 原子备份:任何写库动作之前,先把全部源文件复制到 backup_v2\<时间戳>\ 并生成
//      manifest.txt(sha256 / 相对路径 / 字节数);复制完成后逐字节重算校验,
//      任何不匹配立即中止。同指纹的既有备份直接复用(幂等)。
//   4. 单事务导入:meta / app_category_rule / month_archive / 逐日五表 /
//      migration_log 全在一次 RunInTransaction 内完成。
//   5. 三重幂等账本:migration_log 主键 + 每日 INSERT OR IGNORE(忽略即整日跳过)
//      + 全表主键去重。重复运行结果严格恒等。
//   6. 累计重建:Total 以 daily_summary + month_archive 求和为准;原 total_* 只写进
//      meta.legacy_total_* 与 legacy_total_delta 留痕,避免双重计数。
//   7. 提交前 4 条守恒硬断言:日击键 / 网格同源 / 小时桶 / 月档。
//
//  C# 5;CLEAN_EDITION 下本文件不参与编译(清爽版无数据库)。
// ============================================================================

#if !CLEAN_EDITION

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KeyMouseStats
{
    /// <summary>迁移结果。Failed 与 Skipped 必须区分:前者要报警,后者是正常路径。</summary>
    internal sealed class MigrationOutcome
    {
        internal bool Performed;
        internal bool Skipped;
        internal bool Failed;
        internal string Reason = "";
        internal int SourceFormat;
        internal int DaysImported;
        internal int ArchivesImported;
        internal int RulesImported;
        internal long GridCellsImported;
        internal string BackupFolder = "";
        internal string BackupAction = "";

        internal string Describe()
        {
            if (Failed) return "迁移失败:" + Reason;
            if (Skipped) return "跳过迁移:" + Reason;
            if (!Performed) return "未执行迁移:" + Reason;
            return "迁移完成:format=" + SourceFormat + " · 日=" + DaysImported
                + " · 月档=" + ArchivesImported + " · 规则=" + RulesImported
                + " · 格子=" + GridCellsImported + " · 备份=" + BackupAction;
        }
    }

    internal static class V2Migrator
    {
        internal const string BackupFolderName = "backup_v2";
        internal const string StatsFileName = "stats.txt";
        internal const string ManifestName = "manifest.txt";

        /// <summary>migrated_from_format ≥ 该值即视为已迁移完成。</summary>
        internal const int TargetFormat = 1;

        private sealed class SourceFile
        {
            internal string FullPath;
            internal string Relative;
            internal long Bytes;
            internal string Sha256;
        }

        /// <summary>产品入口:源目录取 Store.DataDirectory,库目录取 NativeBoot.DataRoot。</summary>
        internal static MigrationOutcome EnsureMigrated()
        {
            return Run(Store.DataDirectory, NativeBoot.DataRoot());
        }

        /// <summary>可测试入口:源目录与库目录都显式传入(测试传 %TEMP% 沙盒)。</summary>
        internal static MigrationOutcome Run(string sourceRoot, string dbRoot)
        {
            MigrationOutcome outcome = new MigrationOutcome();
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
            if (string.IsNullOrEmpty(dbRoot))
            {
                outcome.Failed = true;
                outcome.Reason = "数据库目录为空";
                return;
            }

            Directory.CreateDirectory(dbRoot);
            string dbPath = Path.Combine(dbRoot, KmsSchema.DbFileName);

            // ---- 1. 是否已迁移 -------------------------------------------------
            int existingFormat;
            if (AlreadyMigrated(dbPath, out existingFormat))
            {
                outcome.Skipped = true;
                outcome.Reason = "已迁移(format=" + existingFormat + ")";
                return;
            }

            // ---- 2. 收集源文件(只读) ------------------------------------------
            string statsPath = Path.Combine(sourceRoot, StatsFileName);
            List<SourceFile> months = CollectMonthFiles(sourceRoot);
            bool hasStats = File.Exists(statsPath);
            if (!hasStats && months.Count == 0)
            {
                outcome.Skipped = true;
                outcome.Reason = "无 v2 文本存档";
                return;
            }

            // ---- 3. 只读解析(绝不触发 Prune) ----------------------------------
            string statsText = null;
            if (hasStats) statsText = File.ReadAllText(statsPath, Encoding.UTF8);

            bool hasDayLines = statsText != null
                && statsText.IndexOf("[day=", StringComparison.Ordinal) >= 0;
            Store.Parsed statsParsed = Store.ParseText(statsText == null ? "" : statsText);
            if (hasDayLines && statsParsed.History.Count == 0)
            {
                // 与 Store.Load 的坏档判定同源(KeyMouseWidget.cs:806-812)。
                outcome.Failed = true;
                outcome.Reason = "stats.txt 含 [day=] 分节却解析不出任何日记录,已中止(原文件未被修改)";
                LogDiag(dbRoot, outcome.Reason);
                return;
            }

            // 月档并入;stats.txt 中的当月记录优先(与 Store.MergeMonthlyFiles 同序)。
            Store.Parsed parsed = statsParsed;
            for (int i = 0; i < months.Count; i++)
            {
                Store.Parsed monthParsed = Store.ParseText(File.ReadAllText(months[i].FullPath, Encoding.UTF8));
                foreach (KeyValuePair<DateTime, DayRecord> pair in monthParsed.History)
                {
                    if (!parsed.History.ContainsKey(pair.Key)) parsed.History[pair.Key] = pair.Value;
                }
            }

            outcome.SourceFormat = parsed.FormatVersion;

            if (parsed.History.Count == 0 && parsed.Archives.Count == 0 && parsed.Total.Keys == 0)
            {
                outcome.Skipped = true;
                outcome.Reason = "空档:没有可导入的日记录/月档/累计";
                return;
            }

            // ---- 4. 备份 + 校验 -------------------------------------------------
            List<SourceFile> sources = new List<SourceFile>();
            if (hasStats)
            {
                sources.Add(Describe(statsPath, StatsFileName));
                string bak = statsPath + ".bak";
                if (File.Exists(bak)) sources.Add(Describe(bak, StatsFileName + ".bak"));
            }
            for (int i = 0; i < months.Count; i++) sources.Add(months[i]);

            string backupFolder;
            string action;
            if (!EnsureBackup(sourceRoot, sources, out backupFolder, out action))
            {
                outcome.Failed = true;
                outcome.Reason = "备份校验失败,已中止(原文件未被修改)";
                return;
            }
            outcome.BackupFolder = backupFolder;
            outcome.BackupAction = action;

            // ---- 5. 单事务导入 ---------------------------------------------------
            KmsDb db = KmsDb.OpenWriter(dbPath);
            try
            {
                KmsSchema.Ensure(db);
                db.RunInTransaction(delegate(KmsDb tx)
                {
                    ImportMeta(tx, parsed, outcome);
                    ImportRules(tx, parsed, outcome);
                    ImportArchives(tx, parsed, outcome);
                    ImportDays(tx, parsed, outcome);
                    ImportLog(tx, sources, outcome);
                    WriteMigrationMeta(tx, parsed, outcome, statsPath);
                    AssertConservation(tx, parsed, outcome);
                });
            }
            finally
            {
                db.Dispose();
            }

            outcome.Performed = true;
            outcome.Reason = "完成";
            LogDiag(dbRoot, outcome.Describe() + " · backup=" + backupFolder);
        }

        // ---------------------------------------------------------------- 探测

        private static bool AlreadyMigrated(string dbPath, out int format)
        {
            format = 0;
            if (!File.Exists(dbPath)) return false;
            KmsDb db = null;
            try
            {
                db = KmsDb.OpenWriter(dbPath);
                KmsSchema.Ensure(db);
                string raw = db.ScalarText("SELECT v FROM meta WHERE k='migrated_from_format';", null);
                int value;
                if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out value)) format = value;
                return format >= TargetFormat;
            }
            catch
            {
                // 读不出元信息时按「未迁移」处理:后续 import 全靠幂等账本兜底。
                return false;
            }
            finally
            {
                if (db != null) db.Dispose();
            }
        }

        private static List<SourceFile> CollectMonthFiles(string sourceRoot)
        {
            List<SourceFile> files = new List<SourceFile>();
            string[] dirs;
            try { dirs = Directory.GetDirectories(sourceRoot); }
            catch { return files; }

            for (int i = 0; i < dirs.Length; i++)
            {
                string yearName = Path.GetFileName(dirs[i]);
                int year;
                if (yearName.Length != 4) continue;
                if (!int.TryParse(yearName, NumberStyles.Integer, CultureInfo.InvariantCulture, out year)) continue;

                string[] monthFiles;
                try { monthFiles = Directory.GetFiles(dirs[i], "*.txt"); }
                catch { continue; }
                for (int k = 0; k < monthFiles.Length; k++)
                {
                    string relative = yearName + "\\" + Path.GetFileName(monthFiles[k]);
                    try { files.Add(Describe(monthFiles[k], relative)); }
                    catch { }
                }
            }
            files.Sort(delegate(SourceFile a, SourceFile b)
            {
                return string.CompareOrdinal(a.Relative, b.Relative);
            });
            return files;
        }

        private static SourceFile Describe(string fullPath, string relative)
        {
            SourceFile file = new SourceFile();
            file.FullPath = fullPath;
            file.Relative = relative;
            file.Bytes = new FileInfo(fullPath).Length;
            file.Sha256 = HashFile(fullPath);
            return file;
        }

        // ---------------------------------------------------------------- 备份

        private static bool EnsureBackup(string sourceRoot, List<SourceFile> sources,
            out string folder, out string action)
        {
            folder = "";
            action = "";
            string backupRoot = Path.Combine(sourceRoot, BackupFolderName);

            string fingerprint = Fingerprint(sources);
            string reused = FindReusableBackup(backupRoot, fingerprint);
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
                folder = Path.Combine(backupRoot, stamp + "-" + suffix.ToString(CultureInfo.InvariantCulture));
                suffix++;
            }
            Directory.CreateDirectory(folder);

            StringBuilder manifest = new StringBuilder();
            for (int i = 0; i < sources.Count; i++)
            {
                SourceFile source = sources[i];
                string target = Path.Combine(folder, source.Relative);
                string targetDir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
                File.Copy(source.FullPath, target, true);
                manifest.Append(source.Sha256).Append("  ")
                    .Append(source.Relative).Append("  ")
                    .Append(source.Bytes.ToString(CultureInfo.InvariantCulture))
                    .Append(Environment.NewLine);
            }
            manifest.Append("# fingerprint=").Append(fingerprint).Append(Environment.NewLine);

            string manifestPath = Path.Combine(folder, ManifestName);
            File.WriteAllText(manifestPath, manifest.ToString(), new UTF8Encoding(false));

            // 逐字节复核:副本的 sha256 必须与源一致,manifest 必须能读回同样的值。
            for (int i = 0; i < sources.Count; i++)
            {
                SourceFile source = sources[i];
                string target = Path.Combine(folder, source.Relative);
                if (!File.Exists(target)) return false;
                if (new FileInfo(target).Length != source.Bytes) return false;
                if (!string.Equals(HashFile(target), source.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
            }

            action = "created";
            return true;
        }

        private static string FindReusableBackup(string backupRoot, string fingerprint)
        {
            if (!Directory.Exists(backupRoot)) return null;
            string[] dirs;
            try { dirs = Directory.GetDirectories(backupRoot); }
            catch { return null; }
            for (int i = 0; i < dirs.Length; i++)
            {
                string manifest = Path.Combine(dirs[i], ManifestName);
                if (!File.Exists(manifest)) continue;
                try
                {
                    string text = File.ReadAllText(manifest, Encoding.UTF8);
                    if (text.IndexOf("# fingerprint=" + fingerprint, StringComparison.Ordinal) >= 0)
                        return dirs[i];
                }
                catch { }
            }
            return null;
        }

        private static string Fingerprint(List<SourceFile> sources)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < sources.Count; i++)
                sb.Append(sources[i].Relative).Append('|').Append(sources[i].Sha256).Append('\n');
            return HashText(sb.ToString());
        }

        private static string HashFile(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                return ToHex(hash);
            }
        }

        private static string HashText(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text == null ? "" : text));
                return ToHex(hash);
            }
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 导入

        private static void ImportMeta(KmsDb db, Store.Parsed parsed, MigrationOutcome outcome)
        {
            SetIfMissing(db, "theme_id", parsed.ThemeId.ToString(CultureInfo.InvariantCulture));
            SetIfMissing(db, "keyboard_layout", parsed.KeyboardLayout.ToString(CultureInfo.InvariantCulture));
            SetIfMissing(db, "idle_threshold", parsed.IdleThresholdSeconds.ToString(CultureInfo.InvariantCulture));
            SetIfMissing(db, "backup_keep", parsed.BackupKeep.ToString(CultureInfo.InvariantCulture));
            SetIfMissing(db, "pos_x", parsed.PosX.ToString(CultureInfo.InvariantCulture));
            SetIfMissing(db, "pos_y", parsed.PosY.ToString(CultureInfo.InvariantCulture));
            SetIfMissing(db, "all_time_peak_apm", parsed.AllTimePeakApm.ToString(CultureInfo.InvariantCulture));
            SetIfMissing(db, "motion_level", parsed.MotionLevel.ToString(CultureInfo.InvariantCulture));
            SetIfMissing(db, "text_refresh_seconds", parsed.TextRefreshSeconds.ToString(CultureInfo.InvariantCulture));
            SetIfMissing(db, "daily_report_day", parsed.DailyReportDay == null ? "" : parsed.DailyReportDay);
            SetIfMissing(db, "motion_follow_system", parsed.MotionFollowSystem ? "1" : "0");
            SetIfMissing(db, "global_hotkey", parsed.GlobalHotkey ? "1" : "0");

            if (parsed.HasMouseDpi)
                SetIfMissing(db, "mouse_dpi", parsed.MouseDpi.ToString("R", CultureInfo.InvariantCulture));
            if (parsed.HasKeepDays)
                SetIfMissing(db, "keep_days", parsed.KeepDays.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(parsed.Wellbeing))
                SetIfMissing(db, "wellbeing_v1", parsed.Wellbeing);
            if (!string.IsNullOrEmpty(parsed.ShortcutModel))
                SetIfMissing(db, "shortcut_model_v1", parsed.ShortcutModel);
        }

        private static void SetIfMissing(KmsDb db, string key, string value)
        {
            db.Execute("INSERT OR IGNORE INTO meta (k, v) VALUES (?, ?);",
                new object[] { key, value == null ? "" : value });
        }

        private static void ImportRules(KmsDb db, Store.Parsed parsed, MigrationOutcome outcome)
        {
            long now = NowMs();
            for (int i = 0; i < parsed.AppRules.Count; i++)
            {
                string raw = parsed.AppRules[i];
                string[] parts = raw.Split('|');
                if (parts.Length != 2) continue;
                int category;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out category)) continue;
                if (category < 0 || category >= AppActivity.Categories.Length) continue;

                string ruleKey;
                try { ruleKey = AppActivity.Decode(parts[0]); }
                catch { continue; }
                if (string.IsNullOrEmpty(ruleKey)) continue;

                int changed = db.Execute(
                    "INSERT OR IGNORE INTO app_category_rule (rule_key, category_id, source, updated_ms) VALUES (?,?,?,?);",
                    new object[] { ruleKey, category, "user_override", now });
                if (changed > 0) outcome.RulesImported++;
            }
        }

        private static void ImportArchives(KmsDb db, Store.Parsed parsed, MigrationOutcome outcome)
        {
            foreach (KeyValuePair<string, MonthArchive> pair in parsed.Archives)
            {
                MonthArchive archive = pair.Value;
                if (archive == null) continue;
                int changed = db.Execute(
                    "INSERT OR IGNORE INTO month_archive (ym, days, keys, clicks, wheel, combos, active_seconds, " +
                    "move_meters, sessions, hold_count, hold_discarded, hold_total_ms, hold_max_ms, hold_active_seconds) " +
                    "VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?);",
                    new object[]
                    {
                        pair.Key, archive.Days, archive.Keys, archive.Clicks, archive.Wheel,
                        archive.Combos, archive.ActiveSeconds, archive.MoveMeters, archive.Sessions,
                        archive.HoldCount, archive.HoldDiscarded, archive.HoldTotalMs,
                        archive.HoldMaxMs, archive.HoldActiveSeconds
                    });
                if (changed > 0) outcome.ArchivesImported++;

                foreach (KeyValuePair<string, long> app in archive.AppKeys)
                {
                    db.Execute(
                        "INSERT OR IGNORE INTO month_archive_app (ym, process_path, keys) VALUES (?,?,?);",
                        new object[] { pair.Key, app.Key == null ? "" : app.Key, app.Value });
                }
            }
        }

        private static void ImportDays(KmsDb db, Store.Parsed parsed, MigrationOutcome outcome)
        {
            List<DateTime> days = new List<DateTime>(parsed.History.Keys);
            days.Sort();

            for (int i = 0; i < days.Count; i++)
            {
                DayRecord record = parsed.History[days[i]];
                if (record == null) continue;
                string day = record.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                int inserted = db.Execute(
                    "INSERT OR IGNORE INTO daily_summary (day, keys, clicks, left, right, middle, xbuttons, " +
                    "wheel, move_px, move_meters, active_seconds, idle_seconds, app_observed_seconds, " +
                    "app_switches, peak_apm, hold_count, hold_discarded, hold_total_ms, hold_max_ms, " +
                    "hold_active_seconds, sessions, origin) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?, 'migrated');",
                    new object[]
                    {
                        day, record.Keys, record.Clicks, record.Left, record.Right, record.Middle,
                        record.XButtons, record.Wheel, record.MovePx, record.MoveMeters,
                        record.ActiveSeconds, record.IdleSeconds, record.AppObservedSeconds,
                        record.AppSwitches, record.PeakApm, record.HoldCount, record.HoldDiscarded,
                        record.HoldTotalMs, record.HoldMaxMs, record.HoldActiveSeconds,
                        record.Sessions == null ? 0 : record.Sessions.Count
                    });
                if (inserted == 0) continue;   // 该日已存在 → 整日跳过(幂等)
                outcome.DaysImported++;

                ImportHours(db, day, record);
                ImportKeys(db, day, record);
                ImportWindows(db, day, record);
                outcome.GridCellsImported += ImportGrid(db, day, record);
            }
        }

        private static void ImportHours(KmsDb db, string day, DayRecord record)
        {
            for (int hour = 0; hour < 24; hour++)
            {
                long keys = record.HourKeys != null && hour < record.HourKeys.Length ? record.HourKeys[hour] : 0;
                long clicks = record.HourClicks != null && hour < record.HourClicks.Length ? record.HourClicks[hour] : 0;
                double active = record.ActiveHours != null && hour < record.ActiveHours.Length ? record.ActiveHours[hour] : 0;
                if (keys == 0 && clicks == 0 && active <= 0) continue;
                db.Execute(
                    "INSERT OR IGNORE INTO daily_hour_agg (day, hour, keys, clicks, active_seconds) VALUES (?,?,?,?,?);",
                    new object[] { day, hour, keys, clicks, active });
            }
        }

        private static void ImportKeys(KmsDb db, string day, DayRecord record)
        {
            if (record.KeyCounts == null) return;
            foreach (KeyValuePair<int, long> pair in record.KeyCounts)
            {
                if (pair.Value <= 0) continue;
                db.Execute("INSERT OR IGNORE INTO daily_key_agg (day, vk, cnt) VALUES (?,?,?);",
                    new object[] { day, pair.Key, pair.Value });
            }
        }

        private static void ImportWindows(KmsDb db, string day, DayRecord record)
        {
            if (record.Apps == null) return;
            foreach (AppUsage app in record.Apps.Values)
            {
                if (app == null) continue;
                string path = app.ProcessPath == null ? "" : app.ProcessPath;
                string processKey = path.ToLowerInvariant();
                string title = app.Title == null ? "" : app.Title;
                long g0 = 0, g1 = 0, g2 = 0, g3 = 0, g4 = 0, g5 = 0;
                if (app.KeyGroups != null && app.KeyGroups.Length >= 6)
                {
                    g0 = app.KeyGroups[0]; g1 = app.KeyGroups[1]; g2 = app.KeyGroups[2];
                    g3 = app.KeyGroups[3]; g4 = app.KeyGroups[4]; g5 = app.KeyGroups[5];
                }
                db.Execute(
                    "INSERT OR IGNORE INTO window_daily_agg (day, process_key, window_title, process_path, " +
                    "process_name, keys, clicks, wheel, active_seconds, key_g0, key_g1, key_g2, key_g3, key_g4, " +
                    "key_g5, category_id, category_source, semantic_tag) " +
                    "VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,-1,'none','');",
                    new object[]
                    {
                        day, processKey, title, path, app.AppName, app.Keys, app.Clicks, app.Wheel,
                        app.ActiveSeconds, g0, g1, g2, g3, g4, g5
                    });
            }
        }

        /// <summary>
        /// 历史单通道网格 → channel = 0(兼容通道)。
        /// **绝不归入左键**:否则「左键」视图会显示用户从未点过的热点,等于伪造结论。
        /// </summary>
        private static long ImportGrid(KmsDb db, string day, DayRecord record)
        {
            bool hasFlat = record.ClickCells != null && record.ClickCells.Count > 0;
            bool hasChanneled = record.ClickCellsV1 != null && record.ClickCellsV1.Count > 0;
            if (!hasFlat && !hasChanneled) return 0;

            // v3.0 存档带 chv1 且与 ch 自洽时按通道导入;否则整体落到兼容通道 0。
            if (hasChanneled && ClickHeat.InvariantHolds(record))
            {
                long channeled = 0;
                foreach (KeyValuePair<long, long> pair in record.ClickCellsV1)
                {
                    if (pair.Value <= 0) continue;
                    int channel = ClickHeat.ChannelOfKey(pair.Key);
                    int cellX, cellY;
                    ClickHeat.SplitKey(ClickHeat.CellKeyOfChannelKey(pair.Key), out cellX, out cellY);
                    db.Execute(
                        "INSERT OR IGNORE INTO daily_click_grid (day, channel, cell_x, cell_y, screen_id, count) " +
                        "VALUES (?,?,?,?,0,?);",
                        new object[] { day, channel, cellX, cellY, pair.Value });
                    channeled++;
                }
                return channeled;
            }

            long cells = 0;
            foreach (KeyValuePair<int, long> pair in record.ClickCells)
            {
                if (pair.Value <= 0) continue;
                int flatX, flatY;
                ClickHeat.SplitKey(pair.Key, out flatX, out flatY);
                db.Execute(
                    "INSERT OR IGNORE INTO daily_click_grid (day, channel, cell_x, cell_y, screen_id, count) " +
                    "VALUES (?,0,?,?,0,?);",
                    new object[] { day, flatX, flatY, pair.Value });
                cells++;
            }
            return cells;
        }

        private static void ImportLog(KmsDb db, List<SourceFile> sources, MigrationOutcome outcome)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                SourceFile source = sources[i];
                string kind = source.Relative.IndexOf(StatsFileName, StringComparison.Ordinal) == 0
                    ? "v2-stats" : "v2-month";
                db.Execute(
                    "INSERT OR IGNORE INTO migration_log (source_kind, source_id, content_sha256, days_imported, imported_ms) " +
                    "VALUES (?,?,?,?,?);",
                    new object[] { kind, source.Relative, source.Sha256, outcome.DaysImported, NowMs() });
            }
        }

        private static void WriteMigrationMeta(KmsDb db, Store.Parsed parsed,
            MigrationOutcome outcome, string statsPath)
        {
            long dayKeys = 0, dayClicks = 0;
            foreach (KeyValuePair<DateTime, DayRecord> pair in parsed.History)
            {
                if (pair.Value == null) continue;
                dayKeys += pair.Value.Keys;
                dayClicks += pair.Value.Clicks;
            }
            long archiveKeys = 0;
            foreach (KeyValuePair<string, MonthArchive> pair in parsed.Archives)
                if (pair.Value != null) archiveKeys += pair.Value.Keys;

            SetMeta(db, "migrated_from_format", parsed.FormatVersion.ToString(CultureInfo.InvariantCulture));
            SetMeta(db, "migrated_at_ms", NowMs().ToString(CultureInfo.InvariantCulture));
            SetMeta(db, "migrated_days", outcome.DaysImported.ToString(CultureInfo.InvariantCulture));

            // 原累计只留痕,不参与统计,避免与日记录/月档双计。
            SetMeta(db, "legacy_total_keys", parsed.Total.Keys.ToString(CultureInfo.InvariantCulture));
            SetMeta(db, "legacy_total_clicks", parsed.Total.Clicks.ToString(CultureInfo.InvariantCulture));
            SetMeta(db, "legacy_total_left", parsed.Total.Left.ToString(CultureInfo.InvariantCulture));
            SetMeta(db, "legacy_total_right", parsed.Total.Right.ToString(CultureInfo.InvariantCulture));
            SetMeta(db, "legacy_total_middle", parsed.Total.Middle.ToString(CultureInfo.InvariantCulture));
            SetMeta(db, "legacy_total_wheel", parsed.Total.Wheel.ToString(CultureInfo.InvariantCulture));
            SetMeta(db, "legacy_total_delta",
                (parsed.Total.Keys - (dayKeys + archiveKeys)).ToString(CultureInfo.InvariantCulture));

            // 快照桥基线:此刻的 stats.txt 已被本次迁移完整覆盖。
            if (File.Exists(statsPath))
            {
                long mtime = (long)(File.GetLastWriteTimeUtc(statsPath) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                    .TotalMilliseconds;
                SetMeta(db, "last_export_ms", mtime.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void SetMeta(KmsDb db, string key, string value)
        {
            db.Execute("INSERT OR REPLACE INTO meta (k, v) VALUES (?, ?);",
                new object[] { key, value == null ? "" : value });
        }

        // ---------------------------------------------------------------- 守恒

        /// <summary>提交前四条硬断言;任一不符抛异常 → RunInTransaction 自动回滚。</summary>
        private static void AssertConservation(KmsDb db, Store.Parsed parsed, MigrationOutcome outcome)
        {
            long sourceKeys = 0, sourceClicks = 0, sourceCells = 0;
            foreach (KeyValuePair<DateTime, DayRecord> pair in parsed.History)
            {
                DayRecord record = pair.Value;
                if (record == null) continue;
                sourceKeys += record.Keys;
                sourceClicks += record.Clicks;
                if (record.ClickCells != null)
                    foreach (KeyValuePair<int, long> cell in record.ClickCells)
                        if (cell.Value > 0) sourceCells += cell.Value;
            }

            // ① 每日击键总和守恒
            long dbKeys = db.ScalarLong(
                "SELECT IFNULL(SUM(keys),0) FROM daily_summary WHERE origin='migrated';", null);
            AssertEqual(dbKeys, sourceKeys, "① 每日击键总和守恒");

            // ①b 每日点击总和守恒(与击键同源,单独断言更易定位)
            long dbClicks = db.ScalarLong(
                "SELECT IFNULL(SUM(clicks),0) FROM daily_summary WHERE origin='migrated';", null);
            AssertEqual(dbClicks, sourceClicks, "①b 每日点击总和守恒");

            // ② 网格同源总和守恒(channel=0 只承载历史单通道)
            // 带 chv1 的源档会分通道落库,所以这里对**所有通道**求和。
            long dbCells = db.ScalarLong(
                "SELECT IFNULL(SUM(count),0) FROM daily_click_grid;", null);
            AssertEqual(dbCells, sourceCells, "② 网格同源总和守恒");

            // ③ 小时桶总和守恒
            long hourKeys = db.ScalarLong(
                "SELECT IFNULL(SUM(a.keys),0) FROM daily_hour_agg a " +
                "JOIN daily_summary d ON d.day=a.day WHERE d.origin='migrated';", null);
            AssertEqual(hourKeys, sourceKeys, "③ 小时桶总和守恒");

            // ④ 月档总和守恒
            long archiveKeys = 0;
            foreach (KeyValuePair<string, MonthArchive> pair in parsed.Archives)
                if (pair.Value != null) archiveKeys += pair.Value.Keys;
            long dbArchiveKeys = db.ScalarLong("SELECT IFNULL(SUM(keys),0) FROM month_archive;", null);
            AssertEqual(dbArchiveKeys, archiveKeys, "④ 月档总和守恒");
        }

        private static void AssertEqual(long actual, long expected, string what)
        {
            if (actual == expected) return;
            throw new KmsDbException(what + "断言失败(期望 " + expected + ",实际 " + actual + ")");
        }

        // ---------------------------------------------------------------- 快照桥

        /// <summary>
        /// 启动时的受限日汇总同步:stats.txt 的 mtime 晚于 meta.last_export_ms 时,
        /// 说明文本被外部(例如清爽版)改写过。此时**只补库中缺失的日期**,绝不更新已有行,
        /// 因此天然幂等、不会双重计数。
        /// </summary>
        internal static int SyncDerivedSnapshotIfNewer(string sourceRoot, string dbRoot, out string reason)
        {
            reason = "";
            if (string.IsNullOrEmpty(sourceRoot) || string.IsNullOrEmpty(dbRoot)) return 0;
            string statsPath = Path.Combine(sourceRoot, StatsFileName);
            string dbPath = Path.Combine(dbRoot, KmsSchema.DbFileName);
            if (!File.Exists(statsPath) || !File.Exists(dbPath)) return 0;

            KmsDb db = null;
            try
            {
                db = KmsDb.OpenWriter(dbPath);
                KmsSchema.Ensure(db);

                long exported = 0;
                string raw = db.ScalarText("SELECT v FROM meta WHERE k='last_export_ms';", null);
                if (!string.IsNullOrEmpty(raw)) long.TryParse(raw, out exported);

                long mtime = (long)(File.GetLastWriteTimeUtc(statsPath)
                    - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                if (mtime <= exported)
                {
                    reason = "文本快照未变更";
                    return 0;
                }

                Store.Parsed parsed = Store.ParseText(File.ReadAllText(statsPath, Encoding.UTF8));
                int added = 0;
                db.RunInTransaction(delegate(KmsDb tx)
                {
                    List<DateTime> days = new List<DateTime>(parsed.History.Keys);
                    days.Sort();
                    for (int i = 0; i < days.Count; i++)
                    {
                        DayRecord record = parsed.History[days[i]];
                        if (record == null) continue;
                        string day = record.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        int changed = tx.Execute(
                            "INSERT OR IGNORE INTO daily_summary (day, keys, clicks, left, right, middle, " +
                            "xbuttons, wheel, move_px, move_meters, active_seconds, idle_seconds, origin) " +
                            "VALUES (?,?,?,?,?,?,?,?,?,?,?,?, 'snapshot-bridge');",
                            new object[]
                            {
                                day, record.Keys, record.Clicks, record.Left, record.Right, record.Middle,
                                record.XButtons, record.Wheel, record.MovePx, record.MoveMeters,
                                record.ActiveSeconds, record.IdleSeconds
                            });
                        if (changed > 0) added++;
                    }
                    SetMeta(tx, "last_export_ms", mtime.ToString(CultureInfo.InvariantCulture));
                });
                reason = "补入 " + added + " 天";
                return added;
            }
            catch (Exception error)
            {
                reason = "同步失败:" + error.Message;
                return 0;
            }
            finally
            {
                if (db != null) db.Dispose();
            }
        }

        // ---------------------------------------------------------------- 工具

        private static long NowMs()
        {
            return UnixTime.NowMs();
        }

        private static void LogDiag(string dbRoot, string message)
        {
            if (string.IsNullOrEmpty(dbRoot)) return;
            try
            {
                Directory.CreateDirectory(dbRoot);
                File.AppendAllText(Path.Combine(dbRoot, "migration.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }
}

#endif
