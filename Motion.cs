// ============================================================================
//  键鼠统计 - 统一动效时间轴  Motion.cs
//
//  1.8.0 新增。设计前提(见 1.8 计划):
//    · 钩子回调与界面在同一个线程上,所以每一帧都在和按键处理抢时间:
//      单帧必须有界、动效必须短促、按住键时直接停表。
//    · 时间按单调时钟插值推进,不按帧计数:WinForms 定时器精度约 15.6 ms 且会抖动,
//      帧计数会让时长随负载漂移;按真实时间推进也便于测试用假时钟确定性推进。
//    · 动效只描述「某件事从 A 变到 B」,重绘范围由通道自己声明,调用方据此局部 Invalidate,
//      绝不在动效运行时整窗重绘。
//    · 没有动效时完全停表:不产生任何额外重绘。
//
//  调用方(Dashboard/小部件)负责:窗口不可见、最小化、失焦、锁屏、拖动窗口、
//  按住键(HoldTracker.PendingCount > 0)时把 Paused 置为 true。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace KeyMouseStats
{
    /// <summary>缓动类型。</summary>
    internal static class Ease
    {
        public const int OutCubic = 0;
        public const int OutQuad = 1;
        public const int InOutQuad = 2;
        public const int Linear = 3;

        public static double Apply(int kind, double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            switch (kind)
            {
                case OutQuad: return 1 - (1 - t) * (1 - t);
                case InOutQuad: return t < 0.5 ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t);
                case Linear: return t;
                default: { double inv = 1 - t; return 1 - inv * inv * inv; }
            }
        }
    }

    /// <summary>一个动效通道:从开始时起,用单调时钟推进到 1。</summary>
    internal sealed class MotionChannel
    {
        public long Start;
        public double DurationMs;
        public int Kind;
        public Rectangle Rect;
        /// <summary>为 false 时表示这个通道需要整窗重绘(仅用于页面级切换)。</summary>
        public bool HasRect;
        public bool Finished;
    }

    /// <summary>动效时间轴与档位。所有方法只在界面线程使用。</summary>
    internal static class Motion
    {
        /// <summary>动效档位:关 / 精简 / 完整。与 Store.MotionLevel 对应。</summary>
        public const int LevelOff = 0, LevelReduced = 1, LevelFull = 2;

        /// <summary>单帧上限(毫秒):超过它说明这一帧已经影响到钩子的响应,记账并降级。</summary>
        public const double FrameBudgetMs = 33;
        /// <summary>慢帧记账阈值:超过它写进 ui-stalls.log。</summary>
        public const double SlowFrameLogMs = 100;

        /// <summary>窗口不可见、失焦、锁屏、拖动窗口或按住键时置 true:立即停止推进。</summary>
        public static bool Paused;
        /// <summary>1.8.0:界面上是否真的有人在看(由窗口在每次绘制时置为 Visible)。
        /// 离屏渲染(测试、截图)时为 false:数值滚动直接落在目标值上,画面保持确定性。
        /// 默认为 true,便于非界面调用方直接使用。</summary>
        public static bool Live = true;

        private static readonly Dictionary<string, MotionChannel> Channels = new Dictionary<string, MotionChannel>(StringComparer.Ordinal);
        private static readonly Dictionary<string, double> Chases = new Dictionary<string, double>(StringComparer.Ordinal);
        private static readonly List<string> Scratch = new List<string>();

        // ---------------------------------------------------------------- 时间源
        private static bool _fakeClock;
        private static long _fakeNow;

        /// <summary>测试用:换成可控时钟。</summary>
        internal static void UseFakeClock(long startTicks)
        {
            _fakeClock = true;
            _fakeNow = startTicks;
        }
        /// <summary>测试用:推进假时钟。</summary>
        internal static void AdvanceFakeClock(double milliseconds)
        {
            _fakeNow += Ticks(milliseconds);
        }
        /// <summary>测试用:清空全部状态。</summary>
        internal static void ResetForTests()
        {
            Channels.Clear();
            Chases.Clear();
            Paused = false;
            _fakeClock = false;
            _fakeNow = 0;
        }
        public static long Now { get { return _fakeClock ? _fakeNow : Stopwatch.GetTimestamp(); } }
        private static long Ticks(double milliseconds)
        {
            return (long)Math.Round(milliseconds / 1000.0 * Stopwatch.Frequency);
        }

        // ---------------------------------------------------------------- 档位
        private static bool? _systemReduced;
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint flags);

        /// <summary>系统是否要求「减少动态效果」;读不到时按不要求处理。</summary>
        public static bool SystemPrefersReducedMotion
        {
            get
            {
                if (_systemReduced.HasValue) return _systemReduced.Value;
                bool enabled = true;
                try
                {
                    if (!SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, ref enabled, 0)) enabled = true;
                }
                catch { enabled = true; }
                _systemReduced = !enabled;
                return _systemReduced.Value;
            }
        }
        /// <summary>测试用:直接指定系统偏好。</summary>
        internal static void OverrideSystemReducedMotion(bool value) { _systemReduced = value; }

        /// <summary>生效档位:关 / 精简 / 完整。跟随系统时,系统要求减少动效则降到「精简」。</summary>
        public static int Level
        {
            get
            {
                int level = Store.MotionLevel;
                if (level < LevelOff || level > LevelFull) level = LevelFull;
                if (Store.MotionFollowSystem && SystemPrefersReducedMotion && level > LevelReduced) level = LevelReduced;
                return level;
            }
        }
        /// <summary>档位名称,用于菜单。</summary>
        public static string LevelName(int level)
        {
            return level == LevelOff ? "关闭" : level == LevelReduced ? "精简" : "完整";
        }
        public static bool Enabled { get { return Level != LevelOff; } }
        /// <summary>页面级/大范围动效是否允许(精简档只保留局部轻微动效)。</summary>
        public static bool AllowsPageMotion { get { return Level == LevelFull; } }

        // ---------------------------------------------------------------- 通道
        /// <summary>开始(或重开)一个通道。档位为关时直接不建通道,Progress 恒为 1。</summary>
        public static void Start(string key, double durationMs, int kind)
        {
            Start(key, durationMs, kind, Rectangle.Empty, false);
        }
        /// <summary>开始一个只影响给定矩形的通道(推荐:动效只重绘自己那一块)。</summary>
        public static void Start(string key, double durationMs, int kind, Rectangle rect)
        {
            Start(key, durationMs, kind, rect, true);
        }
        private static void Start(string key, double durationMs, int kind, Rectangle rect, bool hasRect)
        {
            if (key == null) return;
            if (!Enabled || durationMs <= 0)
            {
                Channels.Remove(key);
                return;
            }
            if (Level == LevelReduced) durationMs = Math.Min(durationMs, 120);   // 精简档:更短、更快结束
            MotionChannel channel = new MotionChannel();
            channel.Start = Now;
            channel.DurationMs = durationMs;
            channel.Kind = kind;
            channel.Rect = rect;
            channel.HasRect = hasRect;
            channel.Finished = false;
            Channels[key] = channel;
        }

        /// <summary>缓动后的进度 0..1;没有该通道(或已结束、档位关闭)时返回 1。</summary>
        public static double Progress(string key)
        {
            MotionChannel channel;
            if (key == null || !Channels.TryGetValue(key, out channel)) return 1;
            if (channel.Finished) return 1;
            double elapsed = (Now - channel.Start) * 1000.0 / Stopwatch.Frequency;
            double t = channel.DurationMs <= 0 ? 1 : elapsed / channel.DurationMs;
            if (t >= 1)
            {
                channel.Finished = true;
                return 1;
            }
            if (t < 0) t = 0;
            return Ease.Apply(channel.Kind, t);
        }

        /// <summary>线性进度 0..1,不做缓动;用于需要自己分段的时间轴。</summary>
        public static double Linear(string key)
        {
            MotionChannel channel;
            if (key == null || !Channels.TryGetValue(key, out channel)) return 1;
            double elapsed = (Now - channel.Start) * 1000.0 / Stopwatch.Frequency;
            double t = channel.DurationMs <= 0 ? 1 : elapsed / channel.DurationMs;
            return t < 0 ? 0 : t > 1 ? 1 : t;
        }

        public static bool Running(string key)
        {
            MotionChannel channel;
            return key != null && Channels.TryGetValue(key, out channel) && !channel.Finished;
        }
        /// <summary>是否还有通道在跑(决定帧定时器是否继续)。</summary>
        public static bool Active
        {
            get
            {
                if (Paused || !Enabled) return false;
                foreach (KeyValuePair<string, MotionChannel> pair in Channels)
                    if (!pair.Value.Finished)
                    {
                        double elapsed = (Now - pair.Value.Start) * 1000.0 / Stopwatch.Frequency;
                        if (elapsed < pair.Value.DurationMs) return true;
                        pair.Value.Finished = true;
                    }
                return false;
            }
        }
        public static int ActiveCount
        {
            get
            {
                int count = 0;
                foreach (KeyValuePair<string, MotionChannel> pair in Channels) if (!pair.Value.Finished) count++;
                return count;
            }
        }
        /// <summary>把已经跑完的通道清掉,避免字典长期膨胀。
        /// 调用方应在收尾那一帧重绘之后再调它。</summary>
        public static void Sweep()
        {
            if (Channels.Count == 0) return;
            Scratch.Clear();
            foreach (KeyValuePair<string, MotionChannel> pair in Channels)
            {
                double elapsed = (Now - pair.Value.Start) * 1000.0 / Stopwatch.Frequency;
                if (pair.Value.Finished || elapsed >= pair.Value.DurationMs) Scratch.Add(pair.Key);
            }
            for (int i = 0; i < Scratch.Count; i++) Channels.Remove(Scratch[i]);
        }
        public static void StopAll()
        {
            Channels.Clear();
        }

        /// <summary>所有正在跑的通道的并集重绘范围。返回空矩形表示「没有需要重绘的」;
        /// 只要有一个通道声明了整窗(页面级),就返回整窗范围。</summary>
        public static Rectangle InvalidateRegion(Rectangle whole)
        {
            bool any = false;
            Rectangle union = Rectangle.Empty;
            foreach (KeyValuePair<string, MotionChannel> pair in Channels)
            {
                MotionChannel channel = pair.Value;
                if (channel.Finished) continue;
                double elapsed = (Now - channel.Start) * 1000.0 / Stopwatch.Frequency;
                if (elapsed >= channel.DurationMs) continue;
                if (!channel.HasRect) return whole;
                union = any ? Rectangle.Union(union, channel.Rect) : channel.Rect;
                any = true;
            }
            return any ? union : Rectangle.Empty;
        }

        /// <summary>按下一次重绘的实测耗时换算下一帧间隔:实测耗时 ×1.4 后夹在 16–40 ms(最快约 60 FPS,最慢 25 FPS),给慢帧留余量。</summary>
        public static int FrameInterval(double lastFrameMs)
        {
            double interval = lastFrameMs <= 0 ? 16 : lastFrameMs * 1.4;
            if (interval < 16) interval = 16;
            if (interval > 40) interval = 40;
            return (int)Math.Round(interval);
        }
        /// <summary>慢帧记账:交给 Program.LogStall,便于用 ui-stalls.log 做回归。</summary>
        public static void RecordFrame(double milliseconds)
        {
            if (milliseconds >= SlowFrameLogMs) Program.LogStall("动画帧", milliseconds / 1000.0);
        }

        // ---------------------------------------------------------------- 追值
        private static readonly Dictionary<string, long> ChaseStamps = new Dictionary<string, long>();

        /// <summary>按半衰期追向目标值(用于波形、悬停高亮这类连续量);首次调用直接落在目标上,
        /// 之后每次调用按两次调用之间的真实时间推进,所以帧率抖动不会改变收敛速度。</summary>
        public static double Approach(string key, double target, double halfLifeMs)
        {
            if (key == null) return target;
            if (!Live || !Enabled || halfLifeMs <= 0)
            {
                Chases[key] = target;
                ChaseStamps[key] = Now;
                return target;
            }
            double current;
            if (!Chases.TryGetValue(key, out current))
            {
                Chases[key] = target;
                ChaseStamps[key] = Now;
                return target;
            }
            long last;
            if (!ChaseStamps.TryGetValue(key, out last)) last = Now;
            double elapsed = Math.Max(0, (Now - last) * 1000.0 / Stopwatch.Frequency);
            ChaseStamps[key] = Now;
            double factor = 1 - Math.Pow(0.5, elapsed / halfLifeMs);
            current += (target - current) * factor;
            Chases[key] = current;
            return current;
        }

        /// <summary>取当前追值(不推进时间)。</summary>
        public static double ChaseValue(string key, double fallback)
        {
            double value;
            return Chases.TryGetValue(key, out value) ? value : fallback;
        }
        public static bool ChaseActive(string key, double target, double epsilon)
        {
            double value;
            return Chases.TryGetValue(key, out value) && Math.Abs(value - target) > epsilon;
        }
        /// <summary>丢弃某个追值(例如切换数据源时)。</summary>
        public static void ForgetChase(string key)
        {
            if (key == null) return;
            Chases.Remove(key);
            ChaseStamps.Remove(key);
            ChaseTargets.Remove(key);
        }
        private static readonly Dictionary<string, double> ChaseTargets = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>是否还有追值没追上目标(决定帧定时器是否继续:数值滚动这类动效不走通道)。</summary>
        public static bool AnyChaseMoving(double epsilon)
        {
            if (!Enabled) return false;
            foreach (KeyValuePair<string, double> pair in Chases)
            {
                double target;
                if (!ChaseTargets.TryGetValue(pair.Key, out target)) continue;
                if (Math.Abs(pair.Value - target) > epsilon) return true;
            }
            return false;
        }
        /// <summary>把所有追值直接落到目标上(档位为关或要立即收尾时用)。</summary>
        public static void SettleChases()
        {
            foreach (KeyValuePair<string, double> pair in ChaseTargets) Chases[pair.Key] = pair.Value;
        }

        /// <summary>数值滚动:让显示值按半衰期追上真实值,并记住目标以便驱动帧。</summary>
        public static double Roll(string key, double target, double halfLifeMs)
        {
            if (key != null) ChaseTargets[key] = target;
            return Approach(key, target, halfLifeMs);
        }
        /// <summary>读数取整后的滚动值:整数指标用它,避免浮点抖动。</summary>
        public static long Roll(string key, long target, double halfLifeMs)
        {
            return (long)Math.Round(Roll(key, (double)target, halfLifeMs));
        }
    }
}
