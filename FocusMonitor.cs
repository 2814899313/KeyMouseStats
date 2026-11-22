// ============================================================================
//  KeyMouseStats v3.3  FocusMonitor.cs
//
//  纯内存实时心流状态机。输入只来自 LiveRate 的 5 分钟输入桶、
//  ForegroundApp.Cached() 与 ActivityMonitor 的空闲状态;
//  不引用 KmsDb / FlowBoard / SQLite,标准版与清爽版共用。
//
//  C# 5:无 $"…" / ?. / => 成员 / nameof / out var / 元组。
// ============================================================================

using System;
using System.Collections.Generic;

namespace KeyMouseStats
{
    /// <summary>实时状态机的可调阈值。</summary>
    internal sealed class FocusMonitorConfig
    {
        public int WindowSeconds = 300;
        public int IdleSeconds = 20;
        public long MinInput5m = 15;
        public double NormalStickiness = 0.45;
        public double DeepStickiness = 0.72;
        public int FragmentedSwitchLimit = 12;
        public int DeepSwitchLimit = 6;
        public int HysteresisSeconds = 12;
    }

    /// <summary>不可变实时快照。</summary>
    internal sealed class FocusSnapshot
    {
        public readonly FlowState State;
        public readonly double FocusSeconds;
        public readonly string MainApp;
        public readonly string MainAppName;
        public readonly double Stickiness;
        public readonly int Switches5m;
        public readonly long Input5m;
        public readonly double IdleSeconds;
        public readonly bool Busy;
        public readonly int DistinctApps;
        public readonly long SampledAtMs;

        internal FocusSnapshot(FlowState state, double focusSeconds, string mainApp, string mainAppName,
            double stickiness, int switches5m, long input5m, double idleSeconds, bool busy,
            int distinctApps, long sampledAtMs)
        {
            State = state;
            FocusSeconds = focusSeconds;
            MainApp = mainApp ?? "";
            MainAppName = string.IsNullOrEmpty(mainAppName) ? "未识别应用" : mainAppName;
            Stickiness = stickiness;
            Switches5m = switches5m;
            Input5m = input5m;
            IdleSeconds = idleSeconds;
            Busy = busy;
            DistinctApps = distinctApps;
            SampledAtMs = sampledAtMs;
        }
    }

    /// <summary>
    /// 轻量实时心流评估器。采样频率建议 250ms(与 StatsWidget 主定时器一致),
    /// 状态切换带 10–15 秒滞回,避免单次低输入导致胶囊闪烁。
    /// </summary>
    internal sealed class FocusMonitor
    {
        private sealed class AppSpan
        {
            public long StartMs;
            public long EndMs;
            public string Path;
        }

        private readonly FocusMonitorConfig _config;
        private readonly List<AppSpan> _spans = new List<AppSpan>();
        private AppSpan _current;
        private long _lastSampleMs;
        private bool _hasSample;
        private bool _hasState;
        private FlowState _state = FlowState.Idle;
        private FlowState _candidate = FlowState.Idle;
        private long _candidateSinceMs;
        private double _focusSeconds;
        private FocusSnapshot _currentSnapshot;

        public FocusMonitor() : this(null) { }

        public FocusMonitor(FocusMonitorConfig config)
        {
            _config = config == null ? new FocusMonitorConfig() : config;
            NormalizeConfig(_config);
            _currentSnapshot = MakeSnapshot(FlowState.Idle, 0, "", "", 0, 0, 0, 0, false, 0, 0);
        }

        public FocusSnapshot Current { get { return _currentSnapshot; } }

        /// <summary>清空窗口、状态与累计专注秒数;用于测试或跨日/会话重置。</summary>
        public void Reset()
        {
            _spans.Clear();
            _current = null;
            _lastSampleMs = 0;
            _hasSample = false;
            _hasState = false;
            _state = FlowState.Idle;
            _candidate = FlowState.Idle;
            _candidateSinceMs = 0;
            _focusSeconds = 0;
            _currentSnapshot = MakeSnapshot(FlowState.Idle, 0, "", "", 0, 0, 0, 0, false, 0, 0);
        }

        /// <summary>采样并返回不可变快照。nowMs 使用单调毫秒。</summary>
        public FocusSnapshot Sample(long nowMs, long keys5m, long clicks5m, string appPath,
            uint idleMs, bool busy)
        {
            if (nowMs < 0) nowMs = 0;
            if (nowMs < _lastSampleMs) Reset();
            long delta = _hasSample ? nowMs - _lastSampleMs : 0;
            bool gap = delta > _config.WindowSeconds * 1000L;
            if (gap)
            {
                _spans.Clear();
                _current = null;
                _focusSeconds = 0;
                _state = FlowState.Idle;
                _candidate = FlowState.Idle;
                _candidateSinceMs = nowMs;
                _hasState = true;
                delta = 0;
            }
            _lastSampleMs = nowMs;
            _hasSample = true;

            string path = appPath ?? "";
            if (_current == null)
            {
                _current = new AppSpan();
                _current.StartMs = nowMs;
                _current.EndMs = nowMs;
                _current.Path = path;
                _spans.Add(_current);
            }
            else if (!string.Equals(_current.Path, path, StringComparison.Ordinal))
            {
                _current.EndMs = nowMs;
                AppSpan next = new AppSpan();
                next.StartMs = nowMs;
                next.EndMs = nowMs;
                next.Path = path;
                _spans.Add(next);
                _current = next;
            }
            else
            {
                _current.EndMs = nowMs;
            }

            long windowMs = _config.WindowSeconds * 1000L;
            long windowStart = nowMs - windowMs;
            for (int i = _spans.Count - 1; i >= 0; i--)
                if (_spans[i].EndMs <= windowStart) _spans.RemoveAt(i);

            Dictionary<string, long> dwell = new Dictionary<string, long>(StringComparer.Ordinal);
            long totalDwell = 0;
            int switchCount = 0;
            for (int i = 0; i < _spans.Count; i++)
            {
                AppSpan span = _spans[i];
                long from = Math.Max(span.StartMs, windowStart);
                long to = Math.Min(span.EndMs, nowMs);
                if (to <= from) continue;
                string key = span.Path ?? "";
                long value;
                dwell.TryGetValue(key, out value);
                dwell[key] = value + (to - from);
                totalDwell += to - from;
                // 第一个窗口内片段是“起点”,不是切换;从第二个片段开始计数。
                if (i > 0 && span.StartMs > windowStart) switchCount++;
            }

            double stickiness = 0;
            string mainApp = "";
            long bestDwell = -1;
            foreach (KeyValuePair<string, long> pair in dwell)
            {
                // 无法识别的窗口不参与“主力应用”评选,但仍计入总停留分母。
                if (string.IsNullOrEmpty(pair.Key)) continue;
                if (pair.Value > bestDwell
                    || (pair.Value == bestDwell && string.CompareOrdinal(pair.Key, mainApp) < 0))
                {
                    bestDwell = pair.Value;
                    mainApp = pair.Key;
                }
            }
            if (totalDwell > 0 && bestDwell > 0) stickiness = bestDwell * 1.0 / totalDwell;
            if (string.IsNullOrEmpty(mainApp) && _current != null) mainApp = _current.Path ?? "";

            long input5m = keys5m + clicks5m;
            if (input5m < 0) input5m = 0;
            double idleSeconds = idleMs == uint.MaxValue ? 0 : idleMs / 1000.0;
            bool isBusy = busy;

            FlowState raw = RawState(stickiness, switchCount, input5m, idleSeconds, isBusy);
            bool forceIdle = isBusy || idleSeconds >= _config.IdleSeconds;
            if (forceIdle)
            {
                // Idle 本身已由 20 秒空闲阈值去抖,不再额外等待滞回;
                // 这样 FocusBreak 的 18 秒缓冲不会被状态机拖到 30 秒以上。
                _state = FlowState.Idle;
                _candidate = FlowState.Idle;
                _candidateSinceMs = nowMs;
                _hasState = true;
            }
            else if (!_hasState)
            {
                _state = raw;
                _candidate = raw;
                _candidateSinceMs = nowMs;
                _hasState = true;
            }
            else if (raw == _state)
            {
                _candidate = raw;
                _candidateSinceMs = nowMs;
            }
            else if (raw != _candidate)
            {
                _candidate = raw;
                _candidateSinceMs = nowMs;
            }
            else if (nowMs - _candidateSinceMs >= _config.HysteresisSeconds * 1000L)
            {
                _state = raw;
            }

            bool focus = _state == FlowState.Deep || _state == FlowState.Normal;
            if (focus && !isBusy && idleSeconds < _config.IdleSeconds)
                _focusSeconds += Math.Max(0, delta) / 1000.0;
            else
                _focusSeconds = 0;

            _currentSnapshot = MakeSnapshot(_state, _focusSeconds, mainApp, AppName(mainApp),
                stickiness, switchCount, input5m, idleSeconds, isBusy, dwell.Count, nowMs);
            return _currentSnapshot;
        }

        /// <summary>从现有实时信号采样;OnTick 可直接调用,零磁盘 I/O。</summary>
        public FocusSnapshot SampleLive(long nowMs)
        {
            AppUsage app = ForegroundApp.Cached();
            string path = app == null ? "" : app.ProcessPath;
            return Sample(nowMs, LiveRate.Keys5m, LiveRate.Clicks5m, path,
                ActivityMonitor.SystemIdleMilliseconds(), ActivityMonitor.Busy);
        }

        public FocusSnapshot SampleLive()
        {
            return SampleLive(NowMilliseconds());
        }

        internal static long NowMilliseconds()
        {
            return (long)(System.Diagnostics.Stopwatch.GetTimestamp()
                * (1000.0 / System.Diagnostics.Stopwatch.Frequency));
        }

        private FlowState RawState(double stickiness, int switches5m, long input5m,
            double idleSeconds, bool busy)
        {
            if (busy || idleSeconds >= _config.IdleSeconds) return FlowState.Idle;
            if (switches5m >= _config.FragmentedSwitchLimit || stickiness < _config.NormalStickiness)
                return FlowState.Fragmented;
            if (input5m >= _config.MinInput5m
                && stickiness >= _config.DeepStickiness
                && switches5m <= _config.DeepSwitchLimit)
                return FlowState.Deep;
            if (input5m >= _config.MinInput5m) return FlowState.Normal;
            return FlowState.Fragmented;
        }

        private static FocusSnapshot MakeSnapshot(FlowState state, double focusSeconds, string mainApp,
            string mainAppName, double stickiness, int switches5m, long input5m, double idleSeconds,
            bool busy, int distinctApps, long sampledAtMs)
        {
            return new FocusSnapshot(state, focusSeconds, mainApp, mainAppName, stickiness,
                switches5m, input5m, idleSeconds, busy, distinctApps, sampledAtMs);
        }

        private static string AppName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "未识别应用";
            int slash = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            string name = slash >= 0 ? path.Substring(slash + 1) : path;
            return string.IsNullOrEmpty(name) ? "未识别应用" : name;
        }

        private static void NormalizeConfig(FocusMonitorConfig config)
        {
            if (config.WindowSeconds < 60) config.WindowSeconds = 60;
            if (config.IdleSeconds < 1) config.IdleSeconds = 1;
            if (config.MinInput5m < 0) config.MinInput5m = 0;
            if (config.NormalStickiness < 0) config.NormalStickiness = 0;
            if (config.NormalStickiness > 1) config.NormalStickiness = 1;
            if (config.DeepStickiness < config.NormalStickiness) config.DeepStickiness = config.NormalStickiness;
            if (config.DeepStickiness > 1) config.DeepStickiness = 1;
            if (config.FragmentedSwitchLimit < 1) config.FragmentedSwitchLimit = 1;
            if (config.DeepSwitchLimit < 0) config.DeepSwitchLimit = 0;
            if (config.HysteresisSeconds < 0) config.HysteresisSeconds = 0;
        }
    }
}
