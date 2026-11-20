// ============================================================================
//  KeyMouseStats v3.0.1 · DailyReportBattle.cs
//
//  「日报」页的长图入口:打码开关 + 日报/周报/月报 3× 高清 PNG。
//  点击「生成战报长图」弹出小菜单,不改变底栏布局。
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal sealed partial class Dashboard
    {
        /// <summary>导出长图菜单。不能在 Closed 里立即 Dispose,否则菜单点击回程会访问已释放对象。</summary>
        private ContextMenuStrip _reportMenu;

        /// <summary>切换战报打码开关;状态栏杆立即反映。</summary>
        private void ToggleBattleMask()
        {
            PrivacyMask.Enabled = !PrivacyMask.Enabled;
            SetStatus(PrivacyMask.Enabled ? "战报打码已开启" : "战报打码已关闭");
        }

        /// <summary>日报/周报/月报的统一入口。</summary>
        private void ShowReportExportMenu()
        {
            if (_reportMenu != null)
            {
                try { _reportMenu.Dispose(); } catch (Exception) { }
                _reportMenu = null;
            }

            ArtTheme theme = ArtTheme.Current;
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;
            menu.Font = new Font("Microsoft YaHei UI", 9f);
            menu.BackColor = theme.Card;
            menu.ForeColor = theme.Text;
            menu.Renderer = new ToolStripProfessionalRenderer(new ReportMenuColors(theme));

            ToolStripMenuItem header = new ToolStripMenuItem("导出为高清 PNG");
            header.Enabled = false;
            header.ForeColor = theme.Muted;
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem daily = new ToolStripMenuItem("日报长图  ·  4320px");
            daily.ForeColor = theme.Text;
            daily.Click += delegate { GenerateBattleReport(); };
            menu.Items.Add(daily);

            ToolStripMenuItem weekly = new ToolStripMenuItem("周报长图  ·  4320px");
            weekly.ForeColor = theme.Text;
            weekly.Click += delegate { GenerateWeeklyReport(); };
            menu.Items.Add(weekly);

            ToolStripMenuItem monthly = new ToolStripMenuItem("月报长图  ·  4320px");
            monthly.ForeColor = theme.Text;
            monthly.Click += delegate { GenerateMonthlyReport(); };
            menu.Items.Add(monthly);

            _reportMenu = menu;
            // FrameRight / FrameH 是逻辑坐标;Show 需要客户区像素,DPI 下要乘 _s。
            int x = (int)((FrameRight - 476f) * _s);
            int y = (int)((FrameH - 44f) * _s);
            menu.Show(this, new Point(x, y), ToolStripDropDownDirection.AboveRight);
        }

        /// <summary>日报:当前选中日期的 3× 高清 PNG。</summary>
        private void GenerateBattleReport()
        {
            try
            {
                EnsureDayReport();
                DailyReport report = _dayReport;
                DayRecord record = Analysis.GetDay(report.Day);

                using (Bitmap bitmap = DailyReportPoster.Render(report, record, PrivacyMask.Enabled))
                {
                    string path = "";
                    try { path = DailyReportPoster.Save(bitmap, report.Day); }
                    catch (Exception) { }

                    bool copied = false;
                    try { Clipboard.SetImage(bitmap); copied = true; }
                    catch (Exception) { }

                    SetImageStatus("日报", bitmap, path, copied);
                }
            }
            catch (Exception) { SetStatus("日报长图生成失败"); }
        }

        /// <summary>周报:以当前选中日为最后一天的最近 7 天。</summary>
        private void GenerateWeeklyReport()
        {
            try
            {
                EnsureDayReport();
                PeriodReport report = PeriodReport.BuildWeek(_dayReport.Day);
                using (Bitmap bitmap = PeriodPoster.Render(report, PrivacyMask.Enabled))
                {
                    string path = "";
                    try { path = PeriodPoster.Save(bitmap, report); }
                    catch (Exception) { }
                    bool copied = false;
                    try { Clipboard.SetImage(bitmap); copied = true; }
                    catch (Exception) { }
                    SetImageStatus("周报", bitmap, path, copied);
                }
            }
            catch (Exception) { SetStatus("周报长图生成失败"); }
        }

        /// <summary>月报:当前选中日所在月份,当月统计到今天。</summary>
        private void GenerateMonthlyReport()
        {
            try
            {
                EnsureDayReport();
                PeriodReport report = PeriodReport.BuildMonth(_dayReport.Day);
                using (Bitmap bitmap = PeriodPoster.Render(report, PrivacyMask.Enabled))
                {
                    string path = "";
                    try { path = PeriodPoster.Save(bitmap, report); }
                    catch (Exception) { }
                    bool copied = false;
                    try { Clipboard.SetImage(bitmap); copied = true; }
                    catch (Exception) { }
                    SetImageStatus("月报", bitmap, path, copied);
                }
            }
            catch (Exception) { SetStatus("月报长图生成失败"); }
        }

        private void SetImageStatus(string label, Bitmap bitmap, string path, bool copied)
        {
            string size = bitmap.Width + "×" + bitmap.Height;
            if (path.Length > 0 && copied) SetStatus(label + "长图已保存并复制 (" + size + ")");
            else if (path.Length > 0) SetStatus(label + "长图已保存 (" + size + ")");
            else if (copied) SetStatus(label + "长图已复制 (" + size + ")");
            else SetStatus(label + "长图生成失败");
        }

        /// <summary>让右键菜单在当前主题下不突兀。</summary>
        private sealed class ReportMenuColors : ProfessionalColorTable
        {
            private readonly ArtTheme _theme;

            internal ReportMenuColors(ArtTheme theme)
            {
                _theme = theme;
            }

            public override Color ToolStripDropDownBackground { get { return _theme.Card; } }
            public override Color MenuBorder { get { return _theme.Line; } }
            public override Color MenuItemBorder { get { return _theme.Line; } }
            public override Color MenuItemSelected { get { return _theme.Raised; } }
            public override Color MenuItemSelectedGradientBegin { get { return _theme.Raised; } }
            public override Color MenuItemSelectedGradientEnd { get { return _theme.Raised; } }
            public override Color MenuItemPressedGradientBegin { get { return _theme.Raised; } }
            public override Color MenuItemPressedGradientEnd { get { return _theme.Raised; } }
            public override Color ImageMarginGradientBegin { get { return _theme.Card; } }
            public override Color ImageMarginGradientMiddle { get { return _theme.Card; } }
            public override Color ImageMarginGradientEnd { get { return _theme.Card; } }
            public override Color SeparatorDark { get { return _theme.Line; } }
            public override Color SeparatorLight { get { return _theme.Line; } }
        }
    }
}
