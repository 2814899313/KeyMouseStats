// ============================================================================
//  键鼠统计 - Unix 毫秒时间基准  UnixTime.cs
//
//  全仓只允许这一个地方把墙钟换成 Unix 毫秒。
//  历史缺陷:DbWriter 曾用 DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond,
//  得到的是「公元 1 年起算」的毫秒,再被 UnixEpoch.AddMilliseconds 当成 1970
//  起算,导致事件日期整体偏移 62,135,596,800,000 ms(约 1969 年)。
// ============================================================================

using System;

namespace KeyMouseStats
{
    internal static class UnixTime
    {
        /// <summary>0001-01-01T00:00:00Z 到 1970-01-01T00:00:00Z 的毫秒数(719162 天)。</summary>
        internal const long YearOneToUnixMs = 62135596800000L;

        private static readonly DateTime Epoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>当前 UTC Unix 毫秒。</summary>
        internal static long NowMs()
        {
            return (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;
        }

        /// <summary>把 UTC DateTime 换成 Unix 毫秒;Local 会先转 UTC,Unspecified 按 UTC 解释。</summary>
        internal static long ToUnixMs(DateTime utc)
        {
            if (utc.Kind == DateTimeKind.Local) utc = utc.ToUniversalTime();
            else if (utc.Kind == DateTimeKind.Unspecified) utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return (long)(utc - Epoch).TotalMilliseconds;
        }

        /// <summary>Unix 毫秒 → 本地时间。仅供诊断/迁移测试使用。</summary>
        internal static DateTime ToLocal(long ms)
        {
            return Epoch.AddMilliseconds(ms).ToLocalTime();
        }
    }
}
