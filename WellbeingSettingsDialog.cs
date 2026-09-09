// ============================================================================
//  键鼠统计 - 目标 / 提醒设置  WellbeingSettingsDialog.cs
//  目标模式与数值、连续使用提醒与冷却、每日摘要时间。全部默认关闭。
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal sealed class WellbeingSettingsDialog : ThemedDialog
    {
        private readonly RadioButton _goalOff = new RadioButton { Text = "不设置目标" };
        private readonly RadioButton _goalKeys = new RadioButton { Text = "按击键" };
        private readonly RadioButton _goalActive = new RadioButton { Text = "按活跃时长" };
        private readonly NumericUpDown _goalKeysValue = new NumericUpDown { Minimum = 100, Maximum = 1000000, Increment = 500 };
        private readonly NumericUpDown _goalHours = new NumericUpDown { Minimum = 1, Maximum = 24, DecimalPlaces = 1, Increment = 0.5M };
        private readonly CheckBox _continuous = new CheckBox { Text = "连续使用提醒（默认关闭）" };
        private readonly NumericUpDown _continuousMinutes = new NumericUpDown { Minimum = 15, Maximum = 480, Increment = 15 };
        private readonly NumericUpDown _cooldown = new NumericUpDown { Minimum = 5, Maximum = 240, Increment = 5 };
        private readonly CheckBox _summary = new CheckBox { Text = "每日摘要（到点提示今日概况）" };
        private readonly NumericUpDown _summaryHour = new NumericUpDown { Minimum = 0, Maximum = 23 };

        public WellbeingSettingsDialog()
        {
            Text = "目标 / 提醒"; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9f); ClientSize = new Size(460, 430);

            Label goalTitle = Header("每日目标");
            goalTitle.SetBounds(20, 14, 300, 22);
            _goalOff.SetBounds(20, 42, 120, 22);
            _goalKeys.SetBounds(20, 68, 80, 22); _goalKeysValue.SetBounds(104, 66, 110, 26);
            _goalActive.SetBounds(20, 96, 100, 22); _goalHours.SetBounds(124, 94, 70, 26);
            Label hoursLabel = new Label { Text = "小时 / 天", TextAlign = ContentAlignment.MiddleLeft };
            hoursLabel.SetBounds(200, 94, 90, 26);
            Label goalNote = new Label { Text = "达成当天只提示一次；今日未结束，进度是进行中的。", TextAlign = ContentAlignment.MiddleLeft };
            goalNote.SetBounds(20, 126, 420, 20);

            Label reminderTitle = Header("连续使用提醒");
            reminderTitle.SetBounds(20, 158, 300, 22);
            _continuous.SetBounds(20, 186, 260, 22);
            Label thresholdLabel = new Label { Text = "超过", TextAlign = ContentAlignment.MiddleLeft };
            Label thresholdTail = new Label { Text = "分钟提醒一次", TextAlign = ContentAlignment.MiddleLeft };
            thresholdLabel.SetBounds(38, 214, 42, 26);
            _continuousMinutes.SetBounds(82, 214, 70, 26);
            thresholdTail.SetBounds(158, 214, 130, 26);
            Label cooldownLabel = new Label { Text = "冷却", TextAlign = ContentAlignment.MiddleLeft };
            Label cooldownTail = new Label { Text = "分钟（同一提醒不重复）", TextAlign = ContentAlignment.MiddleLeft };
            cooldownLabel.SetBounds(38, 244, 42, 26);
            _cooldown.SetBounds(82, 244, 70, 26);
            cooldownTail.SetBounds(158, 244, 220, 26);
            Label dndNote = new Label { Text = "前台全屏（游戏 / 全屏视频）时自动抑制提醒。", TextAlign = ContentAlignment.MiddleLeft };
            dndNote.SetBounds(20, 274, 420, 20);

            Label summaryTitle = Header("每日摘要");
            summaryTitle.SetBounds(20, 304, 300, 22);
            _summary.SetBounds(20, 332, 300, 22);
            Label summaryLabel = new Label { Text = "在", TextAlign = ContentAlignment.MiddleLeft };
            Label summaryTail = new Label { Text = "点之后提示一次", TextAlign = ContentAlignment.MiddleLeft };
            summaryLabel.SetBounds(38, 360, 22, 26);
            _summaryHour.SetBounds(60, 360, 60, 26);
            summaryTail.SetBounds(126, 360, 140, 26);

            Button ok = new Button { Text = "保存", DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
            ok.SetBounds(250, 392, 88, 28); cancel.SetBounds(348, 392, 88, 28);

            Controls.AddRange(new Control[]{ goalTitle, _goalOff, _goalKeys, _goalKeysValue, _goalActive, _goalHours, hoursLabel, goalNote,
                reminderTitle, _continuous, thresholdLabel, _continuousMinutes, thresholdTail, cooldownLabel, _cooldown, cooldownTail, dndNote,
                summaryTitle, _summary, summaryLabel, _summaryHour, summaryTail, ok, cancel });
            AcceptButton = ok; CancelButton = cancel;

            // 载入现有设置
            _goalOff.Checked = !WellbeingSettings.GoalEnabled;
            _goalKeys.Checked = WellbeingSettings.GoalMode == WellbeingSettings.GoalKeys;
            _goalActive.Checked = WellbeingSettings.GoalMode == WellbeingSettings.GoalActive;
            if (!WellbeingSettings.GoalEnabled) _goalKeys.Checked = true;
            _goalKeysValue.Value = Math.Max(_goalKeysValue.Minimum, Math.Min(_goalKeysValue.Maximum,
                WellbeingSettings.GoalMode == WellbeingSettings.GoalKeys && WellbeingSettings.GoalTarget > 0
                    ? (decimal)WellbeingSettings.GoalTarget : 20000));
            _goalHours.Value = Math.Max(_goalHours.Minimum, Math.Min(_goalHours.Maximum,
                WellbeingSettings.GoalMode == WellbeingSettings.GoalActive && WellbeingSettings.GoalTarget > 0
                    ? (decimal)(WellbeingSettings.GoalTarget / 3600) : 6M));
            _continuous.Checked = WellbeingSettings.ContinuousEnabled;
            _continuousMinutes.Value = Math.Max(_continuousMinutes.Minimum, Math.Min(_continuousMinutes.Maximum, WellbeingSettings.ContinuousMinutes));
            _cooldown.Value = Math.Max(_cooldown.Minimum, Math.Min(_cooldown.Maximum, WellbeingSettings.CooldownMinutes));
            _summary.Checked = WellbeingSettings.SummaryEnabled;
            _summaryHour.Value = Math.Max(_summaryHour.Minimum, Math.Min(_summaryHour.Maximum, WellbeingSettings.SummaryHour));

            _goalKeys.CheckedChanged += delegate { SyncGoalControls(); };
            _goalActive.CheckedChanged += delegate { SyncGoalControls(); };
            _goalOff.CheckedChanged += delegate { SyncGoalControls(); };
            SyncGoalControls();

            ok.Click += delegate { Apply(); };
        }

        private static Label Header(string text)
        {
            return new Label { Text = text, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold) };
        }

        private void SyncGoalControls()
        {
            _goalKeysValue.Enabled = _goalKeys.Checked;
            _goalHours.Enabled = _goalActive.Checked;
        }

        private void Apply()
        {
            if (_goalOff.Checked)
            {
                WellbeingSettings.GoalMode = WellbeingSettings.GoalOff;
                WellbeingSettings.GoalTarget = 0;
            }
            else if (_goalKeys.Checked)
            {
                WellbeingSettings.GoalMode = WellbeingSettings.GoalKeys;
                WellbeingSettings.GoalTarget = (double)_goalKeysValue.Value;
            }
            else
            {
                WellbeingSettings.GoalMode = WellbeingSettings.GoalActive;
                WellbeingSettings.GoalTarget = (double)_goalHours.Value * 3600;
            }
            WellbeingSettings.ContinuousEnabled = _continuous.Checked;
            WellbeingSettings.ContinuousMinutes = (int)_continuousMinutes.Value;
            WellbeingSettings.CooldownMinutes = (int)_cooldown.Value;
            WellbeingSettings.SummaryEnabled = _summary.Checked;
            WellbeingSettings.SummaryHour = (int)_summaryHour.Value;
            Store.Save();
        }
    }
}
