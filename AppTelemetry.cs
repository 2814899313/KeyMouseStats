using System;
using System.Collections.Generic;

namespace KeyMouseStats
{
    internal static class AppTelemetry
    {
        private static AppUsage _previous, _pending;
        private static DateTime _previousTime, _pendingTime;
        private static bool _previousActive;
        public static void Reset() { _previous = null; _pending = null; _previousActive = false; }
        public static void Prepare(DateTime now, AppUsage snapshot) { _pendingTime = now; _pending = snapshot; }
        public static void Interval(DayRecord day, ActiveSession session, DateTime start, DateTime end)
        {
            day.AppObservedSeconds += (end - start).TotalSeconds;
            string attributed=null;
            if (_pending != null && _previous != null && _pending.ProcessPath.Length > 0
                && string.Equals(_pending.ProcessPath, _previous.ProcessPath, StringComparison.OrdinalIgnoreCase)
                && start >= _previousTime && end <= _pendingTime && (_pendingTime - _previousTime).TotalSeconds <= 5)
            {
                AppUsage app = AppActivity.EnsureRecord(day, _pending);
                if (app.ProcessPath.Length > 0){ app.ActiveSeconds += (end - start).TotalSeconds;attributed=app.ProcessPath; }
            }
            CrossTelemetry.Interval(day,session,attributed,start,end);
            RateMemory.UpdateSession(session, end);
        }
        public static void Finish(DateTime now, bool accepted, bool active)
        {
            if (_previous == null || (now - _previousTime).TotalSeconds > 5 || now < _previousTime)
            { _previous = _pending; _previousTime = now; _previousActive = false; return; }
            if (!accepted) return;
            if (active && _previousActive && _pending != null && _pending.ProcessPath.Length > 0 && _previous.ProcessPath.Length > 0
                && !string.Equals(_previous.ProcessPath, _pending.ProcessPath, StringComparison.OrdinalIgnoreCase))
            {
                DayRecord day = Analysis.GetDay(now.Date);
                if (day != null) day.AppSwitches++;
            }
            _previous = _pending; _previousTime = now; _previousActive = active;
        }
    }

    internal static class RateMemory
    {
        private static readonly Queue<DateTime> Events = new Queue<DateTime>();
        private static DateTime _last;
        public static void Clear() { Events.Clear(); _last = DateTime.MinValue; }
        private static void Prune(DateTime now)
        {
            if (now < _last) Events.Clear();
            _last = now;
            while (Events.Count > 0 && Events.Peek() <= now.AddSeconds(-60)) Events.Dequeue();
        }
        public static void Add(DateTime now)
        {
            Prune(now); Events.Enqueue(now);
            if (Store.Day != now.Date) Store.RollDay(now.Date);
            Store.Today.PeakApm = Math.Max(Store.Today.PeakApm, LiveRate.Apm);
            Store.AllTimePeakApm = Math.Max(Store.AllTimePeakApm, LiveRate.Apm);
        }
        public static void UpdateSession(ActiveSession session, DateTime now)
        {
            // Do not prune by an interpolated interval endpoint preceding the latest input event.
            long count = 0;
            foreach (DateTime time in Events) if (time >= session.Start && time > now.AddSeconds(-60) && time <= now) count++;
            session.RateMeasured = true; session.LastApm = count;
            if (count > session.PeakApm) { session.PeakApm = count; session.PeakOffsetSeconds = Math.Max(0, (now - session.Start).TotalSeconds); }
        }
    }
}
