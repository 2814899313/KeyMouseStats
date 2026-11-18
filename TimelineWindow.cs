// ============================================================================
//  KeyMouseStats v3.0 · Milestone 3 / Step 1  TimelineWindow.cs
//
//  时光机视窗的坐标数学。纯函数:只吃数值与矩形,返回数值。
//  统一以「当日秒」(0..86400)为单位,跨度夹紧在 [600, 86400]。
// ============================================================================

using System;
using System.Drawing;

namespace KeyMouseStats
{
    internal static class TimelineWindow
    {
        internal const int DaySeconds = 86400;
        internal const int MinSpanSeconds = 600;
        internal const int MaxSpanSeconds = DaySeconds;

        /// <summary>当日秒 → 轨道像素。跨度非法时退回整日。</summary>
        internal static float MapSecToX(int second, RectangleF track, int startSec, int spanSec)
        {
            if (track.Width <= 0f) return track.X;
            int span = ClampSpan(spanSec);
            int start = ClampStart(startSec, span);
            return track.X + (second - start) * track.Width / span;
        }

        /// <summary>轨道像素 → 当日秒,夹紧在视窗范围内。</summary>
        internal static int MapXToSec(float x, RectangleF track, int startSec, int spanSec)
        {
            int span = ClampSpan(spanSec);
            int start = ClampStart(startSec, span);
            if (track.Width <= 0f) return start;
            double ratio = (x - track.X) / track.Width;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            return start + (int)Math.Round(ratio * span);
        }

        /// <summary>以锚点秒为中心缩放;锚点在屏幕上的位置保持不变。</summary>
        internal static void Zoom(int startSec, int spanSec, int anchorSec, int steps, out int newStart, out int newSpan)
        {
            int span = ClampSpan(spanSec);
            int start = ClampStart(startSec, span);
            int anchor = Clamp(anchorSec, start, start + span);
            int magnitude = Math.Abs(steps);
            for (int i = 0; i < magnitude; i++)
            {
                int next = steps > 0 ? (int)Math.Round(span / 1.5) : (int)Math.Round(span * 1.5);
                span = ClampSpan(next);
            }
            // 锚点相对位置比例保持
            double ratio = anchorSec > start && span > 0 ? (anchor - start) * 1.0 / (spanSec <= 0 ? 1 : spanSec) : 0;
            int target = anchor - (int)Math.Round(ratio * span);
            newSpan = span;
            newStart = ClampStart(target, span);
        }

        /// <summary>按像素位移平移视窗,夹紧在 [0, 86400-span]。</summary>
        internal static int Pan(int startSec, int spanSec, float dxPixels, RectangleF track)
        {
            int span = ClampSpan(spanSec);
            int start = ClampStart(startSec, span);
            if (track.Width <= 0f || dxPixels == 0f) return start;
            double secondsPerPixel = span * 1.0 / track.Width;
            int target = start - (int)Math.Round(dxPixels * secondsPerPixel);
            return ClampStart(target, span);
        }

        internal static int ClampSpan(int spanSec)
        {
            if (spanSec < MinSpanSeconds) return MinSpanSeconds;
            if (spanSec > MaxSpanSeconds) return MaxSpanSeconds;
            return spanSec;
        }

        internal static int ClampStart(int startSec, int spanSec)
        {
            int span = ClampSpan(spanSec);
            int maxStart = DaySeconds - span;
            if (maxStart < 0) maxStart = 0;
            if (startSec < 0) return 0;
            if (startSec > maxStart) return maxStart;
            return startSec;
        }

        internal static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}