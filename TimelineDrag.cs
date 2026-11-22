// ============================================================================
//  KeyMouseStats v3.0 · Milestone 3 / Step 2b  TimelineDrag.cs
//
//  时光机轨道的独立鼠标状态机。完全不碰窗体:调用方把内容坐标喂进来,
//  它只回答「这次按下/移动/抬起/滚轮该怎么改视窗」。这样「拖标题栏不移动游标」
//  这类约束可以在纯函数层面被测试钉住,而不用起窗体。
// ============================================================================

using System;
using System.Drawing;

namespace KeyMouseStats
{
    internal sealed class TimelineDrag
    {
        /// <summary>位移小于此像素数视为「点击」而不是「拖动」。</summary>
        internal const int ClickTolerancePx = 4;
        internal const int StepSeconds = 60;
        internal const int FastStepSeconds = 600;
        internal const int DefaultSpanSeconds = TimelineWindow.DaySeconds;
        internal const int FocusPaddingSeconds = 1800;
        internal const int FocusMinSpanSeconds = 7200;

        public bool Active;
        public RectangleF Track;
        public int StartSec;
        public int SpanSec = DefaultSpanSeconds;
        /// <summary>已选中的分钟游标;-1 表示未选中。</summary>
        public int CursorSec = -1;
        public int HoverSec = -1;

        private float _anchorX;
        private float _lastX;
        private bool _moved;

        public bool HasCursor { get { return CursorSec >= 0; } }
        public int EndSec { get { return StartSec + SpanSec; } }

        /// <summary>命中测试:必须落在轨道矩形内。</summary>
        public bool HitTrack(float x, float y)
        {
            return Track.Width > 0f && Track.Height > 0f && Track.Contains(x, y);
        }

        /// <summary>
        /// 左键按下。只有命中轨道才捕获;返回 false 时调用方必须把事件让给标题栏 / chip 等其他逻辑。
        /// </summary>
        public bool BeginDrag(float x, float y)
        {
            if (!HitTrack(x, y)) return false;
            Active = true;
            _anchorX = x;
            _lastX = x;
            _moved = false;
            return true;
        }

        /// <summary>拖动中。返回 true 表示视窗发生了变化(需要重绘)。</summary>
        public bool Drag(float x, float y)
        {
            if (!Active) return false;
            if (Math.Abs(x - _anchorX) >= ClickTolerancePx) _moved = true;
            if (!_moved) return false;
            int start = TimelineWindow.Pan(StartSec, SpanSec, x - _lastX, Track);
            _lastX = x;
            if (start == StartSec) return false;
            StartSec = start;
            return true;
        }

        /// <summary>
        /// 左键抬起。位移不足 ClickTolerancePx 视为点选:把游标吸附到该分钟。
        /// 返回 true 表示游标发生变化。
        /// </summary>
        public bool EndDrag(float x)
        {
            bool wasActive = Active;
            bool moved = _moved;
            Active = false;
            _moved = false;
            if (!wasActive) return false;
            if (moved) return false;
            int second = TimelineWindow.MapXToSec(x, Track, StartSec, SpanSec);
            if (second == CursorSec) return false;
            CursorSec = second;
            return true;
        }

        /// <summary>
        /// 自动聚焦活跃区间:前后各留 30 分钟,最小跨度 2 小时。
        /// 双击仍由 ResetView 复位为 24 小时全天。
        /// </summary>
        public bool FocusRange(int firstSec, int lastSec)
        {
            int start, span;
            TimelineWindow.FocusRange(firstSec, lastSec, FocusPaddingSeconds, FocusMinSpanSeconds,
                out start, out span);
            bool changed = StartSec != start || SpanSec != span || CursorSec >= 0 || HoverSec >= 0;
            StartSec = start;
            SpanSec = span;
            CursorSec = -1;
            HoverSec = -1;
            Active = false;
            _moved = false;
            return changed;
        }

        /// <summary>双击复位:回到全天 [0, 86400] 并清除游标。返回是否需要重绘。</summary>
        public bool ResetView()
        {
            Active = false;
            _moved = false;
            bool changed = SpanSec != DefaultSpanSeconds || StartSec != 0 || CursorSec >= 0;
            SpanSec = DefaultSpanSeconds;
            StartSec = 0;
            CursorSec = -1;
            HoverSec = -1;
            return changed;
        }

        /// <summary>滚轮缩放:以鼠标所在时刻为锚点。返回是否需要重绘。</summary>
        public bool Wheel(int delta, float x)
        {
            if (delta == 0) return false;
            int anchor = TimelineWindow.MapXToSec(x, Track, StartSec, SpanSec);
            int nextStart, nextSpan;
            TimelineWindow.Zoom(StartSec, SpanSec, anchor, delta > 0 ? 1 : -1, out nextStart, out nextSpan);
            if (nextStart == StartSec && nextSpan == SpanSec) return false;
            StartSec = nextStart;
            SpanSec = nextSpan;
            return true;
        }

        /// <summary>键盘步进(±秒)。游标不存在时先落到视窗起点。</summary>
        public bool Step(int seconds)
        {
            if (seconds == 0) return false;
            int baseSecond = CursorSec >= 0 ? CursorSec : StartSec;
            int target = baseSecond + seconds;
            if (target < StartSec) target = TimelineWindow.ClampStart(target, SpanSec);
            if (target > EndSec - 1) target = EndSec - 1;
            if (target < 0) target = 0;
            if (target > TimelineWindow.DaySeconds - 1) target = TimelineWindow.DaySeconds - 1;
            if (target == CursorSec) return false;
            CursorSec = target;
            return true;
        }

        public bool ClearCursor()
        {
            if (CursorSec < 0) return false;
            CursorSec = -1;
            return true;
        }

        /// <summary>悬停更新,返回是否需要重绘。</summary>
        public bool Hover(float x, float y)
        {
            int second = HitTrack(x, y)
                ? TimelineWindow.MapXToSec(x, Track, StartSec, SpanSec)
                : -1;
            if (second == HoverSec) return false;
            HoverSec = second;
            return true;
        }

        /// <summary>当前用于联动的时刻(优先游标,其次悬停);都没有返回 -1。</summary>
        public int FocusSec
        {
            get
            {
                if (CursorSec >= 0) return CursorSec;
                return HoverSec;
            }
        }
    }
}