// ============================================================================
//  KeyMouseStats v3.0 · Milestone 3 / Step 3c  DailyReportBattle.cs
//
//  「日报」页的战报长图入口:打码开关 + 生成长图(复制到剪贴板)。
//  只接线,不重算:数据取自 Store.Today / Store.Total / FlowBoard。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal sealed partial class Dashboard
    {
        /// <summary>切换战报打码开关;状态栏杆立即反映。</summary>
        private void ToggleBattleMask()
        {
            PrivacyMask.Enabled = !PrivacyMask.Enabled;
            SetStatus(PrivacyMask.Enabled ? "战报打码已开启" : "战报打码已关闭");
        }

        /// <summary>组装战报模型。清爽版没有事件库时心流卡片整段不占位。</summary>
        private BattleReportModel BuildBattleModel()
        {
            BattleReportModel model = new BattleReportModel();
            DayRecord day = Store.Today;
            model.Headline = "今日战报";
            model.Subtitle = DateTime.Now.ToString("yyyy-MM-dd ddd");
            model.Honor = PrivacyMask.Enabled ? "江湖称号 · 匿名" : "江湖称号 · 键指如飞";

            model.Metrics.Add(Metric("今日击键", Analysis.FmtCount(day.Keys), "次"));
            model.Metrics.Add(Metric("今日点击", Analysis.FmtCount(day.Clicks), "次"));
            model.Metrics.Add(Metric("有效活跃", ActivityMonitor.FormatDuration(day.ActiveSeconds), "今日"));
            model.Metrics.Add(Metric("累计击键", Analysis.FmtCount(Store.Total.Keys), Store.History.Count + " 天记录"));

            FlowBoardData flow = FlowBoard.Get(DateTime.Today);
            if (flow != null && flow.HasData && flow.Report != null)
            {
                model.HasFlow = true;
                model.FlowScore = flow.Report.FocusScore;
                model.DeepText = BattleDuration(flow.DeepMs);
                model.NormalText = BattleDuration(flow.NormalMs);
                model.FragmentedText = BattleDuration(flow.FragmentedMs);
                model.LongestFlow = BattleDuration(flow.Report.LongestDeepMs);
            }
            model.Footer = "键鼠统计 v3.0 · " + (PrivacyMask.Enabled ? "隐私打码已开启" : "原始记录");
            FillBattleApps(model, day);
            FillBattleChars(model);
            return model;
        }

        /// <summary>
        /// 常用应用榜。旧版这里什么都没填,长图上只剩一个空的「常用应用 Top 1」标题 ——
        /// 也就是点了「生成战报长图」看起来像坏了。数据直接取当天的 AppUsage,不读盘、不重算。
        /// </summary>
        private static void FillBattleApps(BattleReportModel model, DayRecord day)
        {
            if (day == null || day.Apps == null) return;
            List<KeyValuePair<string, long>> ranked = new List<KeyValuePair<string, long>>();
            foreach (AppUsage usage in day.Apps.Values)
            {
                if (usage == null || usage.Keys <= 0) continue;
                string path = usage.ProcessPath;
                if (string.IsNullOrEmpty(path)) path = usage.Title;
                if (string.IsNullOrEmpty(path)) continue;
                ranked.Add(new KeyValuePair<string, long>(path, usage.Keys));
            }
            ranked.Sort(delegate(KeyValuePair<string, long> a, KeyValuePair<string, long> b)
            {
                return b.Value.CompareTo(a.Value);
            });
            for (int i = 0; i < ranked.Count && i < 5; i++)
                model.TopApps.Add(AppActivity.FriendlyName(ranked[i].Key) + "   " + Analysis.FmtCount(ranked[i].Value) + " 键");
        }

        /// <summary>输入法亮点:复用「字符内容」页已经解析好的 Rime 快照,只取今天的高频汉字。</summary>
        private void FillBattleChars(BattleReportModel model)
        {
            try
            {
                EnsureTextData();
                if (_rimeStats == null) return;
                RimeSlice today = _rimeStats.Slice(DateTime.Today, DateTime.Today);
                if (today == null) return;
                List<KeyValuePair<int, long>> chars = today.TopChars(16);
                string text = "";
                for (int i = 0; i < chars.Count && text.Length < 16; i++)
                {
                    if (!RimeText.IsCjk(chars[i].Key)) continue;
                    text += RimeText.DisplayName(chars[i].Key);
                }
                model.TopChars = text;
            }
            catch (Exception) { }
        }

        private static BattleReportMetric Metric(string label, string value, string hint)
        {
            BattleReportMetric metric = new BattleReportMetric();
            metric.Label = label;
            metric.Value = value;
            metric.Hint = hint;
            return metric;
        }

        private static string BattleDuration(long ms)
        {
            if (ms <= 0) return "0m";
            long minutes = ms / 60000;
            if (minutes < 60) return minutes + "m";
            return (minutes / 60) + "h" + (minutes % 60 == 0 ? "" : (minutes % 60) + "m");
        }

        /// <summary>生成 1080px 战报长图并放进剪贴板;Bitmap 用完立即释放。</summary>
        private void GenerateBattleReport()
        {
            try
            {
                BattleReportModel model = BuildBattleModel();
                using (Bitmap bitmap = BattleReport.Render(model, PrivacyMask.Enabled))
                {
                    try
                    {
                        Clipboard.SetImage(bitmap);
                        SetStatus("战报长图已复制到剪贴板(" + bitmap.Width + "×" + bitmap.Height + ")");
                    }
                    catch (Exception) { SetStatus("剪贴板不可用,战报未复制"); }
                }
            }
            catch (Exception) { SetStatus("战报生成失败"); }
        }
    }
}
