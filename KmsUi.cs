// ============================================================================
//  KeyMouseStats v3.0 · Milestone 3 / Step 3c  KmsUi.cs
//
//  .kms 增量包的界面层:导出 / 合并。只做编排与提示,业务在 KmsPackage 与 KmsDb。
//  清爽版没有事件库,给出明确提示而不是静默失败。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal static class KmsUi
    {
        /// <summary>导出近 30 天的 DAY 汇总 + 事件库里的 SES 会话流。</summary>
        internal static void Export(IWin32Window owner)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = ".kms 增量包 (*.kms)|*.kms|所有文件 (*.*)|*.*";
                dialog.FileName = "keymousestats-" + DateTime.Now.ToString("yyyyMMdd") + ".kms";
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;

                string masked = PrivacyMask.Enabled ? "(窗口标题已脱敏)" : "(窗口标题原样)";
                try
                {
                    KmsPackageData data = BuildPackage();
                    File.WriteAllText(dialog.FileName, KmsPackage.Export(data), new UTF8Encoding(false));
                    MessageBox.Show(owner,
                        "已导出 " + data.Days.Count + " 天汇总、" + data.Sessions.Count + " 段会话流 " + masked,
                        "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception error)
                {
                    MessageBox.Show(owner, "导出失败:" + error.Message, "导出失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>读取 .kms 文件、校验、幂等合并;返回给用户新增/跳过条数。</summary>
        internal static void Merge(IWin32Window owner)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = ".kms 增量包 (*.kms)|*.kms|所有文件 (*.*)|*.*";
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;

                string text;
                try { text = File.ReadAllText(dialog.FileName, Encoding.UTF8); }
                catch (Exception error)
                {
                    MessageBox.Show(owner, "读取失败:" + error.Message, "合并失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                KmsPackageData package;
                try { package = KmsPackage.Import(text); }
                catch (Exception error)
                {
                    MessageBox.Show(owner, "数据包被拒绝:" + error.Message, "校验失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string packageId = KmsPackage.Fingerprint(text);
                try
                {
#if !CLEAN_EDITION
                    KmsMergeResult result = KmsDb.MergeKmsPackage(FlowBoard.DatabasePath, packageId, package);
                    SyncHistory(package);
                    MessageBox.Show(owner,
                        "新增导入 " + (result.DaysApplied + result.SessionsApplied) + " 条、跳过重复 "
                        + (result.DaysSkipped + result.SessionsSkipped) + " 条。\n(日汇总 " + result.DaysApplied
                        + " / 会话流 " + result.SessionsApplied + ")",
                        "合并完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
#else
                    MessageBox.Show(owner,
                        "清爽版不含内置事件数据库,无法合并 .kms 数据包。使用标准版可启用跨设备幂等合并。",
                        "清爽版不支持", MessageBoxButtons.OK, MessageBoxIcon.Information);
#endif
                }
                catch (Exception error)
                {
                    MessageBox.Show(owner, "合并失败:" + error.Message, "合并失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private static KmsPackageData BuildPackage()
        {
            KmsPackageData data = new KmsPackageData();
            data.DeviceId = Environment.MachineName;
            DateTime from = DateTime.Today.AddDays(-29);
            DateTime to = DateTime.Today;
            data.DayRange = from.ToString("yyyy-MM-dd") + ".." + to.ToString("yyyy-MM-dd");

            List<DateTime> days = new List<DateTime>(Store.History.Keys);
            days.Sort();
            for (int i = 0; i < days.Count; i++)
            {
                DateTime day = days[i];
                if (day.Date < from || day.Date > to) continue;
                DayRecord record = Store.History[day];
                if (record == null || record.IsEmpty) continue;
                KmsDayRecord row = new KmsDayRecord();
                row.Day = day.ToString("yyyy-MM-dd");
                row.Keys = record.Keys;
                row.Clicks = record.Clicks;
                row.Wheel = record.Wheel;
                row.ActiveSeconds = (long)record.ActiveSeconds;
                data.Days.Add(row);
#if !CLEAN_EDITION
                try
                {
                    if (File.Exists(FlowBoard.DatabasePath))
                    {
                        using (KmsDb db = KmsDb.OpenReader(FlowBoard.DatabasePath))
                        {
                            List<FlowSessionRow> sessions = db.ReadSessionsForDay(row.Day);
                            for (int s = 0; s < sessions.Count; s++)
                            {
                                FlowSessionRow source = sessions[s];
                                KmsSessionRecord target = new KmsSessionRecord();
                                target.Day = row.Day;
                                target.StartMs = source.StartMs;
                                target.EndMs = source.EndMs;
                                target.KeyCount = source.KeyCount;
                                target.CategoryId = source.CategoryId;
                                target.ProcessPath = PrivacyMask.MaskPath(source.ProcessPath);
                                target.ProcessName = source.ProcessName ?? "";
                                target.WindowTitle = PrivacyMask.MaskTitle(source.WindowTitle, source.CategoryId);
                                data.Sessions.Add(target);
                            }
                        }
                    }
                }
                catch { }
#endif
            }
            return data;
        }

        /// <summary>把包里新增的日并回内存历史,随后重算总数。</summary>
        private static void SyncHistory(KmsPackageData package)
        {
            for (int i = 0; i < package.Days.Count; i++)
            {
                KmsDayRecord row = package.Days[i];
                DateTime day;
                if (!DateTime.TryParse(row.Day, out day)) continue;
                if (Store.History.ContainsKey(day.Date)) continue;
                DayRecord record = new DayRecord();
                record.Keys = row.Keys;
                record.Clicks = row.Clicks;
                record.Wheel = row.Wheel;
                record.ActiveSeconds = row.ActiveSeconds;
                Store.History[day.Date] = record;
            }
            try { Store.Save(); } catch { }
        }
    }
}
