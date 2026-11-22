// ============================================================================
//  KeyMouseStats v3.3  FocusBreak.cs
//
//  心流保护型微休息状态机。只做内存判断:
//    累计非空闲活跃 → Pending → (退出 Deep + 自然空闲缓冲 + 非免打扰) → Ready
//  提醒展示、托盘延后/今日静音由 UI 层负责;本文件不写磁盘、不弹窗。
//
//  C# 5:无 $"…" / ?. / => 成员 / nameof / out var / 元组。
// ============================================================================

using System;
using System.Globalization;

namespace KeyMouseStats
{
    internal enum FocusBreakStatus
    {
        None = 0,
        Pending = 1,
        Ready = 2
    }

    internal sealed class FocusBreakConfig
    {
        public int AfterMinutes = 45;
        public int IdleBufferSeconds = 18;
        public int SnoozeMinutes = 10;

        internal void Normalize()
        {
            if (AfterMinutes < 1) AfterMinutes = 1;
            if (IdleBufferSeconds < 1) IdleBufferSeconds = 1;
            if (SnoozeMinutes < 1) SnoozeMinutes = 1;
        }
    }

    /// <summary>
    /// 免打扰探测:ShellNotification(QUNS) + FullscreenWindow 几何兜底 + 会议应用进程。
    /// FocusBreak.Update 也接受外部 bool,测试可完全脱离真实系统状态。
    /// </summary>
    internal static class FocusDnd
    {
        private static readonly string[] MeetingProcesses = new string[]
        {
            "teams.exe", "ms-teams.exe", "zoom.exe", "zoom.us.exe", "wemeetapp.exe",
            "dingtalk.exe", "feishu.exe", "lark.exe", "webex.exe", "webexmeeting.exe",
            "gotomeeting.exe", "bluejeans.exe"
        };

        /// <summary>测试可替换的会议探测;默认看当前前台应用进程名。</summary>
        internal static Func<bool> MeetingProbe = delegate { return MeetingAppActive(); };

        internal static bool ShouldSuppress(int state, bool fullscreen, bool meeting)
        {
            if (state == ShellNotification.QunsQuietTime) return true;
            if (state > 0 && ShellNotification.IsSuppressed(state)) return true;
            return fullscreen || meeting;
        }

        internal static bool Active()
        {
            if (!WellbeingSettings.FullscreenDnd) return false;
            int state = ShellNotification.QueryState();
            bool fullscreen = false;
            bool meeting = false;
            try { fullscreen = FullscreenWindow.Active(); } catch { }
            try { meeting = MeetingProbe != null && MeetingProbe(); } catch { }
            return ShouldSuppress(state, fullscreen, meeting);
        }

        private static bool MeetingAppActive()
        {
            AppUsage app = ForegroundApp.Cached();
            if (app == null) return false;
            string name = app.AppName;
            if (string.IsNullOrEmpty(name)) return false;
            name = name.ToLowerInvariant();
            for (int i = 0; i < MeetingProcesses.Length; i++)
                if (string.Equals(name, MeetingProcesses[i], StringComparison.Ordinal)) return true;
            return false;
        }
    }

    /// <summary>
    /// 微休息状态机。一个实例对应一次运行;SnoozeUntil / MutedDay 由配置层持久化,
    /// UI 在启动时把 WellbeingSettings 中的值写回本实例。
    /// </summary>
    internal sealed class FocusBreak
    {
        private readonly FocusBreakConfig _config;
        private DateTime _lastUpdate = DateTime.MinValue;
        private DateTime _lastDay = DateTime.MinValue;
        private double _activeSeconds;
        private bool _pending;

        public DateTime SnoozeUntil = DateTime.MinValue;
        public string MutedDay = "";
        public DateTime LastPromptAt = DateTime.MinValue;

        public FocusBreak() : this(null) { }

        public FocusBreak(FocusBreakConfig config)
        {
            _config = config == null ? new FocusBreakConfig() : config;
            _config.Normalize();
        }

        public double ActiveSeconds { get { return _activeSeconds; } }
        public bool Pending { get { return _pending; } }
        public FocusBreakConfig Config { get { return _config; } }

        public void Reset()
        {
            _lastUpdate = DateTime.MinValue;
            _lastDay = DateTime.MinValue;
            _activeSeconds = 0;
            _pending = false;
            SnoozeUntil = DateTime.MinValue;
            MutedDay = "";
            LastPromptAt = DateTime.MinValue;
        }

        /// <summary>使用真实系统免打扰状态。</summary>
        public FocusBreakStatus Update(DateTime now, FocusSnapshot snapshot)
        {
            return Update(now, snapshot, FocusDnd.Active());
        }

        /// <summary>测试/预览可显式指定 dnd。</summary>
        public FocusBreakStatus Update(DateTime now, FocusSnapshot snapshot, bool dnd)
        {
            if (_lastUpdate == DateTime.MinValue)
            {
                _lastUpdate = now;
                _lastDay = now.Date;
                return _pending ? FocusBreakStatus.Pending : FocusBreakStatus.None;
            }

            double delta = (now - _lastUpdate).TotalSeconds;
            if (delta < 0) delta = 0;
            if (delta > 300) delta = 0;   // 定时器长停顿/休眠不补记活跃
            _lastUpdate = now;
            if (_lastDay != now.Date)
            {
                _lastDay = now.Date;
                _activeSeconds = 0;
                _pending = false;
                delta = 0;
            }

            bool muted = IsMuted(now);
            bool snoozed = SnoozeUntil > now;
            bool idle = snapshot == null || snapshot.Busy
                || snapshot.IdleSeconds >= _config.IdleBufferSeconds;

            // 先判断 Ready:只有 Pending、离开 Deep、自然空闲、非 DND、未被延后/静音才触发。
            if (_pending && snapshot != null && !snapshot.Busy && !dnd && !muted && !snoozed
                && snapshot.State != FlowState.Deep && idle)
            {
                _activeSeconds = 0;
                _pending = false;
                LastPromptAt = now;
                return FocusBreakStatus.Ready;
            }

            if (!idle && snapshot != null)
            {
                _activeSeconds += delta;
                if (_activeSeconds >= _config.AfterMinutes * 60.0) _pending = true;
            }
            else if (!_pending)
            {
                // 没有待提醒时,一次自然空闲就是一次微休息,重新累计。
                _activeSeconds = 0;
            }

            return _pending ? FocusBreakStatus.Pending : FocusBreakStatus.None;
        }

        public void Snooze(DateTime now)
        {
            Snooze(now, _config.SnoozeMinutes);
        }

        public void Snooze(DateTime now, int minutes)
        {
            if (minutes < 1) minutes = 1;
            SnoozeUntil = now.AddMinutes(minutes);
        }

        public void MuteToday(DateTime now)
        {
            MutedDay = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            SnoozeUntil = DateTime.MinValue;
        }

        public bool IsSnoozed(DateTime now)
        {
            return SnoozeUntil > now;
        }

        public bool IsMuted(DateTime now)
        {
            return !string.IsNullOrEmpty(MutedDay)
                && string.Equals(MutedDay, now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
}
