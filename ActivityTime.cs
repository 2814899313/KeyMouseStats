using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal enum SessionStartReason { Legacy, Startup, Idle, ObservationGap, ClockChange, Lock, Suspend, Disconnect, Settings, InputUnavailable, NewDay }

    internal sealed class ActiveSession
    {
        public DateTime Start, End;
        public SessionStartReason StartReason;
        public double Seconds;
        public bool RateMeasured;
        public long PeakApm, LastApm;
        public double PeakOffsetSeconds;
        public double CrossObservedSeconds;
        public readonly Dictionary<string,double> AppSeconds=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);

        public ActiveSession Copy()
        {
            ActiveSession copy = new ActiveSession();
            copy.Start = Start; copy.End = End; copy.StartReason = StartReason; copy.Seconds = Seconds;
            copy.RateMeasured = RateMeasured; copy.PeakApm = PeakApm; copy.LastApm = LastApm;
            copy.PeakOffsetSeconds = PeakOffsetSeconds; copy.CrossObservedSeconds = CrossObservedSeconds;
            foreach (KeyValuePair<string, double> app in AppSeconds) copy.AppSeconds[app.Key] = app.Value;
            return copy;
        }
    }

    // Interval integration is separate from Win32 sampling so boundaries can be tested deterministically.
    internal sealed class ActivityTracker
    {
        private readonly Func<DateTime, DayRecord> _getDay;
        private bool _ready;
        private SessionStartReason _nextReason=SessionStartReason.Startup;
        private DateTime _lastWall;
        private long _lastMono;
        private double _lastIdle;
        private ActiveSession _session;
        private DayRecord _sessionDay;
        private ActiveSession _idlePeriod;
        private DayRecord _idleDay;
        public bool IsActive { get; private set; }
        public ActiveSession CurrentSession { get { return IsActive ? _session : null; } }
        public Action<DayRecord, ActiveSession, DateTime, DateTime> OnActiveInterval;

        public ActivityTracker(Func<DateTime, DayRecord> getDay) { _getDay = getDay; }
        public void Reset(SessionStartReason reason=SessionStartReason.Startup) { _nextReason=reason;_ready = false; _session = null; _sessionDay = null; _idlePeriod = null; _idleDay = null; IsActive = false; }

        public bool Sample(DateTime wall, long monotonicMs, double idleSeconds, int threshold)
        {
            if (idleSeconds < 0 || double.IsNaN(idleSeconds) || double.IsInfinity(idleSeconds)) { Reset(SessionStartReason.InputUnavailable); return false; }
            bool active = idleSeconds < threshold;
            if (!_ready)
            {
                _ready = true; _lastWall = wall; _lastMono = monotonicMs; _lastIdle = idleSeconds;
                IsActive = active; return false; // No reconstruction of time before startup/resume.
            }
            double elapsed = (monotonicMs - _lastMono) / 1000.0;
            if (elapsed < 1 && elapsed >= 0) return false;
            DateTime previousWall = _lastWall;
            double previousIdle = _lastIdle;
            _lastWall = wall; _lastMono = monotonicMs; _lastIdle = idleSeconds;
            IsActive = active;
            double span = (wall - previousWall).TotalSeconds;
            // Pair wall and monotonic timestamps at capture time. Never bridge a clock discontinuity.
            if (elapsed <= 0 || span <= 0 || Math.Abs(span - elapsed) > 1)
            { Break(SessionStartReason.ClockChange); return false; }
            // A delayed UI timer is not necessarily idle. The last observed input guarantees
            // activity until its threshold expires, even without any intervening input.
            // Only integrate that fully guaranteed interval; uncertain gaps remain unobserved.
            bool guaranteedActive=active && previousIdle>=0 && previousIdle+Math.Max(span,elapsed)<=threshold;
            if (elapsed > 5 && !guaranteedActive)
            { Break(SessionStartReason.ObservationGap); return false; }

            double prefix = Math.Min(span, Math.Max(0, threshold - previousIdle));
            double suffixStart = active && idleSeconds < span ? Math.Max(0, span - idleSeconds) : span;
            if (suffixStart <= prefix)
                Add(previousWall, wall, true);
            else
            {
                DateTime prefixEnd = Boundary(previousWall, wall, prefix, span);
                DateTime suffix = Boundary(previousWall, wall, suffixStart, span);
                Add(previousWall, prefixEnd, true);
                Add(prefixEnd, suffix, false);
                Add(suffix, wall, true);
            }
            if (!active) { _session = null; _sessionDay = null;_nextReason=SessionStartReason.Idle; }
            return true;
        }

        private void Break(SessionStartReason reason)
        {_nextReason=reason;_session=null;_sessionDay=null;_idlePeriod=null;_idleDay=null;}

        private static DateTime Boundary(DateTime start, DateTime end, double offset, double span)
        {
            // .NET Framework AddSeconds rounds to milliseconds, unlike DateTime.Now.
            // Reuse sampled endpoints exactly; interior boundaries retain tick precision.
            if(offset<=0)return start;
            if(offset>=span)return end;
            return start.AddTicks(Math.Max(0,Math.Min((end-start).Ticks,(long)Math.Round(offset*TimeSpan.TicksPerSecond))));
        }
        private void Add(DateTime start, DateTime end, bool active)
        {
            if (end <= start) return;
            if (!active) { _session = null; _sessionDay = null;_nextReason=SessionStartReason.Idle; }
            while (start < end)
            {
                // Split at every hour, which also splits at local midnight.
                DateTime boundary = start.Date.AddHours(start.Hour + 1);
                DateTime until = end < boundary ? end : boundary;
                double seconds = (until - start).TotalSeconds;
                DayRecord day = _getDay(start.Date);
                if (active)
                {
                    _idlePeriod = null; _idleDay = null;
                    if (_session == null || !object.ReferenceEquals(_sessionDay, day) || _session.End != start)
                    {
                        SessionStartReason reason=_session!=null&&!object.ReferenceEquals(_sessionDay,day)?SessionStartReason.NewDay:_session!=null?SessionStartReason.ObservationGap:_nextReason;
                        _session = new ActiveSession { Start = start, End = start, StartReason=reason };
                        _sessionDay = day;
                        day.Sessions.Add(_session);
                    }
                    _session.End = until; _session.Seconds += seconds;
                    day.ActiveSeconds += seconds;
                    day.ActiveHours[start.Hour] += seconds;
                    if (OnActiveInterval != null) OnActiveInterval(day, _session, start, until);
                }
                else
                {
                    if (_idlePeriod == null || !object.ReferenceEquals(_idleDay, day) || _idlePeriod.End != start)
                    {
                        _idlePeriod = new ActiveSession { Start = start, End = start };
                        _idleDay = day; day.IdlePeriods.Add(_idlePeriod);
                    }
                    _idlePeriod.End = until; _idlePeriod.Seconds += seconds;
                    day.IdleSeconds += seconds;
                }
                start = until;
            }
        }
    }

    internal static class ActivityMonitor
    {
        [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size, Time; }
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);
        [DllImport("wtsapi32.dll")] private static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);
        [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(IntPtr window);
        private static bool _locked, _disconnected, _suspended;
        private static bool _notificationsReady;
        private static readonly ActivityTracker Tracker = new ActivityTracker(GetDay);
        static ActivityMonitor() { Tracker.OnActiveInterval = AppTelemetry.Interval; }
        public static string Status = "等待采样";
        public static bool IsActive { get { return !_locked && !_disconnected && !_suspended && Tracker.IsActive; } }
        public static ActiveSession CurrentSession { get { return IsActive ? Tracker.CurrentSession : null; } }
        public static void Reset(SessionStartReason reason=SessionStartReason.Startup) { Tracker.Reset(reason); AppTelemetry.Reset(); Status = "等待采样"; }
        public static void Register(IntPtr window) { _notificationsReady = WTSRegisterSessionNotification(window, 0); }
        public static void Unregister(IntPtr window) { WTSUnRegisterSessionNotification(window); _notificationsReady = false; }
        internal static uint IdleMilliseconds(uint currentTick, uint lastInputTick) { return unchecked(currentTick - lastInputTick); }
        private static DayRecord GetDay(DateTime date)
        {
            DayRecord day;
            if (!Store.History.TryGetValue(date, out day))
            { day = new DayRecord { Date = date }; Store.History[date] = day; }
            return day;
        }
        public static bool Tick()
        {
            if (!_notificationsReady) { Tracker.Reset(SessionStartReason.InputUnavailable); AppTelemetry.Reset(); Status = "会话检测不可用"; return false; }
            if (_locked || _disconnected || _suspended) { Status = _locked || _disconnected ? "已锁屏 / 会话已断开" : "已暂停"; return false; }
            LastInput input = new LastInput { Size = (uint)Marshal.SizeOf(typeof(LastInput)) };
            if (!GetLastInputInfo(ref input)) { Tracker.Reset(SessionStartReason.InputUnavailable); AppTelemetry.Reset(); Status = "无法读取空闲状态"; return false; }
            uint idle = IdleMilliseconds(unchecked((uint)Environment.TickCount), input.Time);
            DateTime wall = DateTime.Now;
            long monotonicMs=(long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));
            AppTelemetry.Prepare(wall, ForegroundApp.Capture());
            bool changed = Tracker.Sample(wall, monotonicMs,
                idle / 1000.0, Store.IdleThresholdSeconds);
            AppTelemetry.Finish(wall, changed, IsActive);
            Status = IsActive ? "活跃" : "空闲";
            return changed;
        }
        public static void Message(int message, IntPtr value)
        {
            if (message == 0x02B1 || message == 0x0218) AppTelemetry.Reset();
            int code = unchecked((int)value.ToInt64());
            if (message == 0x02B1) // WM_WTSSESSION_CHANGE
            {
                // 锁屏 / 断开 / 休眠期间钩子收不到抬起事件(安全桌面、会话切换),还按着的键
                // 必须在这里丢弃:否则它们会一直挂在 pending 里,和之后某次抬起错配成长按样本。
                if (code == 7) { _locked = true; HoldTracker.DiscardPending(); Tracker.Reset(SessionStartReason.Lock); }
                else if (code == 8) { _locked = false; Tracker.Reset(SessionStartReason.Lock); }
                else if (code == 2 || code == 4 || code == 6) { _disconnected = true; HoldTracker.DiscardPending(); Tracker.Reset(SessionStartReason.Disconnect); }
                else if (code == 1 || code == 3 || code == 5) { _disconnected = false; Tracker.Reset(SessionStartReason.Disconnect); }
            }
            else if (message == 0x0218) // WM_POWERBROADCAST
            {
                if (code == 4) { _suspended = true; HoldTracker.DiscardPending(); Tracker.Reset(SessionStartReason.Suspend); }
                else if (code == 7 || code == 18) { _suspended = false; Tracker.Reset(SessionStartReason.Suspend); }
            }
        }
        public static string FormatDuration(double seconds)
        {
            long whole = Math.Max(0, (long)seconds);
            if (whole < 60) return whole + " 秒";
            if (whole < 3600) return (whole / 60) + " 分 " + (whole % 60) + " 秒";
            return (whole / 3600) + " 时 " + ((whole % 3600) / 60) + " 分";
        }
        public static string EncodeHours(double[] hours)
        {
            string[] values = new string[24];
            for (int i = 0; i < 24; i++) values[i] = hours[i].ToString("R", CultureInfo.InvariantCulture);
            return string.Join(",", values);
        }
        public static void LoadHours(double[] hours, string text)
        {
            string[] values = text.Split(',');
            for (int i = 0; i < Math.Min(24, values.Length); i++) hours[i] = ParseSeconds(values[i]);
        }
        public static double ParseSeconds(string value)
        {
            double seconds;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)
                && seconds >= 0 && !double.IsInfinity(seconds) ? seconds : 0;
        }
        public static void LoadSession(DayRecord day, string text)
        { LoadPeriod(day, text, false); }
        internal static int RemoveLegacyPhantomSessions(DayRecord day)
        {
            // Only the known signature: <= half a millisecond immediately after a recorded idle interval.
            // Preserve aggregate counters and original period endpoints; do not guess at real short activity.
            HashSet<DateTime> idleEnds=new HashSet<DateTime>();
            foreach(ActiveSession idle in day.IdlePeriods)idleEnds.Add(idle.End);
            return day.Sessions.RemoveAll(delegate(ActiveSession s) {
                return s.Seconds>0 && s.Seconds<=0.0005 && s.End>s.Start
                    && (s.End-s.Start).Ticks<=5000 && idleEnds.Contains(s.Start);
            });
        }
        public static string SessionReason(ActiveSession session)
        {
            switch(session.StartReason)
            {
                case SessionStartReason.Startup:return "程序启动 / 重新计时";
                case SessionStartReason.Idle:return "空闲后恢复";
                case SessionStartReason.ObservationGap:return "采样中断后恢复";
                case SessionStartReason.ClockChange:return "时钟异常后恢复";
                case SessionStartReason.Lock:return "解锁后恢复";
                case SessionStartReason.Suspend:return "休眠后恢复";
                case SessionStartReason.Disconnect:return "会话重连后恢复";
                case SessionStartReason.Settings:return "修改空闲阈值";
                case SessionStartReason.InputUnavailable:return "输入状态读取恢复";
                case SessionStartReason.NewDay:return "跨日分段";
                default:return "旧记录未标注";
            }
        }
        public static void LoadSessionReason(DayRecord day,string text)
        {
            string[] fields=text.Split('|');long ticks;int reason;
            if(fields.Length!=2||!long.TryParse(fields[0],out ticks)||!int.TryParse(fields[1],out reason)||!Enum.IsDefined(typeof(SessionStartReason),reason))return;
            foreach(ActiveSession session in day.Sessions)if(session.Start.Ticks==ticks){session.StartReason=(SessionStartReason)reason;return;}
        }
        public static string EncodeSession(ActiveSession session)
        {
            string value = session.Start.ToString("O", CultureInfo.InvariantCulture) + "|" + session.End.ToString("O", CultureInfo.InvariantCulture)
                + "|" + session.Seconds.ToString("R", CultureInfo.InvariantCulture);
            if (session.RateMeasured) value += "|" + session.PeakApm.ToString(CultureInfo.InvariantCulture) + "|"
                + session.PeakOffsetSeconds.ToString("R", CultureInfo.InvariantCulture) + "|" + session.LastApm.ToString(CultureInfo.InvariantCulture) + "|1";
            return value;
        }
        public static void LoadPeriod(DayRecord day, string text, bool idle)
        {
            string[] fields = text.Split('|');
            DateTime start, end;
            if (fields.Length != 3 && fields.Length != 7 || !DateTime.TryParseExact(fields[0], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out start)
                || !DateTime.TryParseExact(fields[1], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out end)) return;
            double seconds = ParseSeconds(fields[2]);
            if (start.Date != day.Date || end < start || end > day.Date.AddDays(1) || seconds <= 0
                || seconds > (end - start).TotalSeconds + 0.001) return;
            ActiveSession session = new ActiveSession { Start = start, End = end, Seconds = seconds };
            long peak, last;
            if (!idle && fields.Length == 7 && fields[6] == "1" && long.TryParse(fields[3], out peak) && long.TryParse(fields[5], out last) && peak >= 0 && last >= 0 && last <= peak)
            { session.RateMeasured = true; session.PeakApm = peak; session.LastApm = last; session.PeakOffsetSeconds = Math.Min(seconds, ParseSeconds(fields[4])); }
            (idle ? day.IdlePeriods : day.Sessions).Add(session);
        }
    }

    internal sealed class ActivitySettings : ThemedDialog
    {
        public ActivitySettings()
        {
            Text = "空闲阈值 / 活跃时长"; Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(480, 255);
            FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            Controls.Add(new Label { Text = "连续无输入多少秒后判为空闲？", Location = new Point(20, 22), AutoSize = true });
            NumericUpDown seconds = new NumericUpDown { Minimum = 10, Maximum = 3600, Value = Store.IdleThresholdSeconds,
                Location = new Point(20, 60), Width = 120 };
            Controls.Add(seconds);
            Controls.Add(new Label { Text = "秒（默认 60）", Location = new Point(154, 64), AutoSize = true });
            Controls.Add(new Label { Text = "阈值内的短暂停顿计入有效使用时长。\n超过阈值结束使用段，重新输入后开始新的一段。\n锁屏、休眠和采样中断期间不补计；跨日分段。\n修改阈值仅影响之后的计时，当前使用段会结束。",
                Location = new Point(20, 105), Size = new Size(440, 90) });
            Button save = new Button { Text = "保存", Location = new Point(264, 210), Size = new Size(90, 28) };
            save.Click += delegate
            {
                if (Store.IdleThresholdSeconds != (int)seconds.Value) { Store.IdleThresholdSeconds = (int)seconds.Value; ActivityMonitor.Reset(SessionStartReason.Settings); }
                Store.Save(); DialogResult = DialogResult.OK; Close();
            };
            Controls.Add(save);
            Button cancel = new Button { Text = "取消", Location = new Point(369, 210), Size = new Size(90, 28), DialogResult = DialogResult.Cancel };
            Controls.Add(cancel); CancelButton = cancel;
        }
    }

    internal sealed class SessionDetails : ThemedDialog
    {
        private readonly ListView _list;
        private readonly Label _summary;
        private readonly Timer _timer;
        public SessionDetails()
        {
            Text = "今日连续使用段"; Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(820, 480);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; ShowInTaskbar = false;
            MinimumSize = new Size(700, 400);
            _summary = new Label { Location = new Point(18, 16), Size = new Size(780, 48), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(_summary);
            _list = new ListView { View = View.Details, FullRowSelect = true, GridLines = false, ShowItemToolTips=true,
                Location = new Point(18, 72), Size = new Size(784, 310), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            _list.Columns.Add("开始", 160); _list.Columns.Add("段末时间", 160); _list.Columns.Add("有效时长", 170);_list.Columns.Add("开始原因",260);
            _list.SizeChanged+=delegate{int width=Math.Max(400,_list.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-4);for(int i=0;i<3;i++)_list.Columns[i].Width=width/5;_list.Columns[3].Width=width-3*(width/5);};
            Controls.Add(_list);
            Controls.Add(new Label { Text = "段末已包含空闲阈值内的缓冲，段间空白不是完整无输入时长。\n当前阈值只影响后续计时；旧记录缺少中断原因，不自动合并。", Location = new Point(18, 410), Size = new Size(780, 54),
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right });
            _timer = new Timer { Interval = 1000 }; _timer.Tick += delegate { RefreshRows(); }; _timer.Start(); RefreshRows();
        }
        private void RefreshRows()
        {
            DayRecord day = Store.Today;
            _summary.Text = day.Date.ToString("yyyy-MM-dd") + "   有效使用 " + ActivityMonitor.FormatDuration(day.ActiveSeconds)
                + "   共 " + day.Sessions.Count + " 段\n当前：" + ActivityMonitor.Status + " · 空闲阈值 " + Store.IdleThresholdSeconds + " 秒";
            _list.BeginUpdate();
            while (_list.Items.Count > day.Sessions.Count) _list.Items.RemoveAt(_list.Items.Count - 1);
            for (int i = 0; i < day.Sessions.Count; i++)
            {
                ActiveSession session = day.Sessions[i];
                if (_list.Items.Count <= i) _list.Items.Add(new ListViewItem(new string[] { "", "", "", "" }));
                _list.Items[i].SubItems[0].Text = session.Start.ToString("HH:mm:ss");
                _list.Items[i].SubItems[1].Text = session.End.ToString("HH:mm:ss");
                _list.Items[i].SubItems[2].Text = ActivityMonitor.FormatDuration(session.Seconds);
                _list.Items[i].SubItems[3].Text=ActivityMonitor.SessionReason(session);
                _list.Items[i].ToolTipText=session.Start.ToString("HH:mm:ss.fff")+" — "+session.End.ToString("HH:mm:ss.fff")+"\n有效时长 "+session.Seconds.ToString("0.###",CultureInfo.InvariantCulture)+" 秒\n"+ActivityMonitor.SessionReason(session);
            }
            _list.EndUpdate();
        }
        protected override void Dispose(bool disposing) { if (disposing && _timer != null) _timer.Dispose(); base.Dispose(disposing); }
    }
}
