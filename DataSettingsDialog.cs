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
        private ImportPreview _preview;

        public DataSettingsDialog()
        {
            Text = "数据管理"; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9f); ClientSize = new Size(620, 600);
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

            Controls.AddRange(new Control[]{ backupTitle, keepLabel, _keep, backupNow, openFolder, _backupStatus,
                exportTitle, export, importTitle, choose, _importFile, _importPreview, _keepLarger, _add, _apply,
                validateTitle, validate, _validationStatus, _issues });

            _keep.ValueChanged += delegate { Store.BackupKeep = (int)_keep.Value; Store.Save(); RefreshBackupStatus(); };
            backupNow.Click += delegate { Backup(); };
            openFolder.Click += delegate { try { Process.Start("explorer.exe", Store.DataDirectory); } catch { } };
            export.Click += delegate { Export(); };
            choose.Click += delegate { ChooseImport(); };
            _apply.Click += delegate { ApplyImport(); };
            validate.Click += delegate { RunValidation(); };

            RefreshBackupStatus();
            UpdateImportSummary();
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
