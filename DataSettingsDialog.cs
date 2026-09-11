// ============================================================================
//  键鼠统计 - 数据管理对话框  DataSettingsDialog.cs
//  备份轮转、导出、导入 / 多机合并、逐日数据校验的统一入口。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal sealed class DataSettingsDialog : ThemedDialog
    {
        private readonly NumericUpDown _keep = new NumericUpDown { Minimum = 1, Maximum = 60, Value = 7 };
        private readonly Label _backupStatus = new Label();
        private readonly Label _importFile = new Label();
        private readonly Label _importPreview = new Label();
        private readonly RadioButton _keepLarger = new RadioButton { Text = "保留击键更多的一份（不会重复计数，同时使用时可能低估）" };
        private readonly RadioButton _add = new RadioButton { Text = "两份相加（分开使用时正确，同时使用时可能重复计数）" };
        private readonly Button _apply = new Button { Text = "应用到本机", Enabled = false };
        private readonly ListView _issues = new ListView { View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.FixedSingle };
        private readonly Label _validationStatus = new Label();
        private readonly NumericUpDown _retentionDays = new NumericUpDown { Minimum = 30, Maximum = 3650, Increment = 30 };
        private readonly CheckBox _retentionForever = new CheckBox { Text = "永久保留" };
        /// <summary>1.8.0:已归档月份一览。超期日折进月度归档后,按住时长就在这里看。</summary>
        private readonly ListView _archives = new ListView { View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.FixedSingle };
        private ImportPreview _preview;

        public DataSettingsDialog()
        {
            Text = "数据管理"; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9f); ClientSize = new Size(620, 834);
            _keep.Value = Math.Max(1, Math.Min(60, Store.BackupKeep));

            Label backupTitle = Header("备份");
            Label keepLabel = new Label { Text = "保留份数", TextAlign = ContentAlignment.MiddleLeft };
            Button backupNow = new Button { Text = "立即备份" };
            Button openFolder = new Button { Text = "打开数据目录" };
            backupTitle.SetBounds(20, 16, 200, 22);
            keepLabel.SetBounds(20, 46, 70, 26); _keep.SetBounds(94, 46, 64, 26);
            backupNow.SetBounds(174, 45, 96, 28); openFolder.SetBounds(280, 45, 116, 28);
            _backupStatus.SetBounds(20, 80, 576, 20);

            Label exportTitle = Header("导出");
            Button export = new Button { Text = "导出数据…" };
            exportTitle.SetBounds(20, 112, 200, 22);
            export.SetBounds(20, 140, 116, 30);

            Label importTitle = Header("导入 / 多机合并");
            Button choose = new Button { Text = "选择文件…" };
            importTitle.SetBounds(20, 184, 300, 22);
            choose.SetBounds(20, 212, 116, 30);
            _importFile.SetBounds(148, 218, 448, 20);
            _importPreview.SetBounds(20, 248, 576, 40);
            _keepLarger.SetBounds(20, 292, 576, 22); _add.SetBounds(20, 316, 576, 22);
            _apply.SetBounds(20, 346, 116, 30);
            _keepLarger.Checked = true;

            Label validateTitle = Header("数据校验");
            Button validate = new Button { Text = "检查数据" };
            validateTitle.SetBounds(20, 390, 200, 22);
            validate.SetBounds(20, 418, 116, 30);
            _validationStatus.SetBounds(148, 424, 448, 20);
            _issues.Columns.Add("级别", 60); _issues.Columns.Add("日期", 90); _issues.Columns.Add("说明", 420);
            _issues.SetBounds(20, 456, 580, 128);

            Label retentionTitle = Header("数据保留");
            Label retentionLabel = new Label { Text = "保留", TextAlign = ContentAlignment.MiddleLeft };
            Label retentionTail = new Label { Text = "天（超期先按月归档，不会直接删除）", TextAlign = ContentAlignment.MiddleLeft };
            retentionTitle.SetBounds(20, 594, 200, 22);
            retentionLabel.SetBounds(20, 622, 34, 26);
            _retentionDays.SetBounds(56, 622, 74, 26);
            retentionTail.SetBounds(136, 622, 290, 26);
            _retentionForever.SetBounds(432, 622, 130, 26);
            _retentionDays.Value = Store.KeepDays <= 0 ? 365 : Math.Max(_retentionDays.Minimum, Math.Min(_retentionDays.Maximum, Store.KeepDays));
            _retentionForever.Checked = Store.KeepDays <= 0;
            _retentionDays.Enabled = !_retentionForever.Checked;
            _retentionForever.CheckedChanged += delegate
            {
                _retentionDays.Enabled = !_retentionForever.Checked;
                Store.KeepDays = _retentionForever.Checked ? 0 : (int)_retentionDays.Value;
                Store.Save();
            };
            _retentionDays.ValueChanged += delegate
            {
                if (_retentionForever.Checked) return;
                Store.KeepDays = (int)_retentionDays.Value;
                Store.Save();
            };

            Controls.AddRange(new Control[]{ backupTitle, keepLabel, _keep, backupNow, openFolder, _backupStatus,
                exportTitle, export, importTitle, choose, _importFile, _importPreview, _keepLarger, _add, _apply,
                validateTitle, validate, _validationStatus, _issues,
                retentionTitle, retentionLabel, _retentionDays, retentionTail, _retentionForever });

            // 1.8.0:已归档月份。保留期之外的日会折进这里,按住时长不再凭空消失。
            Label archiveTitle = Header("已归档月份");
            _archives.Columns.Add("月份", 96);
            _archives.Columns.Add("天数", 56);
            _archives.Columns.Add("击键", 96);
            _archives.Columns.Add("累计按住", 108);
            _archives.Columns.Add("有键按下", 108);
            _archives.Columns.Add("丢弃", 70);
            archiveTitle.SetBounds(20, 664, 300, 22);
            _archives.SetBounds(20, 692, 580, 122);
            Controls.Add(archiveTitle);
            Controls.Add(_archives);

            _keep.ValueChanged += delegate { Store.BackupKeep = (int)_keep.Value; Store.Save(); RefreshBackupStatus(); };
            backupNow.Click += delegate { Backup(); };
            openFolder.Click += delegate { try { Process.Start("explorer.exe", Store.DataDirectory); } catch { } };
            export.Click += delegate { Export(); };
            choose.Click += delegate { ChooseImport(); };
            _apply.Click += delegate { ApplyImport(); };
            validate.Click += delegate { RunValidation(); };

            RefreshBackupStatus();
            RefreshArchives();
            UpdateImportSummary();
        }

        /// <summary>1.8.0:列出已归档月份(新的在前),含按住时长与墙钟占用。</summary>
        private void RefreshArchives()
        {
            _archives.Items.Clear();
            List<MonthArchive> list = new List<MonthArchive>(Store.Archives.Values);
            list.Sort(delegate(MonthArchive a, MonthArchive b) { return string.CompareOrdinal(b.Key, a.Key); });
            foreach (MonthArchive archive in list)
            {
                ListViewItem item = new ListViewItem(archive.Label);
                item.SubItems.Add(archive.Days.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(archive.Keys.ToString("N0", CultureInfo.InvariantCulture));
                item.SubItems.Add(archive.HoldCount > 0 || archive.HoldTotalMs > 0 ? HoldReportData.Duration(archive.HoldTotalMs) : "--");
                item.SubItems.Add(archive.HoldActiveSeconds > 0 ? HoldReportData.Duration(archive.HoldActiveSeconds * 1000) : "--");
                item.SubItems.Add(archive.HoldDiscarded.ToString("N0", CultureInfo.InvariantCulture));
                _archives.Items.Add(item);
            }
            if (list.Count == 0)
            {
                ListViewItem empty = new ListViewItem("暂无归档");
                empty.SubItems.Add("—");
                empty.SubItems.Add("超出保留期的日会先折进月度归档,");
                empty.SubItems.Add("再从这里查看");
                empty.SubItems.Add("不会直接删除");
                empty.SubItems.Add("—");
                _archives.Items.Add(empty);
            }
        }

        private static Label Header(string text)
        {
            Label label = new Label { Text = text, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold) };
            return label;
        }

        private void RefreshBackupStatus()
        {
            List<string> backups = DataManagement.ListBackups();
            _backupStatus.Text = backups.Count == 0 ? "还没有备份。" :
                "共 " + backups.Count + " 份，最新：" + Path.GetFileName(backups[0]);
        }

        private void Backup()
        {
            try
            {
                string path = DataManagement.BackupNow();
                DataManagement.RotateBackups((int)_keep.Value);
                RefreshBackupStatus();
                ThemeMessage.Show(this, "已备份：\n" + path, "备份完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception error) { ThemeMessage.Show(this, error.Message, "备份失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void Export()
        {
            try
            {
                using (SaveFileDialog dialog = new SaveFileDialog
                {
                    Title = "导出统计数据",
                    Filter = "键鼠统计导出 (*.txt)|*.txt",
                    FileName = "键鼠统计_" + DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".txt",
                    DefaultExt = "txt", AddExtension = true
                })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    DataManagement.Export(dialog.FileName);
                    ThemeMessage.Show(this, "已导出：\n" + dialog.FileName, "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception error) { ThemeMessage.Show(this, error.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void ChooseImport()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "选择导出的数据文件", Filter = "键鼠统计导出 (*.txt)|*.txt|所有文件 (*.*)|*.*" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _preview = DataManagement.Inspect(dialog.FileName);
                UpdateImportSummary();
            }
        }

        private void UpdateImportSummary()
        {
            if (_preview == null)
            {
                _importFile.Text = "尚未选择文件";
                _importPreview.Text = "导出文件带校验和；校验不通过时不会写入任何数据。";
                _apply.Enabled = false;
                return;
            }
            _importFile.Text = _preview.FileName;
            if (!_preview.Valid)
            {
                _importPreview.Text = "无法导入：" + _preview.Error;
                _apply.Enabled = false;
                return;
            }
            _importPreview.Text = _preview.Days + " 天 · " + _preview.First.ToString("yyyy.MM.dd") + "—" + _preview.Last.ToString("yyyy.MM.dd")
                + " · 与现有记录冲突 " + _preview.Conflicts + " 天 · 导入击键 " + Analysis.FmtCount(_preview.ImportedKeys)
                + (Store.MouseDpi > 0 ? "" : "");
            _apply.Enabled = true;
        }

        private void ApplyImport()
        {
            if (_preview == null || !_preview.Valid) return;
            MergeStrategy strategy = _add.Checked ? MergeStrategy.Add : MergeStrategy.KeepLarger;
            try
            {
                ImportResult result = DataManagement.Apply(_preview, strategy, false);
                RefreshBackupStatus();
                ThemeMessage.Show(this, result.Summary() + "\n\n累计总数在合并后为近似值。", "导入完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _preview = null;
                UpdateImportSummary();
            }
            catch (Exception error) { ThemeMessage.Show(this, error.Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void RunValidation()
        {
            List<ValidationIssue> issues = DataManagement.Validate();
            _issues.BeginUpdate();
            _issues.Items.Clear();
            int errors = 0, warnings = 0;
            foreach (ValidationIssue issue in issues)
            {
                if (issue.Severity == ValidationIssue.Level.Error) errors++;
                else if (issue.Severity == ValidationIssue.Level.Warning) warnings++;
                ListViewItem row = new ListViewItem(new string[] { issue.LevelText, issue.Date.ToString("yyyy-MM-dd"), issue.Message });
                row.ToolTipText = issue.Message;
                _issues.Items.Add(row);
            }
            _issues.EndUpdate();
            _validationStatus.Text = issues.Count == 0 ? "没有发现问题。"
                : errors + " 个错误 · " + warnings + " 个警告 · 共 " + issues.Count + " 条";
        }
    }
}
