// ============================================================================
//  KeyMouseStats v3.0 · Milestone 3 / Step 1  ContextCost.cs
//
//  上下文切换损耗:从主力工作窗口切出到干扰源,再切回并恢复稳定输入所需的滞后。
//  纯函数,无 IO / 无 UI。
// ============================================================================

using System;
using System.Collections.Generic;

namespace KeyMouseStats
{
    internal sealed class ContextSwitchStat
    {
        public string ProcessName;
        public int CategoryId;
        public int Count;
        public double MedianLagSeconds;
        public double AvgLagSeconds;
        public double TotalLagSeconds;
    }

    internal static class ContextCost
    {
        /// <summary>常见浏览器进程,视为干扰源。</summary>
        internal static bool IsBrowser(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            switch (processName.ToLowerInvariant())
            {
                case "chrome.exe":
                case "msedge.exe":
                case "firefox.exe":
                case "brave.exe":
                case "opera.exe":
                case "vivaldi.exe":
                case "iexplore.exe":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>干扰源:沟通类(8)或常见浏览器。</summary>
        internal static bool IsDistractor(FlowSessionRow row)
        {
            if (row == null) return false;
            if (row.CategoryId == 8) return true;
            return IsBrowser(row.ProcessName);
        }

        /// <summary>
        /// 追踪「主力 → 干扰源 → 切回主力并恢复稳定输入」的滞后。
        /// 恢复判据:连续击键 ≥ minKeys 或连续活跃 ≥ minActiveSeconds。
        /// </summary>
        internal static List<ContextSwitchStat> Analyze(IList<FlowSessionRow> sessions,
            IList<FlowBurstRow> bursts, FlowConfig config, int minKeys, int minActiveSeconds)
        {
            if (minKeys <= 0) minKeys = 5;
            if (minActiveSeconds <= 0) minActiveSeconds = 15;
            List<ContextSwitchStat> result = new List<ContextSwitchStat>();
            List<FlowSessionRow> normalized = FlowAnalyzer.Normalize(sessions, config);
            if (normalized.Count < 3) return result;

            Dictionary<string, List<double>> lags = new Dictionary<string, List<double>>(StringComparer.Ordinal);
            Dictionary<string, int> categoryOf = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < normalized.Count; i++)
            {
                FlowSessionRow current = normalized[i];
                if (!FlowAnalyzer.IsWorkCategory(current.CategoryId)) continue;
                // 找主力之后紧邻的干扰源
                int distractorIndex = i + 1;
                if (distractorIndex >= normalized.Count) break;
                FlowSessionRow distractor = normalized[distractorIndex];
                if (!IsDistractor(distractor)) continue;

                long outMs = distractor.StartMs;
                long recoveryMs = FindRecovery(normalized, bursts, distractorIndex + 1, minKeys, minActiveSeconds);
                if (recoveryMs <= outMs) continue;

                string key = string.IsNullOrEmpty(distractor.ProcessName) ? "未知应用" : distractor.ProcessName;
                List<double> list;
                if (!lags.TryGetValue(key, out list))
                {
                    list = new List<double>();
                    lags[key] = list;
                }
                list.Add((recoveryMs - outMs) / 1000.0);
                categoryOf[key] = distractor.CategoryId;
            }

            foreach (KeyValuePair<string, List<double>> pair in lags)
            {
                List<double> values = pair.Value;
                values.Sort();
                ContextSwitchStat stat = new ContextSwitchStat();
                stat.ProcessName = pair.Key;
                int category;
                if (categoryOf.TryGetValue(pair.Key, out category)) stat.CategoryId = category;
                stat.Count = values.Count;
                double sum = 0;
                for (int i = 0; i < values.Count; i++) sum += values[i];
                stat.TotalLagSeconds = sum;
                stat.AvgLagSeconds = values.Count > 0 ? sum / values.Count : 0;
                stat.MedianLagSeconds = values.Count == 0 ? 0
                    : (values.Count % 2 == 1 ? values[values.Count / 2]
                        : (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2.0);
                result.Add(stat);
            }

            result.Sort(delegate(ContextSwitchStat a, ContextSwitchStat b)
            {
                int byTotal = b.TotalLagSeconds.CompareTo(a.TotalLagSeconds);
                if (byTotal != 0) return byTotal;
                return string.CompareOrdinal(a.ProcessName, b.ProcessName);
            });
            return result;
        }

        /// <summary>从 startIndex 起找第一段主力窗口内恢复稳定输入的时刻;找不到返回 -1。</summary>
        private static long FindRecovery(IList<FlowSessionRow> sessions, IList<FlowBurstRow> bursts,
            int startIndex, int minKeys, int minActiveSeconds)
        {
            for (int i = startIndex; i < sessions.Count; i++)
            {
                FlowSessionRow row = sessions[i];
                if (!FlowAnalyzer.IsWorkCategory(row.CategoryId)) continue;

                if (bursts != null)
                {
                    for (int b = 0; b < bursts.Count; b++)
                    {
                        FlowBurstRow burst = bursts[b];
                        if (burst == null) continue;
                        if (burst.StartMs < row.StartMs || burst.StartMs > row.EndMs) continue;
                        if (burst.KeyCount >= minKeys) return burst.StartMs;
                    }
                }
                long activeMs = row.ActiveMs > 0 ? row.ActiveMs : (row.EndMs - row.StartMs);
                if (activeMs >= minActiveSeconds * 1000L)
                    return row.StartMs + minActiveSeconds * 1000L;
            }
            return -1;
        }
    }
}